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
    public class ReadPayBillNGRecoveriesPDF3
    {
        /// <summary>
        /// Reads the given PDF file and returns a DataSet with two tables:
        /// "Master" and "EmpNGRecoveries".
        /// </summary>
        public static DataSet ExtractFromPdf(string pdfPath, bool dumpDebugText = true)
        {
            string fullText = ExtractTextByCoordinates(pdfPath);
            List<WordInfo> words = ExtractWordInfos(pdfPath);

            if (dumpDebugText)
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(Path.Combine(dir, "debug_raw_text.txt"), fullText);
                File.WriteAllText(Path.Combine(dir, "debug_normalized_text.txt"),
                    Regex.Replace(fullText, @"[ \t]+", " "));
                Console.WriteLine($"Debug text dumped to: {dir}");
            }

            var ds = new DataSet("NGRecoveryReport");
            ds.Tables.Add(BuildMasterTable(fullText));
            ds.Tables.Add(BuildEmployeeTable(fullText, words, dumpDebugText));
            return ds;
        }

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

        // ------------------------------------------------------------------
        // 1. Extract text using word coordinates, reconstructing proper
        //    reading order line by line (top->bottom, left->right).
        // ------------------------------------------------------------------
        private static string ExtractTextByCoordinates(string pdfPath)
        {
            var sb = new StringBuilder();

            using (var document = PdfDocument.Open(pdfPath))
            {
                foreach (Page page in document.GetPages())
                {
                    var words = page.GetWords().ToList();
                    if (words.Count == 0) continue;

                    const double yTolerance = 3.0;

                    var lines = new List<List<UglyToad.PdfPig.Content.Word>>();

                    foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
                    {
                        double wordY = word.BoundingBox.Bottom;

                        var line = lines.FirstOrDefault(l =>
                            Math.Abs(l[0].BoundingBox.Bottom - wordY) <= yTolerance);

                        if (line != null)
                            line.Add(word);
                        else
                            lines.Add(new List<UglyToad.PdfPig.Content.Word> { word });
                    }

                    foreach (var line in lines)
                    {
                        var ordered = line.OrderBy(w => w.BoundingBox.Left);
                        sb.AppendLine(string.Join(" ", ordered.Select(w => w.Text)));
                    }

                    sb.AppendLine(); // page break marker
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Strip recurring page furniture that can otherwise glue onto the
        // next employee's name right after a page break, e.g.:
        //   "2 of 2 NG Recovery 09-08-2026 18:10:44 *Generated From New SEVAARTH System GOM"
        // "GOM" in particular is a plain capitalized word, so without this
        // it gets swallowed as an extra leading word of the next name.
        // ------------------------------------------------------------------
        private static string StripPageBoilerplate(string text)
        {
            string t = text;
            t = Regex.Replace(t, @"\d+\s+of\s+\d+\s+NG Recovery\s+[\d\-]+\s+[\d:]+", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\*?\s*Generated\s+From\s+New\s+SEVAARTH\s+System\s+GOM", " ", RegexOptions.IgnoreCase);
            return t;
        }

        // ------------------------------------------------------------------
        // 2. Build the "Master" table (report header / meta information)
        // ------------------------------------------------------------------
        private static DataTable BuildMasterTable(string text)
        {
            var dt = new DataTable("Master");
            dt.Columns.Add("ReportTitle", typeof(string));
            dt.Columns.Add("GeneratedOn", typeof(string));
            dt.Columns.Add("ReportMonth", typeof(string));
            dt.Columns.Add("Section", typeof(string));
            dt.Columns.Add("OfficeName", typeof(string));
            dt.Columns.Add("Treasury", typeof(string));
            dt.Columns.Add("DDO", typeof(string));
            dt.Columns.Add("GrandTotalAmount", typeof(decimal));
            dt.Columns.Add("GrandTotalInWords", typeof(string));

            string normalized = Regex.Replace(text, @"\s+", " ");

            var generatedMatch = Regex.Match(normalized, @"NG Recovery\s+([\d\-]+\s+[\d:]+)");
            var monthMatch = Regex.Match(normalized,
                @"For the Month of\s*-\s*([A-Za-z]+\s+\d{4})\s*Section\s*:\s*([A-Za-z ]+?)(?=\s+Name of the Office|\s+Sr)");
            var officeMatch = Regex.Match(normalized,
                @"Name of the Office\s*:\s*(.+?)\s*Treasury\s*:\s*([\w]+)\s*DDO\s*:\s*([\w]+)");
            var grandTotalWordsMatch = Regex.Match(normalized,
                @"Grand Total in Words\s*\(Rs\.?\)\s*:\s*(.+)");

            var totalsBlobMatch = Regex.Match(normalized, @"Total \(Rs\)\s+([\d,\s]+?)(?=Grand Total|$)");
            decimal grandTotalNGDed = 0;
            if (totalsBlobMatch.Success)
            {
                var totalTokens = SplitAmountTokens(totalsBlobMatch.Groups[1].Value);
                if (totalTokens.Count >= 2)
                    grandTotalNGDed = ParseAmount(totalTokens[totalTokens.Count - 2]);
            }

            var row = dt.NewRow();
            row["ReportTitle"] = "Report about Non Government Recoveries";
            row["GeneratedOn"] = generatedMatch.Success ? generatedMatch.Groups[1].Value.Trim() : null;
            row["ReportMonth"] = monthMatch.Success ? monthMatch.Groups[1].Value.Trim() : null;
            row["Section"] = monthMatch.Success ? monthMatch.Groups[2].Value.Trim() : null;
            row["OfficeName"] = officeMatch.Success ? officeMatch.Groups[1].Value.Trim() : null;
            row["Treasury"] = officeMatch.Success ? officeMatch.Groups[2].Value.Trim() : null;
            row["DDO"] = officeMatch.Success ? officeMatch.Groups[3].Value.Trim() : null;
            row["GrandTotalAmount"] = grandTotalNGDed;
            row["GrandTotalInWords"] = grandTotalWordsMatch.Success
                ? grandTotalWordsMatch.Groups[1].Value.Trim()
                : null;

            dt.Rows.Add(row);
            return dt;
        }

        private static List<string> SplitAmountTokens(string blob)
        {
            var raw = blob.Trim()
                          .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                          .ToList();

            var result = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string current = raw[i];

                if (i + 1 < raw.Count &&
                    Regex.IsMatch(current, @"^\d{1,2}(,\d{2})*,\d{1,2}$") &&
                    Regex.IsMatch(raw[i + 1], @"^\d+$") &&
                    !Regex.IsMatch(raw[i + 1], @"^\d{3},"))
                {
                    current = current + raw[i + 1];
                    i++;
                }

                result.Add(current);
            }

            return result;
        }

        private static readonly Regex RowPattern = new Regex(
            @"(?<name>[A-Za-z][A-Za-z\.]*(?:\s+[A-Za-z][A-Za-z\.]*){0,3})\s+" +
            @"(?<sr>\d{1,2})\s+" +
            @"(?:\((?<code1>[A-Z0-9]+)\)\s+)?" +
            @"(?<numbers>(?:[\d,]+\s+){1,}[\d,]+)\s*" +
            @"(?:\((?<code2>[A-Z0-9]+)\)\s*)?" +
            @"\(\s*(?<desig>[A-Za-z][A-Za-z\s]*?)\s*\)",
            RegexOptions.Compiled);

        // ------------------------------------------------------------------
        // 3. Build the "EmpNGRecoveries" table (one row per employee)
        //
        //    Two-pass parsing:
        //      Pass 1 (on the FULL text) just discovers how many numeric
        //      columns this report has - even if row 1 happens to be
        //      corrupted by header adjacency, the majority of other rows
        //      will agree on the count.
        //
        //      Pass 2 locates the exact end of the header block (right
        //      after the Nth "(Rs)"-style marker, N = column count from
        //      pass 1) and re-parses rows ONLY from that point onward, so
        //      header text can never bleed into row 1's match.
        // ------------------------------------------------------------------
        private static DataTable BuildEmployeeTable(string text, List<WordInfo> words, bool dumpDebugText)
        {
            string cleanedText = StripPageBoilerplate(text);
            string normalizedFull = Regex.Replace(cleanedText, @"\s+", " ");

            // ---- Pass 1: discover the numeric column count ----
            var pass1Matches = RowPattern.Matches(normalizedFull).Cast<Match>().ToList();
            var pass1Numbers = pass1Matches.Select(m => SplitAmountTokens(m.Groups["numbers"].Value).Count).ToList();

            int recoveryColumnCount = 0;
            if (pass1Numbers.Count > 0)
            {
                recoveryColumnCount = pass1Numbers
                    .Select(c => c - 3)
                    .Where(c => c >= 0)
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .First().Key;
            }
            int totalNumericColumns = recoveryColumnCount + 3;

            // ---- Locate end of header block: right after the Nth "(Rs)" marker
            //      following "DDO : xxxx" ----
            string dataOnlyText = normalizedFull;
            var ddoMatch = Regex.Match(normalizedFull, @"DDO\s*:\s*[\w]+");
            if (ddoMatch.Success && totalNumericColumns > 0)
            {
                string afterDdo = normalizedFull.Substring(ddoMatch.Index + ddoMatch.Length);
                var rsMatches = Regex.Matches(afterDdo, @"\(?\s*Rs\.?\s*\)?").Cast<Match>().ToList();
                if (rsMatches.Count >= totalNumericColumns)
                {
                    var lastHeaderRs = rsMatches[totalNumericColumns - 1];
                    int dataStartIndex = ddoMatch.Index + ddoMatch.Length + lastHeaderRs.Index + lastHeaderRs.Length;
                    dataOnlyText = normalizedFull.Substring(dataStartIndex);
                }
            }

            // ---- Pass 2: parse rows from the header-free text ----
            var matches = RowPattern.Matches(dataOnlyText).Cast<Match>().ToList();

            var parsedRows = new List<(int Sr, string Name, string Code, string Desig, List<string> Numbers)>();
            var rowDebugLines = new List<string>
            {
                $"recoveryColumnCount={recoveryColumnCount} (totalNumericColumns={totalNumericColumns}); pass1 matches={pass1Matches.Count}; pass2 matches={matches.Count}",
                $"dataOnlyText starts with: \"{(dataOnlyText.Length > 120 ? dataOnlyText.Substring(0, 120) : dataOnlyText)}\""
            };

            foreach (var m in matches)
            {
                var numbers = SplitAmountTokens(m.Groups["numbers"].Value);
                string code = m.Groups["code1"].Success
                    ? m.Groups["code1"].Value.Trim()
                    : (m.Groups["code2"].Success ? m.Groups["code2"].Value.Trim() : null);

                parsedRows.Add((
                    int.Parse(m.Groups["sr"].Value),
                    m.Groups["name"].Value.Trim(),
                    code,
                    m.Groups["desig"].Value.Trim(),
                    numbers));

                rowDebugLines.Add(
                    $"Sr={m.Groups["sr"].Value} Name=\"{m.Groups["name"].Value.Trim()}\" Code={code} " +
                    $"Numbers=[{string.Join(",", numbers)}] (count={numbers.Count}) Desig=\"{m.Groups["desig"].Value.Trim()}\"" +
                    (numbers.Count != totalNumericColumns ? "  <-- SKIPPED (count mismatch)" : ""));
            }

            List<string> allColumnLabels = DetermineNumericColumnNames(words, totalNumericColumns, dumpDebugText);

            var recoveryColumnNames = new List<string>();
            var usedNames = new HashSet<string>();
            for (int i = 0; i < recoveryColumnCount; i++)
            {
                string candidate = (i + 1 < allColumnLabels.Count) ? allColumnLabels[i + 1] : null;
                if (string.IsNullOrWhiteSpace(candidate))
                    candidate = $"UnlabeledRecovery{i + 1}";

                string unique = candidate;
                int suffix = 2;
                while (!usedNames.Add(unique))
                    unique = $"{candidate}_{suffix++}";

                recoveryColumnNames.Add(unique);
            }

            var dt = new DataTable("EmpNGRecoveries");
            dt.Columns.Add("SrNo", typeof(int));
            dt.Columns.Add("EmployeeName", typeof(string));
            dt.Columns.Add("EmployeeCode", typeof(string));
            dt.Columns.Add("Designation", typeof(string));
            dt.Columns.Add("NetPayableAmount", typeof(decimal));
            foreach (var colName in recoveryColumnNames)
                dt.Columns.Add(colName, typeof(decimal));
            dt.Columns.Add("TotalNGDeduction", typeof(decimal));
            dt.Columns.Add("NetAmount", typeof(decimal));

            foreach (var r in parsedRows)
            {
                if (r.Numbers.Count != totalNumericColumns)
                    continue;

                var row = dt.NewRow();
                row["SrNo"] = r.Sr;
                row["EmployeeName"] = r.Name;
                row["EmployeeCode"] = r.Code;
                row["Designation"] = r.Desig;
                row["NetPayableAmount"] = ParseAmount(r.Numbers[0]);
                for (int i = 0; i < recoveryColumnCount; i++)
                    row[recoveryColumnNames[i]] = ParseAmount(r.Numbers[1 + i]);
                row["TotalNGDeduction"] = ParseAmount(r.Numbers[r.Numbers.Count - 2]);
                row["NetAmount"] = ParseAmount(r.Numbers[r.Numbers.Count - 1]);
                dt.Rows.Add(row);
            }

            if (dumpDebugText)
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(Path.Combine(dir, "debug_row_matches.txt"), string.Join(Environment.NewLine, rowDebugLines));
            }

            return dt;
        }

        // ------------------------------------------------------------------
        // Coordinate-based header-label reconstruction.
        //
        // Column bands come from the X-position of the actual DATA NUMBERS,
        // not from header "(Rs)" positions - header cells wrap onto a
        // different number of lines depending on label length, so their
        // "(Rs)" tokens don't reliably share the same Y even when they're
        // visually "the same row" (fixed row height + vertical centering).
        // X-position doesn't have that problem.
        //
        // Only "clean" amount-shaped tokens are trusted for finding column
        // positions: a bare "0" or an Indian comma-grouped number like
        // "1,10,213". This deliberately excludes Sr.No. digits (no comma,
        // never "0"), Treasury/DDO codes (too long / no comma), and page
        // markers like "1 of 2" (no comma, not "0").
        // ------------------------------------------------------------------
        private static List<string> DetermineNumericColumnNames(List<WordInfo> allWords, int totalNumericColumns, bool dumpDebugText)
        {
            var fallback = Enumerable.Repeat((string)null, Math.Max(totalNumericColumns, 0)).ToList();
            var debugLines = new List<string>();

            if (totalNumericColumns <= 0 || allWords == null || allWords.Count == 0)
            {
                if (dumpDebugText) WriteHeaderDebug(new List<string> { "No words or no numeric columns - using fallback." });
                return fallback;
            }

            bool IsCleanAmount(string t) => t == "0" || Regex.IsMatch(t, @"^\d{1,2}(,\d{2})*,\d{3}$");
            bool IsRsLike(string t) => t.Length <= 6 && Regex.IsMatch(t, @"Rs\.?", RegexOptions.IgnoreCase);

            var amountWords = allWords.Where(w => IsCleanAmount(w.Text)).ToList();
            debugLines.Add($"Clean amount-shaped words found: {amountWords.Count}");

            if (amountWords.Count == 0)
            {
                debugLines.Add("No amount words found - using fallback.");
                if (dumpDebugText) WriteHeaderDebug(debugLines);
                return fallback;
            }

            var ddoWord = allWords.FirstOrDefault(w => string.Equals(w.Text, "DDO", StringComparison.OrdinalIgnoreCase));
            int headerPage = ddoWord?.PageIndex ?? amountWords.Min(w => w.PageIndex);

            var pageAmountWords = amountWords.Where(w => w.PageIndex == headerPage).ToList();
            if (pageAmountWords.Count == 0)
            {
                debugLines.Add($"No clean amount words on header page {headerPage} - using fallback.");
                if (dumpDebugText) WriteHeaderDebug(debugLines);
                return fallback;
            }

            double maxDataY = pageAmountWords.Max(w => w.Bottom);
            double headerTopY = ddoWord != null ? ddoWord.Bottom : maxDataY + 150.0;
            debugLines.Add($"headerPage={headerPage} maxDataY={maxDataY:F1} headerTopY={headerTopY:F1}");

            // ---- Anchor each column using its "(Rs)" header token ----
            // Each numeric column has exactly one "(Rs)" marker in the header
            // block, sitting at that column's true horizontal center - unlike
            // data-derived bands, this doesn't collapse for columns that happen
            // to be "0" (i.e. visually narrow) in every row.
            var rsWords = allWords
                .Where(w => w.PageIndex == headerPage && w.Bottom > maxDataY && w.Bottom < headerTopY && IsRsLike(w.Text))
                .OrderBy(w => w.XCenter)
                .ToList();

            debugLines.Add($"(Rs) marker words found in header band: {rsWords.Count} (expected {totalNumericColumns})");

            if (rsWords.Count != totalNumericColumns)
            {
                debugLines.Add("(Rs) marker count mismatch - using fallback.");
                if (dumpDebugText) WriteHeaderDebug(debugLines);
                return fallback;
            }

            var anchors = rsWords.Select(w => w.XCenter).ToList();

            // ---- Assign every other header word to its nearest anchor ----
            var headerWords = allWords
                .Where(w => w.PageIndex == headerPage && w.Bottom > maxDataY && w.Bottom < headerTopY && !IsRsLike(w.Text))
                .ToList();

            var columnBuckets = Enumerable.Range(0, totalNumericColumns).Select(_ => new List<WordInfo>()).ToList();
            foreach (var w in headerWords)
            {
                int nearest = 0;
                double best = double.MaxValue;
                for (int i = 0; i < anchors.Count; i++)
                {
                    double d = Math.Abs(w.XCenter - anchors[i]);
                    if (d < best) { best = d; nearest = i; }
                }
                columnBuckets[nearest].Add(w);
            }

            var labels = new List<string>();
            for (int i = 0; i < totalNumericColumns; i++)
            {
                var ordered = columnBuckets[i].OrderByDescending(w => w.Bottom).ThenBy(w => w.Left).ToList();
                string raw = string.Join(" ", ordered.Select(w => w.Text));
                string cleaned = CleanHeaderLabel(raw);
                labels.Add(cleaned);
                debugLines.Add($"Col{i}: anchorX={anchors[i]:F1} raw=\"{raw}\" -> \"{cleaned}\"");
            }

            if (dumpDebugText) WriteHeaderDebug(debugLines);
            return labels;
        }

        private static void WriteHeaderDebug(List<string> lines)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            File.WriteAllText(Path.Combine(dir, "debug_column_headers.txt"), string.Join(Environment.NewLine, lines));
        }

        // Splits words into exactly targetClusters groups by sorting them on
        // keySelector and cutting at the (targetClusters - 1) largest gaps
        // between consecutive key values. Robust to arbitrary column widths
        // since it doesn't rely on a fixed distance threshold, and keeps the
        // actual member words (not just a numeric range) so callers can
        // compute each cluster's real Left/Right extent afterward.
        private static List<List<WordInfo>> ClusterWordsByGaps(List<WordInfo> words, Func<WordInfo, double> keySelector, int targetClusters)
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

        // Turns a raw header chunk like "Co. Op. Bank 1" or "Other Recover y 2"
        // into a clean DataTable-friendly column name like "CoOpBank1" /
        // "OtherRecovery2". Returns null for blank input.
        private static string CleanHeaderLabel(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return null;

            string label = chunk.Trim();

            // Rejoin words split mid-word by PDF line-wrapping: a normal
            // word directly followed by a lone trailing letter
            // (e.g. "Recover" + "y" -> "Recovery", "Amoun" + "t" -> "Amount").
            label = Regex.Replace(label, @"(\S+) ([a-z])(?=\s|$)", "$1$2");

            label = Regex.Replace(label, @"[^\w\s]", " ");
            label = Regex.Replace(label, @"\s+", " ").Trim();

            var wordsArr = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (wordsArr.Length == 0) return null;

            var sb = new StringBuilder();
            foreach (var w in wordsArr)
            {
                if (w.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(w[0]));
                sb.Append(w.Length > 1 ? w.Substring(1) : "");
            }

            string result = sb.ToString();
            if (result.Length > 0 && char.IsDigit(result[0]))
                result = "Col" + result;

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        // ------------------------------------------------------------------
        // Helper: Indian-format numbers like "1,41,751" -> 141751
        // ------------------------------------------------------------------
        private static decimal ParseAmount(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            string cleaned = raw.Replace(",", "").Trim();
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
                ? val
                : 0;
        }
    }

}
