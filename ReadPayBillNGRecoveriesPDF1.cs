using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadPayBillNGRecoveriesPDF1
    {
        /// <summary>
        /// Reads the given PDF file and returns a DataSet with two tables:
        /// "Master" and "EmpNGRecoveries".
        /// </summary>
        public static DataSet ExtractFromPdf(string pdfPath, bool dumpDebugText = false)
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


        // ------------------------------------------------------------------
        // 3. Build the "EmpNGRecoveries" table (one row per employee)
        // ------------------------------------------------------------------
        private static DataTable BuildEmployeeTable(string text, List<WordInfo> words, bool dumpDebugText)
        {
            string normalized = Regex.Replace(text, @"\s+", " ");

            var rowPattern = new Regex(
                @"(?<name>[A-Za-z][A-Za-z\.]*(?:\s+[A-Za-z][A-Za-z\.]*){0,3})\s+" +
                @"(?<sr>\d{1,2})\s+" +
                @"(?:\((?<code1>[A-Z0-9]+)\)\s+)?" +
                @"(?<numbers>(?:[\d,]+\s+){1,}[\d,]+)\s*" +
                @"(?:\((?<code2>[A-Z0-9]+)\)\s*)?" +
                @"\(\s*(?<desig>[A-Za-z][A-Za-z\s]*?)\s*\)",
                RegexOptions.Compiled);

            var matches = rowPattern.Matches(normalized).Cast<Match>().ToList();

            var parsedRows = new List<(int Sr, string Name, string Code, string Desig, List<string> Numbers)>();

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
            }

            // Determine how many numeric columns this report has. Normally
            // every row agrees; if one row is off (line-wrap glitch), go
            // with whatever count is most common.
            int recoveryColumnCount = 0;
            if (parsedRows.Count > 0)
            {
                recoveryColumnCount = parsedRows
                    .Select(r => r.Numbers.Count - 3) // minus NetPayable, TotalNGDed, NetAmount
                    .Where(c => c >= 0)
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .First().Key;
            }

            int totalNumericColumns = recoveryColumnCount + 3;
            List<string> allColumnLabels = DetermineNumericColumnNames(words, totalNumericColumns, dumpDebugText);

            // allColumnLabels[0] = NetPayableAmount's raw label (unused - fixed name kept)
            // allColumnLabels[1 .. totalNumericColumns-3] = the recovery columns we actually want named
            // allColumnLabels[totalNumericColumns-2], [totalNumericColumns-1] = TotalNGDed/NetAmount (unused - fixed names kept)
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
                int expectedCount = recoveryColumnCount + 3;
                if (r.Numbers.Count != expectedCount)
                    continue; // don't risk misaligning a malformed row

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

            return dt;
        }

        private static List<string> DetermineNumericColumnNames(List<WordInfo> words, int totalNumericColumns, bool dumpDebugText)
        {
            var fallback = Enumerable.Range(1, totalNumericColumns).Select(i => (string)null).ToList();

            if (totalNumericColumns <= 0 || words == null || words.Count == 0)
                return fallback;

            // "(Rs)" may or may not stay as one token depending on how
            // PdfPig segmented it - match anything short containing "Rs".
            var rsWords = words
                .Where(w => w.Text.Length <= 6 && Regex.IsMatch(w.Text, @"Rs\.?", RegexOptions.IgnoreCase))
                .ToList();

            if (rsWords.Count < totalNumericColumns)
                return fallback;

            // Group "(Rs)" words by (page, roughly-same Y) to find the
            // header's own "(Rs)" row - it should have exactly
            // totalNumericColumns of them together on one line.
            var grouped = rsWords
                .GroupBy(w => new { w.PageIndex, YBucket = Math.Round(w.Bottom / 2.0) * 2.0 })
                .OrderByDescending(g => g.Count())
                .ToList();

            var headerGroup = grouped.FirstOrDefault(g => g.Count() == totalNumericColumns)
                               ?? grouped.FirstOrDefault();

            if (headerGroup == null)
                return fallback;

            int headerPage = headerGroup.Key.PageIndex;
            double approxY = headerGroup.Key.YBucket;

            var headerRsWords = rsWords
                .Where(w => w.PageIndex == headerPage && Math.Abs(w.Bottom - approxY) <= 4.0)
                .OrderBy(w => w.Left)
                .ToList();

            if (headerRsWords.Count != totalNumericColumns)
                return fallback;

            double headerRowY = headerRsWords.Average(w => w.Bottom);

            // Column bands: bounded by midpoints between adjacent "(Rs)"
            // centers. First/last band left open-ended - irrelevant since
            // those two columns (NetPayableAmount / NetAmount) keep fixed
            // names and their raw text is discarded regardless.
            var centers = headerRsWords.Select(w => w.XCenter).ToList();
            var bands = new List<(double Min, double Max)>();
            for (int i = 0; i < centers.Count; i++)
            {
                double min = i == 0 ? double.NegativeInfinity : (centers[i - 1] + centers[i]) / 2.0;
                double max = i == centers.Count - 1 ? double.PositiveInfinity : (centers[i] + centers[i + 1]) / 2.0;
                bands.Add((min, max));
            }

            // Upper bound for header text: below the "...DDO : xxxx" line
            // if we can find it, else a generous fixed height above the
            // "(Rs)" row.
            var ddoWord = words.FirstOrDefault(w => w.PageIndex == headerPage &&
                                                     string.Equals(w.Text, "DDO", StringComparison.OrdinalIgnoreCase));
            double headerTopY = ddoWord != null ? ddoWord.Bottom : headerRowY + 130.0;

            var labels = new List<string>();
            var debugLines = new List<string>();

            for (int i = 0; i < totalNumericColumns; i++)
            {
                var (min, max) = bands[i];
                var colWords = words
                    .Where(w => w.PageIndex == headerPage &&
                                w.Bottom > headerRowY && w.Bottom < headerTopY &&
                                w.XCenter >= min && w.XCenter < max &&
                                !(w.Text.Length <= 6 && Regex.IsMatch(w.Text, @"Rs\.?", RegexOptions.IgnoreCase)))
                    .OrderByDescending(w => w.Bottom)
                    .ThenBy(w => w.Left)
                    .ToList();

                string raw = string.Join(" ", colWords.Select(w => w.Text));
                string cleaned = CleanHeaderLabel(raw);
                labels.Add(cleaned);

                debugLines.Add($"Col{i}: band=[{min:F1},{max:F1}) raw=\"{raw}\" -> \"{cleaned}\"");
            }

            if (dumpDebugText)
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(Path.Combine(dir, "debug_column_headers.txt"), string.Join(Environment.NewLine, debugLines));
            }

            return labels;
        }

        private static string CleanHeaderLabel(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return null;

            string label = chunk.Trim();

            // Rejoin words split mid-word by PDF line-wrapping: a normal
            // word directly followed by a lone trailing letter
            // (e.g. "Recover" + "y" -> "Recovery", "Amoun" + "t" -> "Amount").
            label = Regex.Replace(label, @"(\S+) ([a-z])(?=\s|$)", "$1$2");

            // Drop periods/punctuation, keep letters/digits/spaces only.
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

    public class WordInfo
    {
        public int PageIndex;
        public string Text;
        public double Left;
        public double Right;
        public double Bottom; // PdfPig origin is bottom-left; higher Bottom = higher on the page
        public double XCenter => (Left + Right) / 2.0;
    }
}
