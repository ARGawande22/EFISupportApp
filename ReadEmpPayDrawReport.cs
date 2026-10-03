using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadEmpPayDrawReport
    {
        // Column order as printed in the report header (Marathi month names).
        // Used only for labeling MonthIndex -> MonthName; column POSITIONS are
        // always derived from the actual header word coordinates, never assumed.
        private static readonly string[] MonthNames =
        {
            "March", "April", "May", "June", "July", "August",
            "September", "October", "November", "December", "January", "February"
        };

        private const int ExpectedMonthColumns = 12;

        // Correctly-spelled Marathi -> English month map. NOTE: PdfPig extracts
        // Devanagari conjuncts/matras in visual glyph order, not logical Unicode
        // order, so the text actually pulled from these PDFs does NOT equal
        // these keys verbatim (e.g. "जुलै" comes out as separate fragments
        // "जलै" + "ु"). Do not dictionary[extractedText] directly - use
        // FuzzyMatchMonth below, which strips vowel signs and compares
        // consonant-skeleton overlap so fragmentation/reordering doesn't break
        // the match.
        public static readonly Dictionary<string, string> MarathiToEnglishMonth = new Dictionary<string, string>
        {
            { "जानेवारी", "January" },
            { "फेब्रुवारी", "February" },
            { "मार्च", "March" },
            { "एप्रिल", "April" },
            { "मे", "May" },
            { "जून", "June" },
            { "जुलै", "July" },
            { "ऑगस्ट", "August" },
            { "सप्टेंबर", "September" },
            { "ऑक्टोंबर", "October" },
            { "नोव्हेंबर", "November" },
            { "डिसेंबर", "December" }
        };

        // Below this score (0..1, character-bag Jaccard overlap on consonant
        // skeletons) a fuzzy match is not trusted, and the positional fallback
        // (MonthNames[], based on the report's fixed left-to-right layout) is
        // used instead.
        private const double MonthFuzzyMatchThreshold = 0.35;


        // ------------------------------------------------------------------
        // Public entry points
        // ------------------------------------------------------------------

        public static DataSet ExtractFromPdf(string pdfPath, bool dumpDebugText = true)
        {
            var ds = new DataSet("PayDrawnReport");
            ds.Tables.Add(BuildEmployeeMasterTable());
            ds.Tables.Add(BuildLineItemsTable());

            ProcessPdfInto(pdfPath, ds, dumpDebugText);
            return ds;
        }

        /// <summary>
        /// Convenience overload: parses several PDFs (e.g. one per employee,
        /// or multiple periods) into a single combined DataSet.
        /// </summary>
        public static DataSet ExtractFromPdfs(IEnumerable<string> pdfPaths, bool dumpDebugText = true)
        {
            var ds = new DataSet("PayDrawnReport");
            ds.Tables.Add(BuildEmployeeMasterTable());
            ds.Tables.Add(BuildLineItemsTable());

            foreach (var path in pdfPaths)
                ProcessPdfInto(path, ds, dumpDebugText, Path.GetFileName(path));

            return ds;
        }

        // ------------------------------------------------------------------
        // Table schemas
        // ------------------------------------------------------------------

        private static DataTable BuildEmployeeMasterTable()
        {
            var dt = new DataTable("EmployeeMaster");
            dt.Columns.Add("SourceFile", typeof(string));
            dt.Columns.Add("PageIndex", typeof(int));
            dt.Columns.Add("EmployeeName", typeof(string));
            dt.Columns.Add("SevaarthID", typeof(string));
            dt.Columns.Add("DDOCode", typeof(string));
            dt.Columns.Add("DDOOfficeName", typeof(string));
            dt.Columns.Add("Period", typeof(string));
            return dt;
        }

        private static DataTable BuildLineItemsTable()
        {
            var dt = new DataTable("PayDrawnLineItems");
            dt.Columns.Add("SourceFile", typeof(string));
            dt.Columns.Add("PageIndex", typeof(int));
            dt.Columns.Add("SevaarthID", typeof(string));
            dt.Columns.Add("EmployeeName", typeof(string));
            dt.Columns.Add("ParameterName", typeof(string));
            dt.Columns.Add("MonthIndex", typeof(int));     // 1..12, per report's own column order
            dt.Columns.Add("MonthName", typeof(string));   // best-effort English label
            dt.Columns.Add("RawValue", typeof(string));    // exact extracted text (safe for dates, IDs, etc.)
            dt.Columns.Add("NumericValue", typeof(decimal)); // parsed amount when RawValue is a plain number; DBNull otherwise
            return dt;
        }

        // ------------------------------------------------------------------
        // Core processing for one PDF
        // ------------------------------------------------------------------

        private static void ProcessPdfInto(string pdfPath, DataSet ds, bool dumpDebugText, string sourceFileLabel = null)
        {
            string sourceFile = sourceFileLabel ?? Path.GetFileName(pdfPath);
            var employeeTable = ds.Tables["EmployeeMaster"];
            var lineItemsTable = ds.Tables["PayDrawnLineItems"];

            var words = ExtractWordInfo1s(pdfPath);
            var debugLines = new List<string>();

            var pages = words.Select(w => w.PageIndex).Distinct().OrderBy(p => p).ToList();

            foreach (int pageIndex in pages)
            {
                var pageWords = words.Where(w => w.PageIndex == pageIndex).ToList();
                var lines = GroupWordsIntoLines(pageWords);

                // Flattened page text (reading order) - used only for the
                // header fields (Name / ID / DDO / Period), which are plain
                // prose rather than a coordinate-aligned grid.
                string pageText = string.Join(" ",
                    lines.SelectMany(l => l.OrderBy(w => w.Left).Select(w => w.Text)));
                pageText = Regex.Replace(pageText, @"\s+", " ");

                string employeeName = null, sevaarthId = null, ddoCode = null, ddoOffice = null, period = null;

                var periodMatch = Regex.Match(pageText, @"For The Period of\s*([\d\-]+)");
                if (periodMatch.Success) period = periodMatch.Groups[1].Value.Trim();

                var nameMatch = Regex.Match(pageText, @"Employee Name\s*:\s*(.+?)\s*Sevaarth ID\s*:\s*(\S+)");
                if (nameMatch.Success)
                {
                    employeeName = nameMatch.Groups[1].Value.Trim();
                    sevaarthId = nameMatch.Groups[2].Value.Trim();
                }

                // "DDO CODE : 2210001669(Current DDO) DDO Office Name : Taluka Agriculture office Purandhar"
                // The office name runs up to the start of the (Devanagari) month-header text.
                var ddoMatch = Regex.Match(pageText,
                    @"DDO CODE\s*:\s*(\S+?)\(Current DDO\)\s*DDO Office Name\s*:\s*(.+?)(?=\s+[\u0900-\u097F]|$)");
                if (ddoMatch.Success)
                {
                    ddoCode = ddoMatch.Groups[1].Value.Trim();
                    ddoOffice = ddoMatch.Groups[2].Value.Trim();
                }

                var erow = employeeTable.NewRow();
                erow["SourceFile"] = sourceFile;
                erow["PageIndex"] = pageIndex;
                erow["EmployeeName"] = (object)employeeName ?? DBNull.Value;
                erow["SevaarthID"] = (object)sevaarthId ?? DBNull.Value;
                erow["DDOCode"] = (object)ddoCode ?? DBNull.Value;
                erow["DDOOfficeName"] = (object)ddoOffice ?? DBNull.Value;
                erow["Period"] = (object)period ?? DBNull.Value;
                employeeTable.Rows.Add(erow);

                // ---- Locate the month-header line and derive 12 column anchors ----
                var monthAnchors = FindMonthColumnAnchors(lines, debugLines);
                if (monthAnchors == null)
                {
                    debugLines.Add($"[Page {pageIndex}] Could not locate month header - skipping line items on this page.");
                    continue;
                }

                double headerBottom = monthAnchors.HeaderLineBottom;

                // ---- Walk every remaining line as a parameter row ----
                foreach (var line in lines)
                {
                    if (line.Count == 0) continue;
                    double lineY = line[0].Bottom;
                    if (lineY >= headerBottom) continue; // skip header itself and anything above/at it

                    var ordered = line.OrderBy(w => w.Left).ToList();

                    int splitAt = ordered.Count;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (LooksLikeValue(ordered[i].Text)) { splitAt = i; break; }
                    }

                    var labelWords = ordered.Take(splitAt).ToList();
                    var valueWords = ordered.Skip(splitAt).ToList();
                    if (labelWords.Count == 0 || valueWords.Count == 0) continue;

                    string paramName = string.Join(" ", labelWords.Select(w => w.Text)).Trim();
                    paramName = Regex.Replace(paramName, @"\s+", " ");
                    if (paramName.Length == 0) continue;
                    // Skip stray boilerplate lines that might slip through (page numbers, DDO CODE line
                    // right after "(Current DDO)" prose that already got captured above, etc.)
                    if (Regex.IsMatch(paramName, @"^(For|Employee|DDO CODE\s*$)", RegexOptions.IgnoreCase))
                        continue;

                    foreach (var vw in valueWords)
                    {
                        int monthIdx = NearestAnchorIndex(vw.XCenter, monthAnchors.Anchors);

                        var lrow = lineItemsTable.NewRow();
                        lrow["SourceFile"] = sourceFile;
                        lrow["PageIndex"] = pageIndex;
                        lrow["SevaarthID"] = (object)sevaarthId ?? DBNull.Value;
                        lrow["EmployeeName"] = (object)employeeName ?? DBNull.Value;
                        lrow["ParameterName"] = paramName;
                        lrow["MonthIndex"] = monthIdx + 1;
                        lrow["MonthName"] = monthIdx >= 0 && monthIdx < monthAnchors.ResolvedNames.Count
                            ? monthAnchors.ResolvedNames[monthIdx]
                            : (monthIdx >= 0 && monthIdx < MonthNames.Length ? MonthNames[monthIdx] : null);
                        lrow["RawValue"] = vw.Text;

                        decimal? numeric = TryParseAmount(vw.Text);
                        lrow["NumericValue"] = numeric.HasValue ? (object)numeric.Value : DBNull.Value;

                        lineItemsTable.Rows.Add(lrow);
                    }
                }
            }

            if (dumpDebugText)
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(
                    Path.Combine(dir, $"debug_paydrawn_{Path.GetFileNameWithoutExtension(pdfPath)}.txt"),
                    string.Join(Environment.NewLine, debugLines));
            }
        }

        // ------------------------------------------------------------------
        // Month header detection
        // ------------------------------------------------------------------

        private class MonthAnchorResult
        {
            public List<double> Anchors;         // 12 X-center positions, left to right
            public List<string> ResolvedNames;   // best-effort English month name per anchor (dictionary match, or positional fallback)
            public List<double> MatchScores;     // fuzzy-match confidence per anchor, for debugging
            public double HeaderLineBottom;       // Y of the header line, to separate it from data rows
        }

        /// <summary>
        /// Finds the line containing the Marathi month names and derives 12
        /// X-position anchors from it. Devanagari conjuncts sometimes get
        /// extracted as multiple separate word tokens by PdfPig (a single
        /// month name splitting into 2-3 glyph fragments), so raw word count
        /// on that line is unreliable. Instead we gap-cluster the line's
        /// words by X position: fragments of the same month sit close
        /// together (small gap) while distinct months are spaced apart
        /// (large gap), and the leading "Month" label word forms its own
        /// cluster that we discard.
        /// </summary>
        private static MonthAnchorResult FindMonthColumnAnchors(List<List<WordInfo1>> lines, List<string> debugLines)
        {
            bool IsDevanagari(string t) => t.Any(c => c >= '\u0900' && c <= '\u097F');

            // A candidate header line: mostly Devanagari script, roughly the
            // right token count for "1 label word + 12 (possibly fragmented)
            // month names" - be generous since fragmentation inflates the count.
            var candidate = lines.FirstOrDefault(l =>
                l.Count(w => IsDevanagari(w.Text)) >= 8 &&
                l.Count >= ExpectedMonthColumns);

            if (candidate == null) return null;

            var ordered = candidate.OrderBy(w => w.Left).ToList();
            debugLines.Add($"Month header candidate line: {ordered.Count} tokens, Y={ordered[0].Bottom:F1}");
            debugLines.Add("  Raw tokens: " + string.Join(" | ", ordered.Select(w => w.Text)));

            // Cluster into (label + 12 months) = 13 groups by the biggest gaps.
            var clusters = ClusterWordsByGaps(ordered, w => w.Left, ExpectedMonthColumns + 1);

            List<List<WordInfo1>> monthClusters;
            if (clusters.Count == ExpectedMonthColumns + 1)
            {
                // Drop the leftmost cluster (the "Month" label word itself).
                monthClusters = clusters.Skip(1).ToList();
            }
            else if (clusters.Count > ExpectedMonthColumns)
            {
                // More clusters than expected: assume any extras are still on
                // the label side and keep the rightmost 12.
                monthClusters = clusters.Skip(clusters.Count - ExpectedMonthColumns).ToList();
            }
            else
            {
                // Fewer clusters than 12 (over-merged) - re-cluster targeting
                // exactly 12 groups across the whole line as a fallback.
                monthClusters = ClusterWordsByGaps(ordered, w => w.Left, ExpectedMonthColumns);
                if (monthClusters.Count != ExpectedMonthColumns)
                {
                    debugLines.Add($"  Could not resolve to {ExpectedMonthColumns} month columns (got {monthClusters.Count}).");
                    return null;
                }
            }

            var anchors = monthClusters.Select(c => c.Average(w => w.XCenter)).ToList();
            debugLines.Add("  Resolved month anchors (X): " + string.Join(", ", anchors.Select(a => a.ToString("F1"))));

            // Try to name each column via the dictionary (fuzzy match against
            // the cluster's own extracted text); fall back to the report's
            // known fixed positional order when confidence is too low.
            var resolvedNames = new List<string>();
            var matchScores = new List<double>();
            for (int i = 0; i < monthClusters.Count; i++)
            {
                string clusterText = string.Join("", monthClusters[i].OrderBy(w => w.Left).Select(w => w.Text));
                var (bestName, bestScore) = FuzzyMatchMonth(clusterText);

                string positionalFallback = i < MonthNames.Length ? MonthNames[i] : null;

                if (bestScore >= MonthFuzzyMatchThreshold)
                {
                    resolvedNames.Add(bestName);
                    matchScores.Add(bestScore);
                }
                else
                {
                    resolvedNames.Add(positionalFallback);
                    matchScores.Add(bestScore);
                    debugLines.Add(
                        $"  Col{i}: fuzzy match too weak (best=\"{bestName}\" score={bestScore:F2} for text=\"{clusterText}\") " +
                        $"- falling back to positional \"{positionalFallback}\".");
                }

                debugLines.Add($"  Col{i}: clusterText=\"{clusterText}\" -> resolved=\"{resolvedNames[i]}\" (score={bestScore:F2})");
            }

            return new MonthAnchorResult
            {
                Anchors = anchors,
                ResolvedNames = resolvedNames,
                MatchScores = matchScores,
                HeaderLineBottom = ordered[0].Bottom
            };
        }

        /// <summary>
        /// Matches a raw extracted Devanagari fragment against
        /// MarathiToEnglishMonth by stripping vowel signs/virama/nukta from
        /// both sides and scoring character-bag overlap (Jaccard-style). This
        /// is deliberately order- and completeness-insensitive, since PdfPig
        /// can both reorder and split a single month's glyphs across tokens.
        /// </summary>
        private static (string EnglishName, double Score) FuzzyMatchMonth(string extractedText)
        {
            string bestName = null;
            double bestScore = 0;

            foreach (var kvp in MarathiToEnglishMonth)
            {
                double score = DevanagariSkeletonOverlap(extractedText, kvp.Key);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestName = kvp.Value;
                }
            }

            return (bestName, bestScore);
        }

        /// <summary>Strips Devanagari vowel signs, virama, nukta, anusvara,
        /// visarga, chandrabindu and joiners, leaving just the "skeleton"
        /// of independent vowels/consonants - the part that survives glyph
        /// reordering fairly reliably.</summary>
        private static string StripDevanagariMarks(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if (ch >= '\u093E' && ch <= '\u094D') continue; // vowel signs + virama
                if (ch == '\u093C') continue; // nukta
                if (ch == '\u0901' || ch == '\u0902' || ch == '\u0903') continue; // chandrabindu/anusvara/visarga
                if (ch == '\u200C' || ch == '\u200D') continue; // ZWNJ/ZWJ
                if (char.IsWhiteSpace(ch)) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static double DevanagariSkeletonOverlap(string a, string b)
        {
            string sa = StripDevanagariMarks(a);
            string sb = StripDevanagariMarks(b);
            if (sa.Length == 0 || sb.Length == 0) return 0;

            var bagA = sa.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            var bagB = sb.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

            int common = 0;
            foreach (var kv in bagA)
                if (bagB.TryGetValue(kv.Key, out int cnt)) common += Math.Min(cnt, kv.Value);

            int union = bagA.Values.Sum() + bagB.Values.Sum() - common;
            return union == 0 ? 0 : (double)common / union;
        }

        // ------------------------------------------------------------------
        // Word / line extraction helpers
        // ------------------------------------------------------------------

        private static List<WordInfo1> ExtractWordInfo1s(string pdfPath)
        {
            var result = new List<WordInfo1>();

            using (var document = PdfDocument.Open(pdfPath))
            {
                int pageIndex = 0;
                foreach (Page page in document.GetPages())
                {
                    foreach (var w in page.GetWords())
                    {
                        result.Add(new WordInfo1
                        {
                            PageIndex = pageIndex,
                            Text = w.Text,
                            Left = w.BoundingBox.Left,
                            Right = w.BoundingBox.Right,
                            Bottom = w.BoundingBox.Bottom
                        });
                    }
                    pageIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// Groups a single page's words into text lines (top -> bottom), each
        /// line kept as an unordered-by-X list of WordInfo1 (caller sorts by
        /// Left when it needs reading order). Mirrors the line-grouping logic
        /// already used in ReadPayBillNGRecoveriesPDF4.
        /// </summary>
        private static List<List<WordInfo1>> GroupWordsIntoLines(List<WordInfo1> pageWords)
        {
            const double yTolerance = 3.0;
            var lines = new List<List<WordInfo1>>();

            foreach (var word in pageWords.OrderByDescending(w => w.Bottom))
            {
                var line = lines.FirstOrDefault(l => Math.Abs(l[0].Bottom - word.Bottom) <= yTolerance);
                if (line != null)
                    line.Add(word);
                else
                    lines.Add(new List<WordInfo1> { word });
            }

            return lines;
        }

        /// <summary>A value token always starts with a digit (amounts, dates,
        /// voucher/bill numbers); every parameter label in this report starts
        /// with a letter, so this simple check reliably separates the two.</summary>
        private static bool LooksLikeValue(string text) => text.Length > 0 && char.IsDigit(text[0]);

        private static int NearestAnchorIndex(double x, List<double> anchors)
        {
            int nearest = 0;
            double best = double.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                double d = Math.Abs(x - anchors[i]);
                if (d < best) { best = d; nearest = i; }
            }
            return nearest;
        }

        /// <summary>
        /// Splits words into exactly targetClusters groups by sorting on
        /// keySelector and cutting at the (targetClusters - 1) largest gaps
        /// between consecutive key values. (Same approach as
        /// ReadPayBillNGRecoveriesPDF4.ClusterWordsByGaps.)
        /// </summary>
        private static List<List<WordInfo1>> ClusterWordsByGaps(
            List<WordInfo1> words, Func<WordInfo1, double> keySelector, int targetClusters)
        {
            var result = new List<List<WordInfo1>>();
            var sorted = words.OrderBy(keySelector).ToList();
            if (sorted.Count == 0) return result;

            if (targetClusters <= 1 || sorted.Count == 1)
            {
                result.Add(sorted);
                return result;
            }

            var keys = sorted.Select(keySelector).ToList();
            var gaps = new List<double>();
            for (int i = 1; i < keys.Count; i++)
                gaps.Add(keys[i] - keys[i - 1]);

            int splitsNeeded = Math.Min(targetClusters - 1, gaps.Count);

            var splitIndices = gaps
                .Select((g, idx) => (Gap: g, Index: idx))
                .OrderByDescending(t => t.Gap)
                .Take(splitsNeeded)
                .Select(t => t.Index)
                .ToHashSet();

            var current = new List<WordInfo1> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (splitIndices.Contains(i - 1))
                {
                    result.Add(current);
                    current = new List<WordInfo1>();
                }
                current.Add(sorted[i]);
            }
            result.Add(current);

            return result;
        }

        /// <summary>Indian-format numbers like "1,41,751" -> 141751. Returns
        /// null (rather than 0) for anything that isn't a plain number, so
        /// dates and mixed tokens are left as null/NumericValue = DBNull
        /// and only available via RawValue.</summary>
        private static decimal? TryParseAmount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.Contains("/") || raw.Contains("-") || raw.Contains(":")) return null; // dates/timestamps
            string cleaned = raw.Replace(",", "").Trim();
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
                ? val
                : (decimal?)null;
        }
    }

    public class WordInfo1
    {
        public int PageIndex;
        public string Text;
        public double Left;
        public double Right;
        public double Bottom;
        public double XCenter => (Left + Right) / 2.0;
    }
}
