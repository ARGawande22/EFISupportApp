using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadPayBillOuterPDF
    {
        // Tolerance (PDF points) for clustering words that sit on the same
        // printed line into one row of text.
        private const double RowGroupTolerance = 3.0;

        private static readonly string[] EmployeeCountRowLabels =
        {
            "Sanctioned Post",
            "Payment Drawn for No. of post",
            "Payment Drawn for Actual post"
        };

        // Fixed column order for the "Employee HRR Schemewise" table. The
        // header line prints as "A B B N Gz C D", where "B N Gz" is itself
        // one category (confirmed by the "Bill for" line's
        // "-A,B,B N Gz,C,D-Both Permanent" listing) -- so this is hardcoded
        // rather than parsed from the header text, which can't be split on
        // whitespace alone.
        private static readonly string[] EmployeeCountColumns = { "A", "B", "B N Gz", "C", "D" };

        public static DataSet ExtractFromPdf(string pdfPath)
        {
            var ds = new DataSet("MTR19OuterBill");

            var masterTable = new DataTable("Master");
            masterTable.Columns.Add("Field");
            masterTable.Columns.Add("Value");
            ds.Tables.Add(masterTable);

            var detailTable = new DataTable("DetailHead");
            detailTable.Columns.Add("SectionCode");
            detailTable.Columns.Add("Label");
            detailTable.Columns.Add("SubDetailedHead", typeof(int));
            detailTable.Columns.Add("Row", typeof(int));
            detailTable.Columns.Add("Amount", typeof(decimal));
            detailTable.Columns.Add("AccountCode");
            ds.Tables.Add(detailTable);

            var npsTable = new DataTable("NpsContributionSummary");
            npsTable.Columns.Add("TypeOfEmployee");
            npsTable.Columns.Add("HeadOfAccountCode");
            npsTable.Columns.Add("Nps", typeof(decimal));
            npsTable.Columns.Add("NpsDelayed", typeof(decimal));
            npsTable.Columns.Add("NpsDa", typeof(decimal));
            npsTable.Columns.Add("NpsPay", typeof(decimal));
            npsTable.Columns.Add("NpsPayDiff", typeof(decimal));
            ds.Tables.Add(npsTable);

            var employeeCountTable = new DataTable("EmployeeCount");
            employeeCountTable.Columns.Add("Metric");
            foreach (var col in EmployeeCountColumns)
                employeeCountTable.Columns.Add(col, typeof(int));
            ds.Tables.Add(employeeCountTable);

            var debugTable = new DataTable("Debug");
            debugTable.Columns.Add("Step");
            debugTable.Columns.Add("Detail");
            ds.Tables.Add(debugTable);

            using var document = PdfDocument.Open(pdfPath);

            // ---- Reconstruct the whole document as an ordered list of lines ----
            var lines = new List<string>();
            int pageNum = 0;
            foreach (var page in document.GetPages())
            {
                pageNum++;
                var pageLines = ReconstructLines(page.GetWords());
                debugTable.Rows.Add($"Page {pageNum}", $"{pageLines.Count} lines reconstructed.");
                lines.AddRange(pageLines);
            }

            string fullText = string.Join("\n", lines);

            // ---- 1. Header / master fields ----
            CaptureMasterInfo(fullText, masterTable, debugTable);

            // ---- 2. Detail-head table (Object of Expenditure) ----
            int startIdx = lines.FindIndex(l => l.Contains("(Object of Expenditure)"));
            if (startIdx == -1)
            {
                debugTable.Rows.Add("DetailHead table", "'(Object of Expenditure)' marker not found -- skipping table parse.");
            }
            else
            {
                // Table body runs from just after the column-header line
                // (the one starting with "001" and containing "Detailed Head")
                // until whichever of these section markers appears first.
                int headerIdx = lines.FindIndex(startIdx, l => l.Contains("Detailed Head") && l.Contains("Amount"));
                int bodyStart = headerIdx == -1 ? startIdx + 1 : headerIdx + 1;

                int endIdx = lines.FindIndex(bodyStart, l =>
                    l.Contains("NPS Employee Contribution Details") ||
                    l.Contains("Employee HRR Schemewise") ||
                    l.Contains("Taluka Agriculture Officer"));
                if (endIdx == -1) endIdx = lines.Count;

                int parsedCount = 0, skippedCount = 0;
                for (int i = bodyStart; i < endIdx; i++)
                {
                    if (TryParseDetailLine(lines[i], out var row))
                    {
                        detailTable.Rows.Add(row.SectionCode, row.Label,
                            (object)row.SubDetailedHead ?? DBNull.Value,
                            (object)row.Row ?? DBNull.Value,
                            (object)row.Amount ?? DBNull.Value,
                            (object)row.AccountCode ?? DBNull.Value);
                        parsedCount++;
                    }
                    else if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        skippedCount++;
                        debugTable.Rows.Add("DetailHead line skipped (no numeric row/amount matched)", lines[i]);
                    }
                }
                debugTable.Rows.Add("DetailHead table", $"{parsedCount} rows parsed, {skippedCount} lines skipped.");
            }

            // ---- 3. NPS Employee Contribution Details (absent on GPF bills) ----
            int npsIdx = lines.FindIndex(l => l.Contains("Ac maintained By"));
            if (npsIdx != -1 && TryParseNpsLine(lines[npsIdx], out var npsRow))
            {
                npsTable.Rows.Add(npsRow.TypeOfEmployee, npsRow.HeadOfAccountCode,
                    npsRow.Nps, npsRow.NpsDelayed, npsRow.NpsDa, npsRow.NpsPay, npsRow.NpsPayDiff);
                debugTable.Rows.Add("NpsContributionSummary", "1 row parsed.");
            }
            else
            {
                debugTable.Rows.Add("NpsContributionSummary", "Not present in this bill (expected for GPF bills).");
            }

            // ---- 4. Employee HRR Schemewise Rent Free Employee Count Details ----
            foreach (var metricLabel in EmployeeCountRowLabels)
            {
                int lineIdx = lines.FindIndex(l => l.StartsWith(metricLabel, StringComparison.OrdinalIgnoreCase));
                if (lineIdx == -1)
                {
                    debugTable.Rows.Add($"EmployeeCount row '{metricLabel}'", "Not found.");
                    continue;
                }

                string remainder = lines[lineIdx].Substring(metricLabel.Length).Trim();
                var numberMatches = Regex.Matches(remainder, @"[\d,]+");
                if (numberMatches.Count != EmployeeCountColumns.Length)
                {
                    debugTable.Rows.Add($"EmployeeCount row '{metricLabel}'",
                        $"Expected {EmployeeCountColumns.Length} numbers, found {numberMatches.Count} -- skipping.");
                    continue;
                }

                var newRow = employeeCountTable.NewRow();
                newRow["Metric"] = metricLabel;
                for (int c = 0; c < EmployeeCountColumns.Length; c++)
                    newRow[EmployeeCountColumns[c]] = ParseInt(numberMatches[c].Value);
                employeeCountTable.Rows.Add(newRow);
            }

            // ---- 5. Convenience copies of key totals into Master, for easy lookup ----
            foreach (var label in new[]
                     {
                         "Total Salary", "Gross Salary", "Gross Amount",
                         "Total (A) AG. DED", "Total (B) TR DED",
                         "Total Deductions", "Net Pay"
                     })
            {
                var match = detailTable.AsEnumerable()
                    .FirstOrDefault(r => string.Equals(r.Field<string>("Label"), label, StringComparison.OrdinalIgnoreCase));
                if (match != null && match["Amount"] != DBNull.Value)
                    masterTable.Rows.Add(label, match.Field<decimal>("Amount").ToString("N0"));
            }

            return ds;
        }

        // ---------------------------------------------------------------
        // Groups words into printed lines by Y-position (descending Y, then
        // left-to-right by X), joining each line's words with a single
        // space. This document's fields aren't packed tightly enough to
        // need PdfPig's raw Letters -- word-level reconstruction is enough.
        // ---------------------------------------------------------------
        private static List<string> ReconstructLines(IEnumerable<Word> words)
        {
            var rows = new List<List<Word>>();
            foreach (var w in words.OrderByDescending(w => w.BoundingBox.Centroid.Y).ThenBy(w => w.BoundingBox.Left))
            {
                var match = rows.FirstOrDefault(r =>
                    Math.Abs(r[0].BoundingBox.Centroid.Y - w.BoundingBox.Centroid.Y) <= RowGroupTolerance);
                if (match != null) match.Add(w);
                else rows.Add(new List<Word> { w });
            }

            return rows
                .Select(r => string.Join(" ", r.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                .ToList();
        }

        private class DetailLineResult
        {
            public string SectionCode;
            public string Label;
            public int? SubDetailedHead;
            public int? Row;
            public decimal? Amount;
            public string AccountCode;
        }

        // ---------------------------------------------------------------
        // Parses one line of the detail-head table. Because different rows
        // carry different subsets of columns (a plain total row has no
        // Sub-Detailed-Head or Account Code; a section-header row like
        // "Deductions Adj. By Accountant General 11" has ONLY a Row number
        // and no Amount at all; only the Basic/HBA/GPF/treasury-deduction
        // rows carry an Account Code), this works from the RIGHT of the
        // line inward, but -- importantly -- decides what each trailing
        // number MEANS based on how many trailing numbers there are, not
        // just their position. A single trailing number is always the Row
        // (never the Amount): rows with a real Amount always print at least
        // "Row Amount" together, so seeing only one number left over means
        // this line has no Amount at all.
        //   1. Optional leading section code (e.g. "0030 -", "004", "8342 -")
        //   2. Trailing long digit-string (8+ digits, no commas) -> AccountCode
        //   3. Count the remaining trailing numeric tokens (0, 1, 2, or 3+):
        //        0 numbers -> decorative/section-title-only line, skip
        //        1 number  -> that's the Row; no Amount, no SubDetailedHead
        //        2 numbers -> Row, Amount (in that left-to-right order)
        //        3+ numbers-> SubDetailedHead, Row, Amount (rightmost 3 kept)
        //   4. Whatever text remains -> Label
        // ---------------------------------------------------------------
        private static bool TryParseDetailLine(string line, out DetailLineResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            string remaining = line.Trim();
            string sectionCode = null;

            var sectionMatch = Regex.Match(remaining, @"^(?<code>\d{2,4})\s*-?\s+(?=[A-Za-z<])");
            if (sectionMatch.Success)
            {
                sectionCode = sectionMatch.Groups["code"].Value;
                remaining = remaining.Substring(sectionMatch.Length).Trim();
            }

            var tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count == 0) return false;

            string accountCode = null;
            if (tokens.Count > 0 && Regex.IsMatch(tokens[^1], @"^\d{8,}$"))
            {
                accountCode = tokens[^1];
                tokens.RemoveAt(tokens.Count - 1);
            }

            // Count how many numeric tokens trail the label, without
            // removing them yet -- this count decides their meaning.
            int trailingNumericCount = 0;
            while (trailingNumericCount < tokens.Count &&
                   Regex.IsMatch(tokens[tokens.Count - 1 - trailingNumericCount], @"^[\d,]+$"))
            {
                trailingNumericCount++;
            }

            if (trailingNumericCount == 0) return false; // decorative/section-title-only line

            decimal? amount = null;
            int? rowNum = null;
            int? subDetailedHead = null;

            if (trailingNumericCount == 1)
            {
                rowNum = ParseInt(tokens[^1]);
                tokens.RemoveAt(tokens.Count - 1);
            }
            else
            {
                // 2 or more trailing numbers: rightmost is Amount, then Row,
                // then (if a third exists) SubDetailedHead. Any numbers
                // beyond the rightmost 3 are left as part of the label --
                // not seen in the reference samples, but kept safe rather
                // than silently discarding label text.
                amount = ParseAmount(tokens[^1]);
                tokens.RemoveAt(tokens.Count - 1);
                rowNum = ParseInt(tokens[^1]);
                tokens.RemoveAt(tokens.Count - 1);

                if (trailingNumericCount >= 3)
                {
                    subDetailedHead = ParseInt(tokens[^1]);
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            string label = string.Join(" ", tokens).Trim();
            if (label.Length == 0) return false;

            result = new DetailLineResult
            {
                SectionCode = sectionCode,
                Label = label,
                SubDetailedHead = subDetailedHead,
                Row = rowNum,
                Amount = amount,
                AccountCode = accountCode
            };
            return true;
        }

        private class NpsLineResult
        {
            public string TypeOfEmployee;
            public string HeadOfAccountCode;
            public decimal Nps, NpsDelayed, NpsDa, NpsPay, NpsPayDiff;
        }

        // Parses e.g. "Ac maintained By SRKA 8342508101 37,541 0 0 0 0"
        // -> TypeOfEmployee = "Ac maintained By SRKA", HeadOfAccountCode =
        // "8342508101", Nps/NpsDelayed/NpsDa/NpsPay/NpsPayDiff = the five
        // trailing numbers in that fixed order.
        private static bool TryParseNpsLine(string line, out NpsLineResult result)
        {
            result = null;
            var m = Regex.Match(line.Trim(),
                @"^(?<type>.+?)\s+(?<acct>\d{8,})\s+(?<nps>[\d,]+)\s+(?<delayed>[\d,]+)\s+(?<da>[\d,]+)\s+(?<pay>[\d,]+)\s+(?<paydiff>[\d,]+)\s*$");
            if (!m.Success) return false;

            result = new NpsLineResult
            {
                TypeOfEmployee = m.Groups["type"].Value.Trim(),
                HeadOfAccountCode = m.Groups["acct"].Value,
                Nps = ParseAmount(m.Groups["nps"].Value),
                NpsDelayed = ParseAmount(m.Groups["delayed"].Value),
                NpsDa = ParseAmount(m.Groups["da"].Value),
                NpsPay = ParseAmount(m.Groups["pay"].Value),
                NpsPayDiff = ParseAmount(m.Groups["paydiff"].Value)
            };
            return true;
        }

        // ---------------------------------------------------------------
        // Header/label-value fields. Most live on their own dedicated line
        // and are captured directly. A few genuinely share a printed line
        // with an unrelated field from the form's second column (e.g.
        // "Treasury Code/2210PURANDHAR, SUB Administrative Department:
        // Voucher No.:") -- those side-by-side pairs are NOT reliably
        // separable by text alone (there's no delimiter between the two
        // columns once word-spacing is collapsed), so this method
        // intentionally does not attempt to extract every single field on
        // the form; it captures the fields that are unambiguous.
        // ---------------------------------------------------------------
        private static void CaptureMasterInfo(string fullText, DataTable masterTable, DataTable debugTable)
        {
            AddIfMatch(masterTable, "Bill For", fullText, @"Bill for\s*:\s*(.+?)(?:\n|$)");
            AddIfMatch(masterTable, "Name of Office", fullText, @"Name of Office\s*:\s*(.+?)\s+Month\s*:");
            AddIfMatch(masterTable, "Month", fullText, @"Month\s*:\s*(.+?)\s+Year\s*:");
            AddIfMatch(masterTable, "Year", fullText, @"Year\s*:\s*(.+?)\s+Bill Id\s*:");
            AddIfMatch(masterTable, "Bill Id", fullText, @"Bill Id\s*:\s*(\S+)");
            AddIfMatch(masterTable, "Bill Name", fullText, @"BILL Name\s*:\s*(.+?)(?:\n|$)");
            AddIfMatch(masterTable, "Demand No", fullText, @"Demand No\.\s*:\s*(\S+)");
            AddIfMatch(masterTable, "Drawing Officer Code", fullText, @"Drawing Officer'?s?\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Major Head", fullText, @"Major Head\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Sub-Major Head", fullText, @"Sub-Major Head\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Minor Head", fullText, @"Minor Head\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Sub-Minor Head", fullText, @"Sub-Minor Head\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Detail Head", fullText, @"Detail Head\s*:\s*(\d+\s+[A-Z]+)\s+Scheme Code\s*:");
            AddIfMatch(masterTable, "Scheme Code", fullText, @"Scheme Code\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Voucher No", fullText, @"Voucher No\s*:\s*(\d+)");
            AddIfMatch(masterTable, "Voucher Date", fullText, @"Voucher Date\s*:\s*(\S+)");
            AddIfMatch(masterTable, "Bill Generation Time", fullText, @"BILL GENERATION TIME\s*:\s*(.+?)(?:\n|$)");

            if (masterTable.Rows.Count == 0)
                debugTable.Rows.Add("CaptureMasterInfo", "No header fields matched -- layout may differ from the reference samples.");
        }

        private static void AddIfMatch(DataTable table, string field, string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (m.Success)
                table.Rows.Add(field, m.Groups[1].Value.Trim());
        }

        private static decimal ParseAmount(string value) =>
            decimal.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);

        private static int ParseInt(string value) =>
            int.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);
    }
}
