using EFISupportApp.Models.BankStatement;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Logging;

namespace EFISupportApp
{
    public class ReadPayBillITReport
    {
        #region Instance Variable        
        //IT Report Master 
        private static readonly Regex TanNoRegex = new(@"Tan No\s*:\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex OfficeLineRegex = new(@"Name of the Office\s*:\s*(.+?)\s*\(\s*(\d+)\s*\)", RegexOptions.Compiled);
        private static readonly Regex MonthYearRegex = new(@"For the Month of\s+(\w+)\s+(\d{4})", RegexOptions.Compiled);
        private static readonly Regex VoucherNoRegex = new(@"Treasury Voucher No\.\s*:\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex VoucherDateRegex = new(@"Treasury Voucher Date\s*:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.Compiled);
        private static readonly Regex TotalDeductionRegex = new(@"Total\s*\(Rs\)\s*([\d,]+)", RegexOptions.Compiled);
        private static readonly Regex TotalDeductionWordsRegex = new(@"Total Deduction in words\(Rs\)\s*:\s*(.+?)\.", RegexOptions.Compiled);

        //IT Report Emp Details        
        private static readonly Regex CodeNameRegex = new(@"^\s*\(\s*(?<code>[A-Za-z0-9]+)\s*\)\s*-\s*(?<name>.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex SrPanAmountRegex = new(@"^\s*(?<sr>\d{1,3})\s+(?<pan>[A-Z]{5}\d{4}[A-Z])\s+(?<gross>[\d,]+)\s+(?<ded>[\d,]+)\s*$", RegexOptions.Compiled);
        private static readonly Regex DesignationOnlyRegex = new(@"^\s*\(\s*(?<desig>[^)]+?)\s*\)\s*$", RegexOptions.Compiled);
        #endregion

        #region Method
        public static EmpIncomeTaxReport ExtractFromPdf(string pdfPath)
        {
            var text = ExtractText(pdfPath);
            return ParseText(text, Path.GetFileName(pdfPath));
        }

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

        private static EmpIncomeTaxReport ParseText(string text, string sourceFileName)
        {
            //IT Report Master Details
            var ITMaster = new IncomeTaxMaster() { };

            var tanNo = TanNoRegex.Match(text);
            if (tanNo.Success) ITMaster.TANNo = tanNo.Groups[1].Value.Trim();

            var office = OfficeLineRegex.Match(text);
            if (office.Success)
            {
                ITMaster.OfficeName = office.Groups[1].Value.Trim();
                ITMaster.DDOCode = office.Groups[2].Value.Trim();
            }

            var monthYear = MonthYearRegex.Match(text);
            if (monthYear.Success)
            {
                ITMaster.MonthName = monthYear.Groups[1].Value;
                ITMaster.Year = int.Parse(monthYear.Groups[2].Value);
            }

            var voucherNo = VoucherNoRegex.Match(text);
            if (voucherNo.Success) ITMaster.VoucherNo = voucherNo.Groups[1].Value.Trim();

            var voucherDate = VoucherDateRegex.Match(text);
            if (voucherDate.Success)
                ITMaster.VoucherDate = DateTime.ParseExact(voucherDate.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);

            ITMaster.TotalDeduction = ParseAmount(TotalDeductionRegex, text);

            var wordsMatch = TotalDeductionWordsRegex.Match(text);
            if (wordsMatch.Success) ITMaster.TotalDeductionInWords = wordsMatch.Groups[1].Value.Trim();

            // Employee PAN Detaills
            var empPanDetails = new List<EmpPANDetail>();
            var lines = text.Split('\n').Select(l => l.Trim()).ToArray();

            for (int i = 0; i < lines.Length; i++)
            {
                var codeName = CodeNameRegex.Match(lines[i]);
                if (!codeName.Success) continue;

                // sr + PAN + gross + deduction line: look ahead a couple lines in case of blanks.
                for (int j = i + 1; j < Math.Min(i + 3, lines.Length); j++)
                {
                    var srPan = SrPanAmountRegex.Match(lines[j]);
                    if (!srPan.Success) continue;

                    // Designation (optional): usually the very next line after sr/PAN/amounts.
                    string designation = "";
                    if (j + 1 < lines.Length)
                    {
                        var desigMatch = DesignationOnlyRegex.Match(lines[j + 1]);
                        if (desigMatch.Success) designation = desigMatch.Groups["desig"].Value.Trim();
                    }

                    empPanDetails.Add(new EmpPANDetail
                    {
                        SrNo = int.Parse(srPan.Groups["sr"].Value),
                        EmployeeCode = codeName.Groups["code"].Value.Trim(),
                        EmployeeName = Regex.Replace(codeName.Groups["name"].Value, @"\s+", " ").Trim(),
                        Designation = Regex.Replace(designation, @"\s+", " ").Trim(),
                        PANNumber = srPan.Groups["pan"].Value.Trim(),
                        GrossIncome = decimal.Parse(srPan.Groups["gross"].Value, NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture),
                        DeductionAmount = decimal.Parse(srPan.Groups["ded"].Value, NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture)
                    });

                    i = j; // skip past the consumed line(s)
                    break;
                }
            }

            return new EmpIncomeTaxReport { ITMaster = ITMaster, EmpPANDetails = empPanDetails };
        }

        private static decimal ParseAmount(Regex regex, string text)
        {
            var match = regex.Match(text);
            if (!match.Success) return 0m;
            return decimal.Parse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseDateTimeSafe(string datePart, string timePart)
        {
            var combined = $"{datePart} {timePart}";
            if (DateTime.TryParseExact(combined, "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return null;
        }
        #endregion
    }

    //Models
    public class EmpIncomeTaxReport
    {
        public IncomeTaxMaster ITMaster { get; set; }
        public List<EmpPANDetail> EmpPANDetails { get; set; }
    }

    public class IncomeTaxMaster
    {
        public string TANNo { get; set; }
        public string OfficeName { get; set; }
        public string DDOCode { get; set; }
        public string MonthName { get; set; }
        public int Year { get; set; }
        public string VoucherNo { get; set; }
        public DateTime? VoucherDate { get; set; }
        public decimal TotalDeduction { get; set; }
        public string TotalDeductionInWords { get; set; }
    }

    public class EmpPANDetail
    {
        public int SrNo { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Designation { get; set; }
        public string PANNumber { get; set; }
        public decimal GrossIncome { get; set; }
        public decimal DeductionAmount { get; set; }
    }

    #region Get All details
    //public class ReadPayBillITReport
    //{
    //    // --- Regex patterns tuned to the "SEVAARTH" Income Tax Report layout ---

    //    private static readonly Regex ReportGeneratedRegex =
    //        new(@"Income Tax Report\s+(\d{2}-\d{2}-\d{4})\s+(\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

    //    private static readonly Regex MonthYearRegex =
    //        new(@"For the Month of\s+(\w+)\s+(\d{4})", RegexOptions.Compiled);

    //    private static readonly Regex TreasuryRegex =
    //        new(@"Treasury\s*:\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    //    private static readonly Regex OfficeLineRegex =
    //        new(@"Name of the Office\s*:\s*(.+?)\s*\(\s*(\d+)\s*\)", RegexOptions.Compiled);

    //    private static readonly Regex TanNoRegex =
    //        new(@"Tan No\s*:\s*(\S+)", RegexOptions.Compiled);

    //    private static readonly Regex BillGroupRegex =
    //        new(@"Bill Group Name\s*:\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    //    private static readonly Regex TotalDeductionRegex =
    //        new(@"Total\s*\(Rs\)\s*([\d,]+)", RegexOptions.Compiled);

    //    private static readonly Regex TotalDeductionWordsRegex =
    //        new(@"Total Deduction in words\(Rs\)\s*:\s*(.+?)\.", RegexOptions.Compiled);

    //    private static readonly Regex VoucherNoRegex =
    //        new(@"Treasury Voucher No\.\s*:\s*(\d+)", RegexOptions.Compiled);

    //    private static readonly Regex VoucherDateRegex =
    //        new(@"Treasury Voucher Date\s*:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.Compiled);

    //    private static readonly Regex VerificationTimeRegex =
    //        new(@"VERIFICATION TIME\s*:\s*(\d{2}-\d{2}-\d{4})\s+(\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

    //    // "(AGRJBBM7201)-Jalindar Baban Bare" — code + name, on its own line, no sr number here.
    //    private static readonly Regex CodeNameRegex = new(
    //        @"^\s*\(\s*(?<code>[A-Za-z0-9]+)\s*\)\s*-\s*(?<name>.+?)\s*$",
    //        RegexOptions.Compiled);

    //    // "1 AMZPB8220G 1,19,088 3,000" — sr + PAN + gross + deduction, no designation here.
    //    private static readonly Regex SrPanAmountRegex = new(
    //        @"^\s*(?<sr>\d{1,3})\s+(?<pan>[A-Z]{5}\d{4}[A-Z])\s+(?<gross>[\d,]+)\s+(?<ded>[\d,]+)\s*$",
    //        RegexOptions.Compiled);

    //    // "(Agriculture Supervisor)" — designation alone, appears on the line after sr+PAN+amounts.
    //    private static readonly Regex DesignationOnlyRegex = new(
    //        @"^\s*\(\s*(?<desig>[^)]+?)\s*\)\s*$",
    //        RegexOptions.Compiled);

    //    /// <summary>
    //    /// Parses every .pdf in the given folder and returns one ParsedIncomeTaxStatement per file.
    //    /// </summary>
    //    public static List<ParsedIncomeTaxStatement> ParseFolder(string folderPath)
    //    {
    //        var results = new List<ParsedIncomeTaxStatement>();

    //        foreach (var file in Directory.GetFiles(folderPath, "*.pdf").OrderBy(f => f))
    //        {
    //            var text = ExtractText(file);
    //            var parsed = ParseText(text, Path.GetFileName(file));
    //            results.Add(parsed);
    //        }

    //        return results;
    //    }

    //    public static ParsedIncomeTaxStatement ExtractFromPdf(string pdfPath)
    //    {
    //        var text = ExtractText(pdfPath);
    //        return ParseText(text, Path.GetFileName(pdfPath));
    //    }

    //    /// <summary>
    //    /// Debug helper: returns the reconstructed line-by-line text for a PDF without
    //    /// parsing it. Print/inspect this if Master fields or Employees still come out
    //    /// empty, to see exactly what the regexes are matching against.
    //    /// </summary>
    //    public static string GetRawReconstructedText(string pdfPath) => ExtractText(pdfPath);

    //    private static string ExtractText(string pdfPath)
    //    {
    //        using var document = PdfDocument.Open(pdfPath);
    //        var sb = new System.Text.StringBuilder();
    //        foreach (var page in document.GetPages())
    //        {
    //            sb.AppendLine(ReconstructLines(page));
    //        }
    //        return sb.ToString();
    //    }

    //    /// <summary>
    //    /// Same Y-position based line reconstruction as ReadPayBillBankStatement, so that
    //    /// each visual row of the report (including wrapped multi-line cells) ends up as its
    //    /// own text line instead of being glued to neighbouring rows.
    //    /// </summary>
    //    private static string ReconstructLines(Page page)
    //    {
    //        const double yTolerance = 3.0; // points; tune up slightly if rows still merge/split

    //        var words = page.GetWords().ToList();
    //        if (words.Count == 0) return string.Empty;

    //        var rows = new List<List<Word>>();

    //        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
    //        {
    //            var row = rows.FirstOrDefault(r =>
    //                Math.Abs(r[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= yTolerance);

    //            if (row != null)
    //                row.Add(word);
    //            else
    //                rows.Add(new List<Word> { word });
    //        }

    //        var lines = rows.Select(r =>
    //            string.Join(" ", r.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

    //        return string.Join("\n", lines);
    //    }

    //    private static ParsedIncomeTaxStatement ParseText(string text, string sourceFileName)
    //    {
    //        var master = new IncomeTaxMasterRecord { SourceFile = sourceFileName };

    //        var reportGen = ReportGeneratedRegex.Match(text);
    //        if (reportGen.Success)
    //            master.ReportGeneratedDate = ParseDateTimeSafe(
    //                reportGen.Groups[1].Value, reportGen.Groups[2].Value);

    //        var monthYear = MonthYearRegex.Match(text);
    //        if (monthYear.Success)
    //        {
    //            master.Month = monthYear.Groups[1].Value;
    //            master.Year = int.Parse(monthYear.Groups[2].Value);
    //        }

    //        var treasury = TreasuryRegex.Match(text);
    //        if (treasury.Success) master.Treasury = treasury.Groups[1].Value.Trim();

    //        var office = OfficeLineRegex.Match(text);
    //        if (office.Success)
    //        {
    //            master.OfficeName = office.Groups[1].Value.Trim();
    //            master.OfficeCode = office.Groups[2].Value.Trim();
    //        }

    //        var tanNo = TanNoRegex.Match(text);
    //        if (tanNo.Success) master.TanNo = tanNo.Groups[1].Value.Trim();

    //        var billGroup = BillGroupRegex.Match(text);
    //        if (billGroup.Success) master.BillGroupName = billGroup.Groups[1].Value.Trim();

    //        master.TotalDeduction = ParseAmount(TotalDeductionRegex, text);

    //        var wordsMatch = TotalDeductionWordsRegex.Match(text);
    //        if (wordsMatch.Success) master.TotalDeductionInWords = wordsMatch.Groups[1].Value.Trim();

    //        var voucherNo = VoucherNoRegex.Match(text);
    //        if (voucherNo.Success) master.VoucherNo = voucherNo.Groups[1].Value.Trim();

    //        var voucherDate = VoucherDateRegex.Match(text);
    //        if (voucherDate.Success)
    //            master.VoucherDate = DateTime.ParseExact(
    //                voucherDate.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);

    //        var verification = VerificationTimeRegex.Match(text);
    //        if (verification.Success)
    //            master.VerificationTime = ParseDateTimeSafe(
    //                verification.Groups[1].Value, verification.Groups[2].Value);

    //        // --- Employee rows ---
    //        // Confirmed real layout, 3 physical lines per record, in this exact order:
    //        //   "(AGRJBBM7201)-Jalindar Baban Bare"        (code + name)
    //        //   "1 AMZPB8220G 1,19,088 3,000"                (sr + PAN + gross + deduction)
    //        //   "(Agriculture Supervisor)"                   (designation alone)
    //        // We anchor on the code/name line, then look ahead for the sr/PAN/amount line,
    //        // then look ahead once more (optional) for the standalone designation line.
    //        var employees = new List<IncomeTaxEmpDetail>();
    //        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();

    //        for (int i = 0; i < lines.Length; i++)
    //        {
    //            var codeName = CodeNameRegex.Match(lines[i]);
    //            if (!codeName.Success) continue;

    //            // sr + PAN + gross + deduction line: look ahead a couple lines in case of blanks.
    //            for (int j = i + 1; j < Math.Min(i + 3, lines.Length); j++)
    //            {
    //                var srPan = SrPanAmountRegex.Match(lines[j]);
    //                if (!srPan.Success) continue;

    //                // Designation (optional): usually the very next line after sr/PAN/amounts.
    //                string designation = "";
    //                if (j + 1 < lines.Length)
    //                {
    //                    var desigMatch = DesignationOnlyRegex.Match(lines[j + 1]);
    //                    if (desigMatch.Success) designation = desigMatch.Groups["desig"].Value.Trim();
    //                }

    //                employees.Add(new IncomeTaxEmpDetail
    //                {
    //                    SrNo = int.Parse(srPan.Groups["sr"].Value),
    //                    EmployeeCode = codeName.Groups["code"].Value.Trim(),
    //                    EmployeeName = Regex.Replace(codeName.Groups["name"].Value, @"\s+", " ").Trim(),
    //                    Designation = Regex.Replace(designation, @"\s+", " ").Trim(),
    //                    PanNumber = srPan.Groups["pan"].Value.Trim(),
    //                    GrossIncome = decimal.Parse(srPan.Groups["gross"].Value, NumberStyles.AllowThousands,
    //                        CultureInfo.InvariantCulture),
    //                    DeductionAmount = decimal.Parse(srPan.Groups["ded"].Value, NumberStyles.AllowThousands,
    //                        CultureInfo.InvariantCulture)
    //                });

    //                i = j; // skip past the consumed line(s)
    //                break;
    //            }
    //        }

    //        return new ParsedIncomeTaxStatement { Master = master, Employees = employees };
    //    }

    //    private static decimal ParseAmount(Regex regex, string text)
    //    {
    //        var match = regex.Match(text);
    //        if (!match.Success) return 0m;
    //        return decimal.Parse(match.Groups[1].Value, NumberStyles.AllowThousands,
    //            CultureInfo.InvariantCulture);
    //    }

    //    private static DateTime? ParseDateTimeSafe(string datePart, string timePart)
    //    {
    //        var combined = $"{datePart} {timePart}";
    //        if (DateTime.TryParseExact(combined, "dd-MM-yyyy HH:mm:ss",
    //            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
    //            return dt;
    //        return null;
    //    }
    //}

    //public class IncomeTaxMasterRecord
    //{
    //    public string SourceFile { get; set; }
    //    public DateTime? ReportGeneratedDate { get; set; }
    //    public string Month { get; set; }
    //    public int Year { get; set; }
    //    public string Treasury { get; set; }
    //    public string OfficeName { get; set; }
    //    public string OfficeCode { get; set; }
    //    public string TanNo { get; set; }
    //    public string BillGroupName { get; set; }
    //    public decimal TotalDeduction { get; set; }
    //    public string TotalDeductionInWords { get; set; }
    //    public string VoucherNo { get; set; }
    //    public DateTime? VoucherDate { get; set; }
    //    public DateTime? VerificationTime { get; set; }
    //}

    //public class IncomeTaxEmpDetail
    //{
    //    public int SrNo { get; set; }
    //    public string EmployeeCode { get; set; }
    //    public string EmployeeName { get; set; }
    //    public string Designation { get; set; }
    //    public string PanNumber { get; set; }
    //    public decimal GrossIncome { get; set; }
    //    public decimal DeductionAmount { get; set; }
    //}

    //public class ParsedIncomeTaxStatement
    //{
    //    public IncomeTaxMasterRecord Master { get; set; }
    //    public List<IncomeTaxEmpDetail> Employees { get; set; }
    //}
    #endregion
}
