using EFISupportApp.Models.PaySlip;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    // ---------------------------------------------------------------------
    // Parser  (one PDF page == one pay slip)
    //
    //  1. Each PDF page is parsed on its own (no cross-page merging).
    //  2. Words are grouped into lines by BASELINE Y, not BoundingBox.Top,
    //     so ":" / "e" / "(DGP...)" no longer break a line apart.
    //  3. Designation is read by geometry (it can wrap onto a 2nd line).
    // ---------------------------------------------------------------------
    public class ReadPaySlipPDF2
    {
        private const double BaselineTolerance = 3.0;

        private static double BaseY(Word w) => w.Letters[0].StartBaseLine.Y;

        public static List<Employee> Parse(string pdfPath)
        {
            var pages = ExtractPages(pdfPath);
            var employees = new List<Employee>();

            for (int i = 0; i < pages.Count; i++)
            {
                var emp = ParseEmployeeBlock(pages[i]);
                if (emp == null)
                {
                    Console.WriteLine($"WARN: page {i + 1}: no employee found");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(emp.Name) || string.IsNullOrWhiteSpace(emp.Designation) ||
                    string.IsNullOrWhiteSpace(emp.BillNo) || string.IsNullOrWhiteSpace(emp.VoucherNumber))
                {
                    Console.WriteLine($"WARN: page {i + 1}: incomplete parse for '{emp.EmployeeCode}'");
                }
                employees.Add(emp);
            }

            if (employees.Count != pages.Count)
                Console.WriteLine($"WARN: {pages.Count} pages but {employees.Count} employees parsed");

            return employees;
        }

        public static List<VoucherGroup> GroupByVoucher(IEnumerable<Employee> employees)
        {
            return employees
                .GroupBy(e => new
                {
                    VoucherNumber = (e.VoucherNumber ?? "").Trim(),
                    VoucherDate = (e.VoucherDate ?? "").Trim(),
                    BillNo = (e.BillNo ?? "").Trim()
                })
                .Select(g => new VoucherGroup
                {
                    VoucherNumber = g.Key.VoucherNumber,
                    VoucherDate = g.Key.VoucherDate,
                    BillNo = g.Key.BillNo,
                    BillDescription = g.Select(e => e.BillDescription).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "",
                    GrossAmt = g.Select(e => e.GrossAmt).FirstOrDefault(v => v.HasValue),
                    NetAmt = g.Select(e => e.NetAmt).FirstOrDefault(v => v.HasValue),
                    DdoCode = g.Select(e => e.DdoCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "",
                    SalaryMonth = g.Select(e => e.SalaryMonth).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "",
                    VoucherAmount = g.Sum(e => e.ITaxAmount),
                    Employees = g.ToList()
                })
                .OrderBy(v => v.VoucherNumber)
                .ToList();
        }

        // -- Step 1: one list of visual lines per PDF page ------------------

        private static List<List<PdfLine>> ExtractPages(string pdfPath)
        {
            var pages = new List<List<PdfLine>>();

            using var document = PdfDocument.Open(pdfPath);
            foreach (Page page in document.GetPages())
            {
                var lines = ClusterWordsIntoLines(page.GetWords())
                    .Select(words => new PdfLine
                    {
                        Y = BaseY(words[0]),
                        Words = words
                    })
                    .OrderByDescending(l => l.Y)
                    .ToList();

                pages.Add(lines);
            }
            return pages;
        }

        // Groups words into visual lines by baseline. A new line starts when
        // the baseline drops by more than the tolerance relative to the FIRST
        // word of the current line (no chaining). Each line is left-to-right.
        private static List<List<Word>> ClusterWordsIntoLines(IEnumerable<Word> wordsIn, double tolerance = BaselineTolerance)
        {
            var sorted = wordsIn
                .Where(w => w.Letters.Count > 0)
                .Select(w => new { Word = w, Y = BaseY(w) })
                .OrderByDescending(x => x.Y)
                .ToList();

            var lines = new List<List<Word>>();
            var lineY = new List<double>();

            foreach (var item in sorted)
            {
                if (lines.Count == 0 || lineY[^1] - item.Y > tolerance)
                {
                    lines.Add(new List<Word>());
                    lineY.Add(item.Y);
                }
                lines[^1].Add(item.Word);
            }

            foreach (var line in lines)
                line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

            return lines;
        }

        private static string GetReadingOrderText(IEnumerable<Word> wordsIn)
        {
            var lines = ClusterWordsIntoLines(wordsIn);
            return string.Join(" ", lines.Select(line => string.Join(" ", line.Select(w => w.Text))));
        }

        // -- Step 2: parse one page ------------------------------------------

        private static Employee ParseEmployeeBlock(List<PdfLine> block)
        {
            var allWords = block.SelectMany(l => l.Words).ToList();
            string fullText = GetReadingOrderText(allWords);

            if (!fullText.Contains("Employee Name")) return null;

            var emp = new Employee();

            emp.Office = Match1(fullText, @"Pay Slip\s*(.*?)\s*DDO CODE");
            emp.DdoCode = Match1(fullText, @"DDO CODE\s*:\s*(\d+)");

            // Name ends at "Designation :" (normal) or "Salary Month :" (when the
            // designation wrapped and its first line was read before this one).
            var nameMatch = Regex.Match(fullText,
                @"Employee Name\s*:\s*\(([^)]+)\)\s*(.+?)\s*(?:Designation\s*:|Salary Month\s*:)");
            if (nameMatch.Success)
            {
                emp.EmployeeCode = nameMatch.Groups[1].Value.Trim();
                emp.Name = nameMatch.Groups[2].Value.Trim();
            }

            emp.SalaryMonth = Match1(fullText, @"Salary Month\s*:\s*([A-Za-z]+\s*\d{4})");
            emp.Designation = ExtractDesignation(allWords);

            emp.DateOfBirth = Match1(fullText, @"Date of Birth\s*:\s*([\d/]+)");
            emp.DateOfJoining = Match1(fullText, @"Date of Joining\s*:\s*([\d/]+)");
            emp.DateOfRetirement = Match1(fullText, @"Date of Retirement\s*:\s*([\d/]+)");

            emp.UidNo = Match1(fullText, @"Uid No\s*:\s*(\S+)");
            emp.PayCommission = Match1(fullText, @"Pay Commission\s*:\s*(.+?)\s*(?:Pay Level|Level)\s*:");
            emp.Level = Match1(fullText, @"Level\s*:\s*(.+?)\s*(?:GPF/DCPS|PRAN|Bank A/c|IFSC|Mobile|:)");

            emp.GpfDcpsAccNo = Match1(fullText, @"GPF/DCPS AC\. No\.\s*:\s*(\S+)");
            emp.PranNo = Match1(fullText, @"PRAN No\.\s*:\s*(\S+)");
            emp.BankAccNo = Match1(fullText, @"Bank A/c No\s*:\s*(\S+)");
            emp.IfscCode = Match1(fullText, @"IFSC Code\s*:\s*(\S+)");
            emp.BasicPay = MatchDecimal(fullText, @"Basic Pay\s*:\s*(\d+(?:\.\d+)?)");

            emp.MobileNo = Match1(fullText, @"Mobile No\s*:\s*(\S+)");
            emp.PanNo = Match1(fullText, @"PAN NO\s*:\s*(\S+)");

            var netPay = Regex.Match(fullText, @"Net Pay\s*:\s*(\d+(?:\.\d+)?)\s*\(([^)]+)\)");
            if (netPay.Success)
            {
                emp.NetPayAmount = decimal.Parse(netPay.Groups[1].Value, CultureInfo.InvariantCulture);
                emp.NetPayWords = netPay.Groups[2].Value.Trim();
            }

            emp.BillNo = Match1(fullText, @"Bill No\s*:-\s*(\S+)");
            emp.BillDescription = Match1(fullText, @"Bill Description\s*:-\s*(.+?)\s*Gross Amt");
            emp.GrossAmt = MatchDecimal(fullText, @"Gross Amt\s*:-\s*(\d+(?:\.\d+)?)");
            emp.NetAmt = MatchDecimal(fullText, @"Net Amt\s*:-\s*(\d+(?:\.\d+)?)");
            emp.VoucherNumber = Match1(fullText, @"voucher Number\s*:-\s*(\S+)");
            emp.VoucherDate = Match1(fullText, @"Voucher Date\s*:-\s*(\S+)");

            ParseTable(block, emp);

            return emp;
        }

        // Designation may wrap onto a 2nd line ("Deputy Superintendent of Police"
        // + "(Unarmed)"), which scrambles plain text order. Use geometry: every
        // word inside the Designation column, from the label's line down to just
        // above the "Date of Joining" line.
        private static string ExtractDesignation(List<Word> words)
        {
            var label = words.FirstOrDefault(w => w.Text.StartsWith("Designation", StringComparison.OrdinalIgnoreCase));
            if (label == null) return "";

            var salary = words.FirstOrDefault(w => w.Text == "Salary" && w.BoundingBox.Left > label.BoundingBox.Left);
            var joining = words.FirstOrDefault(w => w.Text == "Joining");
            if (salary == null) return "";

            double labelY = BaseY(label);
            double leftLimit = label.BoundingBox.Left - 2;
            double rightLimit = salary.BoundingBox.Left - 1;
            double lowerLimit = joining != null ? BaseY(joining) + 3 : double.MinValue; // PDF Y grows upward

            var colWords = words.Where(w =>
                    !ReferenceEquals(w, label) &&
                    w.Text != ":" &&
                    w.BoundingBox.Left >= leftLimit &&
                    w.BoundingBox.Left < rightLimit &&
                    BaseY(w) <= labelY + 3 &&      // on/below the label line
                    BaseY(w) > lowerLimit)         // above the Date-of-Joining row
                .ToList();

            return GetReadingOrderText(colWords).Trim();
        }

        // -- Step 3: parse the 3-section, variable-row table ------------------

        private static void ParseTable(List<PdfLine> block, Employee emp)
        {
            int headerIdx = block.FindIndex(l =>
                l.Text.Contains("Particulars") && l.Text.Contains("Amount"));

            int totalIdx = block.FindIndex(l => l.Text.Contains("Total Emolument"));

            if (headerIdx < 0 || totalIdx < 0 || totalIdx <= headerIdx) return;

            var headerWords = block[headerIdx].Words.OrderBy(w => w.BoundingBox.Left).ToList();
            var anchors = BuildColumnAnchors(headerWords);
            if (anchors.Count < 8) return;

            var tableWords = block.GetRange(headerIdx + 1, totalIdx - headerIdx - 1).SelectMany(l => l.Words);
            var dataLines = ClusterWordsIntoLines(tableWords)
                .Select(words => new PdfLine
                {
                    Y = words.Average(w => BaseY(w)),
                    Words = words
                })
                .OrderByDescending(l => l.Y)
                .ToList();

            double boundary0 = (anchors[0] + anchors[1]) / 2.0;
            double boundary2 = (anchors[2] + anchors[3]) / 2.0;
            double boundary4 = (anchors[5] + anchors[6]) / 2.0;

            double boundary1 = RefineBoundaryFromBody(dataLines, boundary0, anchors[3])
                                ?? (anchors[1] + anchors[2]) / 2.0;
            double boundary3 = RefineBoundaryFromBody(dataLines, boundary2, anchors[6])
                                ?? (anchors[4] + anchors[5]) / 2.0;

            var boundaries = new List<double> { boundary0, boundary1, boundary2, boundary3, boundary4 };

            const int bandCount = 6;
            var colBuckets = new List<SortedDictionary<double, List<Word>>>();
            for (int c = 0; c < bandCount; c++)
                colBuckets.Add(new SortedDictionary<double, List<Word>>(new DescendingComparer()));

            foreach (var line in dataLines)
            {
                foreach (var w in line.Words)
                {
                    int col = ColumnIndexFor(w.BoundingBox.Left, boundaries);
                    if (!colBuckets[col].ContainsKey(line.Y))
                        colBuckets[col][line.Y] = new List<Word>();
                    colBuckets[col][line.Y].Add(w);
                }
            }

            emp.Emoluments = BuildRows(colBuckets[0], colBuckets[1]);
            emp.GovtRecoveries = BuildRows(colBuckets[2], colBuckets[3]);
            emp.NonGovtRecoveries = BuildRows(colBuckets[4], colBuckets[5]);

            string totalsText = block[totalIdx].Text;
            emp.TotalEmolument = MatchDecimal(totalsText, @"Total Emolument\s+(\d+(?:\.\d+)?)");
            emp.TotalGovtRecoveries = MatchDecimal(totalsText, @"Total Govt\.?\s*Recoveries\s+(\d+(?:\.\d+)?)");
            emp.TotalNGRecoveries = MatchDecimal(totalsText, @"Total NG Recoveries\s+(\d+(?:\.\d+)?)");
        }

        private static double? RefineBoundaryFromBody(List<PdfLine> dataLines, double windowLeft, double windowRight)
        {
            double? maxValueRight = null;
            double? minTextLeft = null;

            foreach (var line in dataLines)
            {
                foreach (var w in line.Words)
                {
                    double left = w.BoundingBox.Left;
                    if (left < windowLeft || left > windowRight) continue;

                    bool isValueToken = Regex.IsMatch(w.Text, @"^-?\d+(\.\d+)?$") ||
                                         Regex.IsMatch(w.Text, @"^\d+\s*/\s*\d+$");

                    if (isValueToken)
                    {
                        if (!maxValueRight.HasValue || w.BoundingBox.Right > maxValueRight.Value)
                            maxValueRight = w.BoundingBox.Right;
                    }
                    else
                    {
                        if (!minTextLeft.HasValue || left < minTextLeft.Value)
                            minTextLeft = left;
                    }
                }
            }

            if (maxValueRight.HasValue && minTextLeft.HasValue && maxValueRight.Value < minTextLeft.Value)
                return (maxValueRight.Value + minTextLeft.Value) / 2.0;

            return null;
        }

        private static List<double> BuildColumnAnchors(List<Word> headerWords)
        {
            var anchors = new List<double>();
            foreach (var w in headerWords)
            {
                if (w.Text.StartsWith("Particulars", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.StartsWith("Amount", StringComparison.OrdinalIgnoreCase) ||
                    w.Text.StartsWith("Inst", StringComparison.OrdinalIgnoreCase))
                {
                    anchors.Add(w.BoundingBox.Left);
                }
            }
            return anchors;
        }

        private static int ColumnIndexFor(double x, List<double> boundaries)
        {
            for (int i = 0; i < boundaries.Count; i++)
                if (x < boundaries[i]) return i;
            return boundaries.Count;
        }

        private static (decimal? Amount, string InstNo) ParseValueAndInst(string valueText)
        {
            valueText = (valueText ?? "").Trim();
            if (valueText.Length == 0) return (null, "");

            var fracMatch = Regex.Match(valueText, @"\d+\s*/\s*\d+");
            string instNo = fracMatch.Success ? fracMatch.Value.Replace(" ", "") : "";

            string amountPart = fracMatch.Success ? valueText.Substring(0, fracMatch.Index) : valueText;

            decimal? amount = null;
            var amtMatch = Regex.Match(amountPart.Replace(",", ""), @"-?\d+(\.\d+)?");
            if (amtMatch.Success) amount = decimal.Parse(amtMatch.Value, CultureInfo.InvariantCulture);

            return (amount, instNo);
        }

        private static List<SalaryLineItem> BuildRows(
            SortedDictionary<double, List<Word>> particularCol,
            SortedDictionary<double, List<Word>> valueCol)
        {
            var allYs = new SortedSet<double>(new DescendingComparer());
            foreach (var y in particularCol.Keys) allYs.Add(y);
            foreach (var y in valueCol.Keys) allYs.Add(y);

            var rows = new List<SalaryLineItem>();
            foreach (var y in allYs)
            {
                string particular = particularCol.TryGetValue(y, out var pw)
                    ? string.Join(" ", pw.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))
                    : "";
                string valueText = valueCol.TryGetValue(y, out var vw)
                    ? string.Join(" ", vw.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))
                    : "";

                if (string.IsNullOrWhiteSpace(particular) && string.IsNullOrWhiteSpace(valueText))
                    continue;

                var (amount, instNo) = ParseValueAndInst(valueText);

                rows.Add(new SalaryLineItem
                {
                    Particular = particular.Trim(),
                    Amount = amount,
                    InstNo = instNo
                });
            }
            return rows;
        }

        private class DescendingComparer : IComparer<double>
        {
            public int Compare(double x, double y) => y.CompareTo(x);
        }

        private static string Match1(string text, string pattern, RegexOptions opts = RegexOptions.None)
        {
            var m = Regex.Match(text, pattern, opts);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static decimal? MatchDecimal(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            if (!m.Success) return null;
            return decimal.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v
                : (decimal?)null;
        }
    }
}