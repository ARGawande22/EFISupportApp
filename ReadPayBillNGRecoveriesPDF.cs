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
    public class ReadPayBillNGRecoveriesPDF
    {
        /// <summary>
        /// Reads the given PDF file and returns a DataSet with two tables:
        /// "Master" and "EmpNGRecoveries".
        /// </summary>
        public static DataSet ExtractFromPdf(string pdfPath, bool dumpDebugText = false)
        {
            string fullText = ExtractTextByCoordinates(pdfPath);

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
            ds.Tables.Add(BuildEmployeeTable(fullText));
            return ds;
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

                    // Group words into visual lines: words whose vertical
                    // center is within a small tolerance of each other are
                    // considered part of the same line. PdfPig's Y axis has
                    // origin at bottom-left, so higher Y = higher on page.
                    const double yTolerance = 3.0; // points; tweak if lines merge/split incorrectly

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

                    // Each line's words sorted left-to-right by X position
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
            var totalsLineMatch = Regex.Match(normalized,
                @"Total \(Rs\)\s*([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)");

            decimal grandTotalNGDed = totalsLineMatch.Success
                ? ParseAmount(totalsLineMatch.Groups[7].Value)
                : 0;

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

        // ------------------------------------------------------------------
        // 3. Build the "EmpNGRecoveries" table (one row per employee)
        // ------------------------------------------------------------------
        private static DataTable BuildEmployeeTable(string text)
        {
            var dt = new DataTable("EmpNGRecoveries");
            dt.Columns.Add("SrNo", typeof(int));
            dt.Columns.Add("EmployeeName", typeof(string));
            dt.Columns.Add("EmployeeCode", typeof(string));
            dt.Columns.Add("Designation", typeof(string));
            dt.Columns.Add("NetPayableAmount", typeof(decimal));
            dt.Columns.Add("Column2", typeof(decimal));      // unlabeled 2nd column in the source report (always 0 in samples seen so far)
            dt.Columns.Add("CoOpBank1", typeof(decimal));
            dt.Columns.Add("CoOpCrSoc2", typeof(decimal));
            dt.Columns.Add("CoOpCrSoc1", typeof(decimal));
            dt.Columns.Add("CoOpBank2", typeof(decimal));
            dt.Columns.Add("TotalNGDeduction", typeof(decimal));
            dt.Columns.Add("NetAmount", typeof(decimal));

            // Collapse all whitespace/newlines to single spaces so a logical
            // record reads as one contiguous chunk regardless of how many
            // visual lines it originally spanned.
            string normalized = Regex.Replace(text, @"\s+", " ");

            // Real word order confirmed from the extracted text is:
            //   <Name (2-4 words)> <SrNo> [(Code)] <8 numbers> [(Code)] (<Designation>)
            // The employee code sometimes appears right after the Sr No, and
            // sometimes after the numbers instead - both are optional here,
            // and whichever one is present gets captured (code1 or code2).
            //
            // Example A: "Ashish Bajarang Gaikwad 1 44,720 0 5,612 0 0 13,385 18,997 25,723 (AGRABGM8704) (Agriculture Assistant)"
            // Example B: "Atul Vitthal Hingane 4 (AGRAVHM8802) 58,440 0 14,271 0 0 2,500 16,771 41,669 (Agriculture Assistant)"
            var rowPattern = new Regex(
                @"(?<name>[A-Za-z][A-Za-z\.]*(?:\s+[A-Za-z][A-Za-z\.]*){0,3})\s+" +
                @"(?<sr>\d{1,2})\s+" +
                @"(?:\((?<code1>[A-Z0-9]+)\)\s+)?" +
                @"(?<n1>[\d,]+)\s+(?<n2>[\d,]+)\s+(?<n3>[\d,]+)\s+(?<n4>[\d,]+)\s+" +
                @"(?<n5>[\d,]+)\s+(?<n6>[\d,]+)\s+(?<n7>[\d,]+)\s+(?<n8>[\d,]+)\s*" +
                @"(?:\((?<code2>[A-Z0-9]+)\)\s*)?" +
                @"\(\s*(?<desig>[A-Za-z][A-Za-z\s]*?)\s*\)",
                RegexOptions.Compiled);

            foreach (Match m in rowPattern.Matches(normalized))
            {
                var row = dt.NewRow();
                row["SrNo"] = int.Parse(m.Groups["sr"].Value);
                row["EmployeeName"] = m.Groups["name"].Value.Trim();
                row["EmployeeCode"] = m.Groups["code1"].Success
                    ? m.Groups["code1"].Value.Trim()
                    : m.Groups["code2"].Value.Trim();
                row["Designation"] = m.Groups["desig"].Value.Trim();
                row["NetPayableAmount"] = ParseAmount(m.Groups["n1"].Value);
                row["Column2"] = ParseAmount(m.Groups["n2"].Value);
                row["CoOpBank1"] = ParseAmount(m.Groups["n3"].Value);
                row["CoOpCrSoc2"] = ParseAmount(m.Groups["n4"].Value);
                row["CoOpCrSoc1"] = ParseAmount(m.Groups["n5"].Value);
                row["CoOpBank2"] = ParseAmount(m.Groups["n6"].Value);
                row["TotalNGDeduction"] = ParseAmount(m.Groups["n7"].Value);
                row["NetAmount"] = ParseAmount(m.Groups["n8"].Value);
                dt.Rows.Add(row);
            }

            return dt;
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
