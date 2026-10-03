using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadPayBillITReport1
    {
        // Line A: "(AGRJBBM7201)-Jalindar Baban Bare"  — code + name, no sr number here
        private static readonly Regex CodeNameRegex = new(
            @"^\s*\(\s*(?<code>[A-Za-z0-9]+)\s*\)\s*-\s*(?<name>.+?)\s*$",
            RegexOptions.Compiled);

        // Line B: "1 AOBPA6391K 93,478 1,000"  — sr + PAN + gross + deduction, no designation
        private static readonly Regex SrPanAmountRegex = new(
            @"^\s*\d+\s+(?<pan>[A-Z]{5}\d{4}[A-Z])\s+[\d,]+\s+[\d,]+\s*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses every .pdf in the given folder and returns the combined list of
        /// EmployeeCode / EmployeeName / PAN across all files.
        /// </summary>
        public static List<IncomeTaxEmpDetail1> ParseFolder(string folderPath)
        {
            var results = new List<IncomeTaxEmpDetail1>();

            foreach (var file in Directory.GetFiles(folderPath, "*.pdf").OrderBy(f => f))
            {
                results.AddRange(ExtractFromPdf(file));
            }

            return results;
        }

        public static List<IncomeTaxEmpDetail1> ExtractFromPdf(string pdfPath)
        {
            var text = ExtractText(pdfPath);
            return ParseText(text);
        }

        /// <summary>
        /// Debug helper: returns the reconstructed line-by-line text for a PDF without
        /// parsing it, in case the regexes need tuning against the real layout.
        /// </summary>
        public static string GetRawReconstructedText(string pdfPath) => ExtractText(pdfPath);

        private static string ExtractText(string pdfPath)
        {
            using var document = PdfDocument.Open(pdfPath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(ReconstructLines(page));
            }
            return sb.ToString();
        }

        private static string ReconstructLines(Page page)
        {
            const double yTolerance = 3.0; // points; tune up slightly if rows still merge/split

            var words = page.GetWords().ToList();
            if (words.Count == 0) return string.Empty;

            var rows = new List<List<Word>>();

            foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
            {
                var row = rows.FirstOrDefault(r =>
                    Math.Abs(r[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= yTolerance);

                if (row != null)
                    row.Add(word);
                else
                    rows.Add(new List<Word> { word });
            }

            var lines = rows.Select(r =>
                string.Join(" ", r.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

            return string.Join("\n", lines);
        }

        private static List<IncomeTaxEmpDetail1> ParseText(string text)
        {
            var employees = new List<IncomeTaxEmpDetail1>();
            var lines = text.Split('\n').Select(l => l.Trim()).ToArray();

            for (int i = 0; i < lines.Length; i++)
            {
                // Anchor on the code+name line.
                var codeName = CodeNameRegex.Match(lines[i]);
                if (!codeName.Success) continue;

                // The sr+PAN+amount line comes AFTER the code+name line in this layout.
                for (int j = i + 1; j < Math.Min(i + 3, lines.Length); j++)
                {
                    var srPan = SrPanAmountRegex.Match(lines[j]);
                    if (!srPan.Success) continue;

                    employees.Add(new IncomeTaxEmpDetail1
                    {
                        EmployeeCode = codeName.Groups["code"].Value.Trim(),
                        EmployeeName = Regex.Replace(codeName.Groups["name"].Value, @"\s+", " ").Trim(),
                        PanNumber = srPan.Groups["pan"].Value.Trim()
                    });

                    i = j; // skip past the consumed line
                    break;
                }
            }

            return employees;
        }
    }

    public class IncomeTaxEmpDetail1
    {
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string PanNumber { get; set; }
    }
}
