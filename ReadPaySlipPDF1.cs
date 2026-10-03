using EFISupportApp.Models.PaySlip;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    // ---------------------------------------------------------------------
    // Parser
    // ---------------------------------------------------------------------
    public class ReadPaySlipPDF1
    {
        // How close (in PDF points) two words' vertical centers must be
        // to be considered "the same line". Tune this if rows merge/split wrongly.
        private const double YTolerance = 3.0;

        public static List<Employee> Parse(string pdfPath)
        {
            var allLines = ExtractLines(pdfPath);
            var blocks = SplitIntoEmployeeBlocks(allLines);

            var employees = new List<Employee>();
            foreach (var block in blocks)
            {
                var emp = ParseEmployeeBlock(block);
                if (emp != null) employees.Add(emp);
            }
            return employees;
        }

        // Groups employees that share the same VoucherNumber + VoucherDate +
        // BillNo (i.e. everyone paid under the same bill run). Field values
        // are trimmed/normalized before comparison so stray whitespace
        // differences picked up during PDF text extraction don't split what
        // should be a single group into two.
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
                    // Bill description / gross / net amt / DDO code / salary
                    // month are the same for every employee in the group -
                    // take them from the first record that actually has a
                    // value, in case any one record failed to parse that
                    // particular field.
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

        // -- Step 1: turn the raw word/bbox soup into ordered "lines" -------

        private static List<PdfLine> ExtractLines(string pdfPath)
        {
            var lines = new List<PdfLine>();

            using var document = PdfDocument.Open(pdfPath);
            foreach (Page page in document.GetPages())
            {
                var words = page.GetWords().ToList();

                // Sort top-to-bottom (PdfPig Y grows upward -> descending),
                // then left-to-right within a line.
                var remaining = new List<Word>(words);
                var pageLines = new List<PdfLine>();

                while (remaining.Count > 0)
                {
                    // Seed a new line with the highest remaining word.
                    var seed = remaining.OrderByDescending(w => w.BoundingBox.Top).First();
                    double seedY = seed.BoundingBox.Top;

                    var sameLine = remaining
                        .Where(w => Math.Abs(w.BoundingBox.Top - seedY) <= YTolerance)
                        .OrderBy(w => w.BoundingBox.Left)
                        .ToList();

                    pageLines.Add(new PdfLine { Y = seedY, Words = sameLine });

                    foreach (var w in sameLine) remaining.Remove(w);
                }

                // Order lines top-to-bottom for this page.
                lines.AddRange(pageLines.OrderByDescending(l => l.Y));
            }

            return lines;
        }

        // -- Step 2: cut the line list into one chunk per employee ---------

        private static List<List<PdfLine>> SplitIntoEmployeeBlocks(List<PdfLine> lines)
        {
            var startIdx = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Text.Contains("Employee Name", StringComparison.OrdinalIgnoreCase))
                    startIdx.Add(i);
            }

            var blocks = new List<List<PdfLine>>();
            for (int b = 0; b < startIdx.Count; b++)
            {
                int start = startIdx[b];
                // A block runs from one "Taluka Agriculture office..." header
                // line (just before Employee Name) up to just before the next
                // one. We back up a couple of lines to include the DDO/header
                // line, since it sits just above "Employee Name".
                int backedUpStart = Math.Max(0, start - 2);
                int end = (b + 1 < startIdx.Count) ? startIdx[b + 1] - 2 : lines.Count;
                end = Math.Max(end, backedUpStart + 1);

                blocks.Add(lines.GetRange(backedUpStart, end - backedUpStart));
            }
            return blocks;
        }

        // -- Step 3: parse one employee's block -----------------------------

        // Gap between consecutive words' vertical midpoints (sorted top to
        // bottom) above which we consider it a new visual line. This is
        // deliberately RELATIVE/adaptive rather than a fixed absolute grid:
        // an absolute grid (e.g. "round Y to nearest 4pt") misaligns
        // differently for every employee block, because each block's total
        // height varies with how many salary-table rows it has - so a line
        // that sits at Y=302 for one employee might sit at Y=298 for the
        // next, and could round into a different/same bucket unpredictably.
        // Gap-based detection only cares about the spacing between adjacent
        // words, so it doesn't matter where on the page the line falls.
        private const double LineGapThreshold = 3.0;

        // Groups words into visual lines using adaptive gap detection (see
        // GetReadingOrderText below for the full rationale). Returns each
        // line as its raw word list (still left-to-right ordered) rather
        // than a joined string, so callers that need per-word X positions
        // (like the salary table column bucketing) can still use them.
        private static List<List<Word>> ClusterWordsIntoLines(IEnumerable<Word> wordsIn, double gapThreshold = LineGapThreshold)
        {
            var sorted = wordsIn
                .Select(w => new { Word = w, Mid = (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0 })
                .OrderByDescending(x => x.Mid)
                .ToList();

            var lines = new List<List<Word>>();
            List<Word> current = null;
            double? lastMid = null;

            foreach (var item in sorted)
            {
                bool startNewLine = current == null ||
                                     (lastMid.HasValue && (lastMid.Value - item.Mid) > gapThreshold);
                if (startNewLine)
                {
                    current = new List<Word>();
                    lines.Add(current);
                }
                current.Add(item.Word);
                lastMid = item.Mid;
            }

            // Left-to-right within each line.
            foreach (var line in lines)
                line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

            return lines;
        }

        private static string GetReadingOrderText(IEnumerable<Word> wordsIn)
        {
            var lines = ClusterWordsIntoLines(wordsIn);
            return string.Join(" ", lines.Select(line => string.Join(" ", line.Select(w => w.Text))));
        }

        private static Employee ParseEmployeeBlock(List<PdfLine> block)
        {
            // IMPORTANT: for the header/personal-detail fields we do NOT use
            // the pre-clustered `block` lines (those can scramble on dense,
            // multi-field rows - see GetReadingOrderText comment above).
            // Instead we flatten every word in the block back out and
            // re-sort it into reading order ourselves. This is what fixed
            // Name/Designation/DOB/DOJ/DOR/UidNo/PayCommission/Level/
            // GpfDcpsAccNo/PranNo/BankAccNo/IfscCode/MobileNo/PanNo/NetPay.
            var allWords = block.SelectMany(l => l.Words);
            string fullText = GetReadingOrderText(allWords);

            if (!fullText.Contains("Employee Name")) return null;

            var emp = new Employee();

            emp.Office = Match1(fullText, @"^(.*?)\s*DDO CODE", RegexOptions.Multiline);
            // "DDO CODE : 2210001669" - value is a run of digits (occasionally
            // seen with a stray leading/trailing letter in some IFMS exports,
            // so we match digits specifically rather than \S+).
            emp.DdoCode = Match1(fullText, @"DDO CODE\s*:\s*(\d+)");

            var nameMatch = Regex.Match(fullText,
                @"Employee Name\s*:\s*\(([^)]+)\)\s*(.+?)\s*Designation\s*:\s*(.+?)\s*Salary Month\s*:\s*([A-Za-z]+\s*\d{4})");
            if (nameMatch.Success)
            {
                emp.EmployeeCode = nameMatch.Groups[1].Value.Trim();
                emp.Name = nameMatch.Groups[2].Value.Trim();
                emp.Designation = nameMatch.Groups[3].Value.Trim();
                emp.SalaryMonth = nameMatch.Groups[4].Value.Trim();
            }

            emp.DateOfBirth = Match1(fullText, @"Date of Birth\s*:\s*([\d/]+)");
            emp.DateOfJoining = Match1(fullText, @"Date of Joining\s*:\s*([\d/]+)");
            emp.DateOfRetirement = Match1(fullText, @"Date of Retirement\s*:\s*([\d/]+)");

            emp.UidNo = Match1(fullText, @"Uid No\s*:\s*(\S+)");
            emp.PayCommission = Match1(fullText, @"Pay Commission\s*:\s*(.+?)\s*(?:Pay Level|Level)\s*:");
            emp.Level = Match1(fullText, @"Level\s*:\s*(.+?)\s*(?:GPF/DCPS|PRAN|Bank A/c|:)");

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

        // -- Step 4: parse the 3-column, variable-row table -----------------

        private static void ParseTable(List<PdfLine> block, Employee emp)
        {
            // Find the header line: "Particulars Amount (Rs.) Particulars Amount (Rs.) Inst. No. Particulars Amount (Rs.) Inst. No."
            int headerIdx = block.FindIndex(l =>
                l.Text.Contains("Particulars") && l.Text.Contains("Amount"));

            // Find the totals line: "Total Emolument ... Total Govt. Recoveries ... Total NG Recoveries ..."
            int totalIdx = block.FindIndex(l => l.Text.Contains("Total Emolument"));

            if (headerIdx < 0 || totalIdx < 0 || totalIdx <= headerIdx) return;

            var headerWords = block[headerIdx].Words.OrderBy(w => w.BoundingBox.Left).ToList();
            var anchors = BuildColumnAnchors(headerWords);
            if (anchors.Count < 8) return;

            // Build dataLines FIRST - boundaries[1] and boundaries[3] need it.
            var tableWords = block.GetRange(headerIdx + 1, totalIdx - headerIdx - 1).SelectMany(l => l.Words);
            var dataLines = ClusterWordsIntoLines(tableWords)
                .Select(words => new PdfLine
                {
                    Y = words.Average(w => (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0),
                    Words = words
                })
                .OrderByDescending(l => l.Y)
                .ToList();

            double boundary0 = (anchors[0] + anchors[1]) / 2.0; // EmolPart | EmolAmt   (reliable via header)
            double boundary2 = (anchors[2] + anchors[3]) / 2.0; // GovtPart | GovtValue (reliable via header)
            double boundary4 = (anchors[5] + anchors[6]) / 2.0; // NGPart   | NGValue   (reliable via header)

            // These two are the cross-section boundaries that header anchors alone
            // can't reliably place (see RefineBoundaryFromBody comment) - derive
            // them from the body instead, with the header estimate only as a
            // last-resort fallback.
            double boundary1 = RefineBoundaryFromBody(dataLines, boundary0, anchors[3])
                                ?? (anchors[1] + anchors[2]) / 2.0;
            double boundary3 = RefineBoundaryFromBody(dataLines, boundary2, anchors[6])
                                ?? (anchors[4] + anchors[5]) / 2.0;

            var boundaries = new List<double> { boundary0, boundary1, boundary2, boundary3, boundary4 };

            // Per-band word buckets, each keyed by the row's Y (so we can
            // re-assemble "Particular" + "Value" that belong to the same row).
            const int bandCount = 6;
            var colBuckets = new List<SortedDictionary<double, List<Word>>>();
            for (int c = 0; c < bandCount; c++) colBuckets.Add(new SortedDictionary<double, List<Word>>(new DescendingComparer()));

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

            // Totals line, e.g.:
            // "Total Emolument 17500 Total Govt. Recoveries 201 0 Total NG Recoveries 0"
            string totalsText = block[totalIdx].Text;
            emp.TotalEmolument = MatchDecimal(totalsText, @"Total Emolument\s+(\d+(?:\.\d+)?)");
            emp.TotalGovtRecoveries = MatchDecimal(totalsText, @"Total Govt\.?\s*Recoveries\s+(\d+(?:\.\d+)?)");
            emp.TotalNGRecoveries = MatchDecimal(totalsText, @"Total NG Recoveries\s+(\d+(?:\.\d+)?)");
        }


        // Refines a column boundary using the ACTUAL body data instead of the
        // header's word bounding boxes (header labels like "Amount (Rs.)" don't
        // reliably line up with where the real data column starts/ends in this
        // PDF export - see notes above). Scans words whose Left edge falls in
        // [windowLeft, windowRight] and finds:
        //   - the rightmost edge of any purely numeric token (an Amount value,
        //     or an instalment ref like "11/36") -> true right edge of the
        //     value column's actual data, regardless of how narrow/wide
        //   - the leftmost edge of any non-numeric token -> true left edge of
        //     the next column's Particulars text
        // The midpoint of those two is a safe, data-driven boundary. Falls back
        // to the caller's header-derived estimate if we can't find both.
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

            return null; // couldn't determine safely from body data - let caller fall back
        }


        private static List<double> BuildColumnAnchors(List<Word> headerWords)
        {
            // headerWords, left-to-right, look like:
            // Particulars | Amount | (Rs.) | Particulars | Amount | (Rs.) | Inst. | No. | Particulars | Amount | (Rs.) | Inst. | No.
            var anchors = new List<double>();

            foreach (var w in headerWords)
            {
                if (w.Text.StartsWith("Particulars", StringComparison.OrdinalIgnoreCase))
                    anchors.Add(w.BoundingBox.Left);
                else if (w.Text.StartsWith("Amount", StringComparison.OrdinalIgnoreCase))
                    anchors.Add(w.BoundingBox.Left);
                else if (w.Text.StartsWith("Inst", StringComparison.OrdinalIgnoreCase))
                    anchors.Add(w.BoundingBox.Left);
                // "(Rs.)" and "No." are ignored - they sit right next to
                // Amount / Inst. and don't need their own anchor.
            }

            // Expect: Particulars(1) Amount(1) Particulars(1) Amount(1) Inst(1) Particulars(1) Amount(1) Inst(1)
            // -> exactly 8 anchors in left-to-right order already, since we
            // iterated headerWords left-to-right.
            return anchors;
        }


        private static int ColumnIndexFor(double x, List<double> boundaries)
        {
            for (int i = 0; i < boundaries.Count; i++)
                if (x < boundaries[i]) return i;
            return boundaries.Count; // last column
        }

        // Parses a combined "Amount [Inst.No]" cell, e.g.:
        //   "9000"          -> Amount = 9000,   InstNo = ""
        //   "1"             -> Amount = 1,      InstNo = ""
        //   "37580 11/36"   -> Amount = 37580,  InstNo = "11/36"
        //   "1250.00 8/10"  -> Amount = 1250.00, InstNo = "8/10"
        // An instalment reference always looks like a small-integer
        // fraction ("N/M"); that pattern is what actually distinguishes it
        // from the amount, not its X-position on the page.
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
            // Both columns were bucketed by the *same* line Y-keys (since we
            // iterate the same dataLines for every column), so rows line up
            // naturally per physical text line. A column that has no words on
            // a given line simply won't have a key there.
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
                    continue; // stray/empty row - skip

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

        // -- small regex helpers --------------------------------------------

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
