using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadEmpPayDrawReport1
    {
        private class WordInfo
        {
            public int PageIndex;
            public string Text;
            public double Left;
            public double Right;
            public double Bottom;
            public double XCenter => (Left + Right) / 2.0;
        }

        private const int ExpectedMonthColumns = 12;

        // Fixed print order used only as a last-resort fallback when a column's
        // header text can't be confidently matched to a dictionary entry.
        private static readonly string[] PositionalMonthFallback =
        {
            "March", "April", "May", "June", "July", "August",
            "September", "October", "November", "December", "January", "February"
        };

        // Correctly-spelled Marathi -> English month map. NOTE: PdfPig extracts
        // Devanagari conjuncts/matras in visual glyph order, not logical Unicode
        // order, so text actually pulled from these PDFs does NOT equal these
        // keys verbatim (e.g. "जुलै" comes out as separate fragments "जलै" +
        // "ु"). Matching is done via FuzzyMatchMonth, which strips vowel signs
        // and compares consonant-skeleton overlap so fragmentation/reordering
        // doesn't break the match.
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

        private const double MonthFuzzyMatchThreshold = 0.35;

        // ------------------------------------------------------------------
        // Public entry points
        // ------------------------------------------------------------------

        public static DataSet ExtractFromPdf(string pdfPath, bool dumpDebugText = true)
        {
            var ds = new DataSet("PayDrawnReport");
            ds.Tables.Add(BuildEmployeeMasterTable());
            ProcessPdfInto(pdfPath, ds, dumpDebugText);
            return ds;
        }

        /// <summary>
        /// Parses several PDFs into one combined DataSet. Assumes every PDF
        /// uses the same report template (12 months, same column order); the
        /// "PayDrawnGrid" table's month columns are created from the first
        /// successfully-parsed page and reused (by position) for the rest.
        /// </summary>
        public static DataSet ExtractFromPdfs(IEnumerable<string> pdfPaths, bool dumpDebugText = true)
        {
            var ds = new DataSet("PayDrawnReport");
            ds.Tables.Add(BuildEmployeeMasterTable());
            foreach (var path in pdfPaths)
                ProcessPdfInto(path, ds, dumpDebugText, Path.GetFileName(path));
            return ds;
        }

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

        // ------------------------------------------------------------------
        // Core processing for one PDF
        // ------------------------------------------------------------------

        private static void ProcessPdfInto(string pdfPath, DataSet ds, bool dumpDebugText, string sourceFileLabel = null)
        {
            string sourceFile = sourceFileLabel ?? Path.GetFileName(pdfPath);
            var employeeTable = ds.Tables["EmployeeMaster"];

            var words = ExtractWordInfos(pdfPath);
            var debugLines = new List<string>();

            var pages = words.Select(w => w.PageIndex).Distinct().OrderBy(p => p).ToList();

            foreach (int pageIndex in pages)
            {
                var pageWords = words.Where(w => w.PageIndex == pageIndex).ToList();
                var lines = GroupWordsIntoLines(pageWords);

                // ---- Header / prose fields (Name, ID, DDO, Period) ----
                string pageText = string.Join(" ", lines.SelectMany(l => l.OrderBy(w => w.Left).Select(w => w.Text)));
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

                // ---- Locate the month header line (used only to LABEL columns) ----
                var headerLine = FindMonthHeaderLine(lines, debugLines);
                if (headerLine == null)
                {
                    debugLines.Add($"[Page {pageIndex}] Could not locate month header line - skipping grid data on this page.");
                    continue;
                }
                double headerBottom = headerLine[0].Bottom;

                // ---- Derive reliable column anchors from fully-populated data rows ----
                var dataLines = lines.Where(l => l.Count > 0 && l[0].Bottom < headerBottom).ToList();
                var anchors = FindReferenceColumnAnchors(dataLines, debugLines);
                if (anchors == null)
                {
                    debugLines.Add($"[Page {pageIndex}] Could not find a fully-populated (12-column) reference row - " +
                                    "falling back to header-text clustering (less reliable).");
                    anchors = ClusterHeaderTextAnchors(headerLine, debugLines);
                }
                if (anchors == null || anchors.Count != ExpectedMonthColumns)
                {
                    debugLines.Add($"[Page {pageIndex}] Could not resolve {ExpectedMonthColumns} column anchors - skipping grid data on this page.");
                    continue;
                }

                var monthNames = LabelAnchorsFromHeader(headerLine, anchors, debugLines);

                // ---- Walk every data line, bucket its values by nearest anchor ----
                var paramOrder = new List<string>();
                var paramValues = new Dictionary<string, Dictionary<int, string>>(); // paramName -> (colIndex 0-based -> rawValue)

                foreach (var line in dataLines)
                {
                    var ordered = line.OrderBy(w => w.Left).ToList();

                    int splitAt = ordered.Count;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (LooksLikeValue(ordered[i].Text)) { splitAt = i; break; }
                    }

                    var labelWords = ordered.Take(splitAt).ToList();
                    var valueWords = ordered.Skip(splitAt).ToList();
                    if (labelWords.Count == 0 || valueWords.Count == 0) continue;

                    string paramName = Regex.Replace(string.Join(" ", labelWords.Select(w => w.Text)).Trim(), @"\s+", " ");
                    if (paramName.Length == 0) continue;
                    if (Regex.IsMatch(paramName, @"^(For|Employee)\b", RegexOptions.IgnoreCase)) continue;

                    if (!paramValues.ContainsKey(paramName))
                    {
                        paramValues[paramName] = new Dictionary<int, string>();
                        paramOrder.Add(paramName);
                    }

                    foreach (var vw in valueWords)
                    {
                        int colIndex = NearestAnchorIndex(vw.XCenter, anchors);
                        // Later values for the same column in the same row overwrite
                        // earlier ones only if that slot is still empty - keeps the
                        // first (leftmost) match, which is what we want since values
                        // are processed in left-to-right order already.
                        if (!paramValues[paramName].ContainsKey(colIndex))
                            paramValues[paramName][colIndex] = vw.Text;
                    }
                }

                // ---- Build/extend the shared "PayDrawnGrid" table ----
                DataTable gridTable = ds.Tables["PayDrawnGrid"];
                if (gridTable == null)
                {
                    gridTable = new DataTable("PayDrawnGrid");
                    gridTable.Columns.Add("SourceFile", typeof(string));
                    gridTable.Columns.Add("PageIndex", typeof(int));
                    gridTable.Columns.Add("SevaarthID", typeof(string));
                    gridTable.Columns.Add("EmployeeName", typeof(string));
                    gridTable.Columns.Add("Description", typeof(string));

                    var usedNames = new HashSet<string> { "SourceFile", "PageIndex", "SevaarthID", "EmployeeName", "Description" };
                    foreach (var name in monthNames)
                    {
                        string colName = string.IsNullOrWhiteSpace(name) ? "Month" : name;
                        string unique = colName;
                        int suffix = 2;
                        while (!usedNames.Add(unique))
                            unique = $"{colName}_{suffix++}";
                        gridTable.Columns.Add(unique, typeof(string));
                    }
                    ds.Tables.Add(gridTable);
                }

                int monthColumnCount = gridTable.Columns.Count - 5; // minus the 5 fixed leading columns
                foreach (var paramName in paramOrder)
                {
                    var row = gridTable.NewRow();
                    row["SourceFile"] = sourceFile;
                    row["PageIndex"] = pageIndex;
                    row["SevaarthID"] = (object)sevaarthId ?? DBNull.Value;
                    row["EmployeeName"] = (object)employeeName ?? DBNull.Value;
                    row["Description"] = paramName;

                    var values = paramValues[paramName];
                    for (int col = 0; col < monthColumnCount && col < anchors.Count; col++)
                    {
                        row[5 + col] = values.TryGetValue(col, out var v) ? (object)v : DBNull.Value;
                    }
                    gridTable.Rows.Add(row);
                }

                debugLines.Add($"[Page {pageIndex}] Built grid rows for {paramOrder.Count} parameters across {anchors.Count} month columns.");
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
        // Column anchor derivation
        // ------------------------------------------------------------------

        /// <summary>
        /// Looks for one or more data lines whose values fill exactly
        /// ExpectedMonthColumns slots (e.g. "Total Allownance" / "Total
        /// Deduction", which print an explicit 0 per month rather than
        /// omitting blanks). Averages their value X-centers column-by-column
        /// (after sorting each candidate line's values left-to-right) to get
        /// robust, evenly-reliable anchors independent of header-text quirks.
        /// </summary>
        private static List<double> FindReferenceColumnAnchors(List<List<WordInfo>> dataLines, List<string> debugLines)
        {
            var candidateSortedValues = new List<List<WordInfo>>();

            foreach (var line in dataLines)
            {
                var ordered = line.OrderBy(w => w.Left).ToList();
                int splitAt = ordered.Count;
                for (int i = 0; i < ordered.Count; i++)
                    if (LooksLikeValue(ordered[i].Text)) { splitAt = i; break; }

                var valueWords = ordered.Skip(splitAt).ToList();
                if (valueWords.Count == ExpectedMonthColumns)
                    candidateSortedValues.Add(valueWords.OrderBy(w => w.Left).ToList());
            }

            if (candidateSortedValues.Count == 0)
                return null;

            debugLines.Add($"Found {candidateSortedValues.Count} fully-populated ({ExpectedMonthColumns}-column) reference row(s) for anchor derivation.");

            var anchors = new List<double>();
            for (int col = 0; col < ExpectedMonthColumns; col++)
            {
                double avg = candidateSortedValues.Average(rowVals => rowVals[col].XCenter);
                anchors.Add(avg);
            }

            debugLines.Add("Reference column anchors (X): " + string.Join(", ", anchors.Select(a => a.ToString("F1"))));
            return anchors;
        }

        /// <summary>Fallback used only if no fully-populated reference row exists:
        /// gap-clusters the header line's own words into columns. Less reliable
        /// since Devanagari month-name widths vary a lot.</summary>
        private static List<double> ClusterHeaderTextAnchors(List<WordInfo> headerLine, List<string> debugLines)
        {
            var ordered = headerLine.OrderBy(w => w.Left).ToList();
            var clusters = ClusterWordsByGaps(ordered, w => w.Left, ExpectedMonthColumns + 1);

            List<List<WordInfo>> monthClusters;
            if (clusters.Count == ExpectedMonthColumns + 1)
                monthClusters = clusters.Skip(1).ToList(); // drop leading "Month" label cluster
            else if (clusters.Count > ExpectedMonthColumns)
                monthClusters = clusters.Skip(clusters.Count - ExpectedMonthColumns).ToList();
            else
                monthClusters = ClusterWordsByGaps(ordered, w => w.Left, ExpectedMonthColumns);

            if (monthClusters.Count != ExpectedMonthColumns) return null;
            return monthClusters.Select(c => c.Average(w => w.XCenter)).ToList();
        }

        /// <summary>
        /// Finds the line containing the Marathi month names: mostly
        /// Devanagari script with roughly the right token count (generous,
        /// since a single month name can fragment into 2-3 tokens).
        /// </summary>
        private static List<WordInfo> FindMonthHeaderLine(List<List<WordInfo>> lines, List<string> debugLines)
        {
            bool IsDevanagari(string t) => t.Any(c => c >= '\u0900' && c <= '\u097F');

            var candidate = lines.FirstOrDefault(l =>
                l.Count(w => IsDevanagari(w.Text)) >= 8 && l.Count >= ExpectedMonthColumns);

            if (candidate == null) return null;

            var ordered = candidate.OrderBy(w => w.Left).ToList();
            debugLines.Add($"Month header line: {ordered.Count} tokens, Y={ordered[0].Bottom:F1}");
            debugLines.Add("  Raw tokens: " + string.Join(" | ", ordered.Select(w => w.Text)));
            return ordered;
        }

        /// <summary>
        /// Labels each already-known anchor by snapping header words to their
        /// nearest anchor (discarding any header word - typically the leading
        /// "Month" label - that sits farther from every anchor than half the
        /// smallest inter-anchor gap, so it doesn't get misattributed to
        /// column 0), then fuzzy-matching each anchor's accumulated text
        /// against the Marathi->English dictionary.
        /// </summary>
        private static List<string> LabelAnchorsFromHeader(List<WordInfo> headerLine, List<double> anchors, List<string> debugLines)
        {
            double minGap = double.MaxValue;
            for (int i = 1; i < anchors.Count; i++)
                minGap = Math.Min(minGap, anchors[i] - anchors[i - 1]);
            double maxAttachDistance = minGap / 2.0;

            var buckets = Enumerable.Range(0, anchors.Count).Select(_ => new List<WordInfo>()).ToList();
            foreach (var w in headerLine.OrderBy(x => x.Left))
            {
                int nearest = NearestAnchorIndex(w.XCenter, anchors);
                double dist = Math.Abs(w.XCenter - anchors[nearest]);
                if (dist <= maxAttachDistance)
                    buckets[nearest].Add(w);
            }

            // Fuzzy-match header text per anchor as a DIAGNOSTIC ONLY. In
            // practice this is unreliable: Devanagari pre-base vowel signs
            // (e.g. the "ि" in "डिसेंबर"/December, which renders to the LEFT
            // of its own consonant) get extracted by PdfPig as glyphs that
            // can attach to the wrong neighboring word/anchor, producing a
            // cascading off-by-one label shift across several consecutive
            // months (observed: March..July each resolving one month late,
            // self-correcting at August, then repeating September..January
            // before correcting again at February). The header text is
            // therefore not trustworthy as the source of truth for naming.
            var diagnosticMatch = new List<string>();
            var diagnosticScore = new List<double>();
            for (int i = 0; i < anchors.Count; i++)
            {
                string clusterText = string.Join("", buckets[i].OrderBy(w => w.Left).Select(w => w.Text));
                var (bestName, bestScore) = FuzzyMatchMonth(clusterText);
                diagnosticMatch.Add(bestScore >= MonthFuzzyMatchThreshold ? bestName : null);
                diagnosticScore.Add(bestScore);
            }

            // AUTHORITATIVE labels: the report always prints the 12 months in
            // the same fixed left-to-right order, and the anchors themselves
            // are already derived from reliable data-row X-positions in that
            // same left-to-right order (see FindReferenceColumnAnchors). So
            // position is trusted over header-text content.
            var positional = new List<string>();
            for (int i = 0; i < anchors.Count; i++)
                positional.Add(i < PositionalMonthFallback.Length ? PositionalMonthFallback[i] : $"Month{i + 1}");

            for (int i = 0; i < anchors.Count; i++)
            {
                string note = diagnosticMatch[i] != null && diagnosticMatch[i] != positional[i]
                    ? $" <-- header fuzzy-match disagreed (got \"{diagnosticMatch[i]}\"); using positional order instead"
                    : "";
                debugLines.Add($"  Col{i}: anchorX={anchors[i]:F1} -> \"{positional[i]}\" (header fuzzy score={diagnosticScore[i]:F2}){note}");
            }

            return positional;
        }

        // ------------------------------------------------------------------
        // Fuzzy Devanagari matching
        // ------------------------------------------------------------------

        private static (string EnglishName, double Score) FuzzyMatchMonth(string extractedText)
        {
            string bestName = null;
            double bestScore = 0;
            foreach (var kvp in MarathiToEnglishMonth)
            {
                double score = DevanagariSkeletonOverlap(extractedText, kvp.Key);
                if (score > bestScore) { bestScore = score; bestName = kvp.Value; }
            }
            return (bestName, bestScore);
        }

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

        private static List<WordInfo> ExtractWordInfos(string pdfPath)
        {
            var result = new List<WordInfo>();
            using (var document = PdfDocument.Open(pdfPath))
            {
                int pageIndex = 0;
                foreach (Page page in document.GetPages())
                {
                    foreach (var w in page.GetWords())
                    {
                        result.Add(new WordInfo
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

        private static List<List<WordInfo>> GroupWordsIntoLines(List<WordInfo> pageWords)
        {
            const double yTolerance = 3.0;
            var lines = new List<List<WordInfo>>();

            foreach (var word in pageWords.OrderByDescending(w => w.Bottom))
            {
                var line = lines.FirstOrDefault(l => Math.Abs(l[0].Bottom - word.Bottom) <= yTolerance);
                if (line != null) line.Add(word);
                else lines.Add(new List<WordInfo> { word });
            }
            return lines;
        }

        /// <summary>A value token always starts with a digit (amounts, dates,
        /// voucher/bill numbers); every parameter label in this report starts
        /// with a letter.</summary>
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

        private static List<List<WordInfo>> ClusterWordsByGaps(
            List<WordInfo> words, Func<WordInfo, double> keySelector, int targetClusters)
        {
            var result = new List<List<WordInfo>>();
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

            var current = new List<WordInfo> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (splitIndices.Contains(i - 1))
                {
                    result.Add(current);
                    current = new List<WordInfo>();
                }
                current.Add(sorted[i]);
            }
            result.Add(current);
            return result;
        }

    }
}
