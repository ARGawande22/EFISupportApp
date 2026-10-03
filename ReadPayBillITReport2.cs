using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig;

namespace EFISupportApp
{
    public class ReadPayBillITReport2
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

        // DataSet / table names
        private const string MasterTableName = "ITMaster";
        private const string EmpTableName = "EmpPANDetails";
        #endregion

        #region Method

        /// <summary>
        /// Extracts and parses a single Income Tax Report PDF, returning the data as a
        /// DataSet with two tables: "ITMaster" (one row) and "EmpPANDetails" (one row per employee).
        /// </summary>
        public static DataSet ExtractFromPdf(string pdfPath)
        {
            var text = ExtractText(pdfPath);
            return ParseTextToDataSet(text, Path.GetFileName(pdfPath));
        }

        /// <summary>
        /// Parses every .pdf in the given folder, returning ONE combined DataSet with
        /// "ITMaster" (one row per file) and "EmpPANDetails" (one row per employee, across all files).
        /// A "SourceFile" column is added to both tables so rows can be traced back / joined.
        /// </summary>
        public DataSet ExtractFromFolderAsDataSet(string folderPath)
        {
            var combined = CreateEmptyDataSet();

            foreach (var file in Directory.GetFiles(folderPath, "*.pdf").OrderBy(f => f))
            {
                var text = ExtractText(file);
                var single = ParseTextToDataSet(text, Path.GetFileName(file));

                combined.Tables[MasterTableName].ImportRow(single.Tables[MasterTableName].Rows[0]);
                foreach (DataRow row in single.Tables[EmpTableName].Rows)
                {
                    combined.Tables[EmpTableName].ImportRow(row);
                }
            }

            return combined;
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

        private static DataSet CreateEmptyDataSet()
        {
            var ds = new DataSet();

            var masterTable = new DataTable(MasterTableName);
            masterTable.Columns.Add("SourceFile", typeof(string));
            masterTable.Columns.Add("TANNo", typeof(string));
            masterTable.Columns.Add("OfficeName", typeof(string));
            masterTable.Columns.Add("DDOCode", typeof(string));
            masterTable.Columns.Add("MonthName", typeof(string));
            masterTable.Columns.Add("Year", typeof(int));
            masterTable.Columns.Add("VoucherNo", typeof(string));
            masterTable.Columns.Add("VoucherDate", typeof(DateTime));
            masterTable.Columns.Add("TotalDeduction", typeof(decimal));
            masterTable.Columns.Add("TotalDeductionInWords", typeof(string));
            ds.Tables.Add(masterTable);

            var empTable = new DataTable(EmpTableName);
            empTable.Columns.Add("SourceFile", typeof(string));
            empTable.Columns.Add("SrNo", typeof(int));
            empTable.Columns.Add("EmployeeCode", typeof(string));
            empTable.Columns.Add("EmployeeName", typeof(string));
            empTable.Columns.Add("Designation", typeof(string));
            empTable.Columns.Add("PANNumber", typeof(string));
            empTable.Columns.Add("GrossIncome", typeof(decimal));
            empTable.Columns.Add("DeductionAmount", typeof(decimal));
            ds.Tables.Add(empTable);

            return ds;
        }

        private static DataSet ParseTextToDataSet(string text, string sourceFileName)
        {
            var ds = CreateEmptyDataSet();
            var masterTable = ds.Tables[MasterTableName];
            var empTable = ds.Tables[EmpTableName];

            // --- Master row ---
            var masterRow = masterTable.NewRow();
            masterRow["SourceFile"] = sourceFileName;

            var tanNo = TanNoRegex.Match(text);
            masterRow["TANNo"] = tanNo.Success ? tanNo.Groups[1].Value.Trim() : (object)DBNull.Value;

            var office = OfficeLineRegex.Match(text);
            if (office.Success)
            {
                masterRow["OfficeName"] = office.Groups[1].Value.Trim();
                masterRow["DDOCode"] = office.Groups[2].Value.Trim();
            }

            var monthYear = MonthYearRegex.Match(text);
            if (monthYear.Success)
            {
                masterRow["MonthName"] = monthYear.Groups[1].Value;
                masterRow["Year"] = int.Parse(monthYear.Groups[2].Value);
            }

            var voucherNo = VoucherNoRegex.Match(text);
            masterRow["VoucherNo"] = voucherNo.Success ? voucherNo.Groups[1].Value.Trim() : (object)DBNull.Value;

            var voucherDate = VoucherDateRegex.Match(text);
            if (voucherDate.Success)
                masterRow["VoucherDate"] = DateTime.ParseExact(
                    voucherDate.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);

            masterRow["TotalDeduction"] = ParseAmount(TotalDeductionRegex, text);

            var wordsMatch = TotalDeductionWordsRegex.Match(text);
            masterRow["TotalDeductionInWords"] = wordsMatch.Success ? wordsMatch.Groups[1].Value.Trim() : (object)DBNull.Value;

            masterTable.Rows.Add(masterRow);

            // --- Employee rows ---
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

                    var empRow = empTable.NewRow();
                    empRow["SourceFile"] = sourceFileName;
                    empRow["SrNo"] = int.Parse(srPan.Groups["sr"].Value);
                    empRow["EmployeeCode"] = codeName.Groups["code"].Value.Trim();
                    empRow["EmployeeName"] = Regex.Replace(codeName.Groups["name"].Value, @"\s+", " ").Trim();
                    empRow["Designation"] = Regex.Replace(designation, @"\s+", " ").Trim();
                    empRow["PANNumber"] = srPan.Groups["pan"].Value.Trim();
                    empRow["GrossIncome"] = decimal.Parse(srPan.Groups["gross"].Value, NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture);
                    empRow["DeductionAmount"] = decimal.Parse(srPan.Groups["ded"].Value, NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture);
                    empTable.Rows.Add(empRow);

                    i = j; // skip past the consumed line(s)
                    break;
                }
            }

            return ds;
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
}
