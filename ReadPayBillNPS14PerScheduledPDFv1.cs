using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EFISupportApp
{
    public class ReadPayBillNPS14PerScheduledPDFv1
    {
        // Tolerance (PDF points) for matching a word/letter to a column anchor.
        private const double ColumnTolerance = 2.0;

        // Tolerance (PDF points) for clustering letters/words that sit on the
        // same printed line into one "row" of text.
        private const double RowGroupTolerance = 2.0;

        // How far above the first record's main row / below the last record's
        // main row we still look for that record's own overflow content
        // (floating NPS-Regular-Amt line above, PRAN-tail/Code/end-date below).
        // Calibrated so it comfortably includes that overflow (~12-15pt) but
        // stops well short of the page header (~23pt above) or footer / grand
        // total text (~30-37pt below) on this report.
        private const double PageEdgeBuffer = 20.0;

        // Name/Code letters live left of this X; PRAN letters live in
        // [PranColMinX, PranColMaxX); numeric columns are matched by right edge.
        private const double NameColMaxX = 165.0;
        private const double PranColMinX = 165.0;
        private const double PranColMaxX = 206.0;

        // ------------------------------------------------------------------
        // FALLBACK ONLY. These were calibrated against one specific bill's
        // rendering (DDO 24010145). The report engine auto-fits column
        // widths per bill -- e.g. the "basic" column's right edge measured
        // 231.3pt in that bill but only 223.8pt in a different bill (DDO
        // 24010136), a shift well past ColumnTolerance. Because of this,
        // these constants are NOT reliable across different bills/files and
        // are only used if per-page self-calibration (DetectNumericColumnAnchors)
        // fails to find a clean data row to calibrate from.
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, double> NumericColumnRightEdge = new()
        {
            { "basic", 229.7 },
            { "da", 283.7 },
            { "totalbasicda", 321.9 },
            { "npsregular", 403.2 },
            { "emp10", 706.4 },
            { "emp14", 744.9 },
            { "total24", 783.4 },
        };

        // Column order matches the left-to-right layout of the 11 numeric
        // cells in a genuine data row (Basic, DA, Total, NPSRegularAmt, the
        // four blank/zero duration-amounts, then Emp10%, Emp14%, Total24%).
        // All 11 must be present for a row to count as a valid calibration row.
        private static readonly string[] CalibrationColumnOrder =
        {
            "basic", "da", "totalbasicda", "npsregular",
            "delay0", "daarr0", "payarr0", "payarrdiff0",
            "emp10", "emp14", "total24"
        };

        // Columns we actually keep as anchors (matches NumericColumnRightEdge's key set).
        private static readonly string[] KeptAnchorKeys =
        {
            "basic", "da", "totalbasicda", "npsregular", "emp10", "emp14", "total24"
        };

        private static readonly Regex PlainNumber = new Regex(@"^[\d,]+$", RegexOptions.Compiled);
        private static readonly Regex CodeInParens = new Regex(@"\(([A-Za-z0-9]+)\)", RegexOptions.Compiled);
        private static readonly Regex LeadingDigits = new Regex(@"^\d+", RegexOptions.Compiled);

        public static DataSet ExtractFromPdf(string pdfPath)
        {
            var ds = new DataSet("NpsContributionReport");

            var masterTable = new DataTable("Master");
            masterTable.Columns.Add("Field");
            masterTable.Columns.Add("Value");
            ds.Tables.Add(masterTable);

            var dataTable = BuildEmptyDataTable();
            ds.Tables.Add(dataTable);

            var debugTable = new DataTable("Debug");
            debugTable.Columns.Add("Step");
            debugTable.Columns.Add("Detail");
            ds.Tables.Add(debugTable);

            using var document = PdfDocument.Open(pdfPath);

            int srNo = 0;
            bool masterCaptured = false;
            int pageNum = 0;

            // Anchors are cached once calibrated so that if a later page
            // (e.g. a footer/certificate-only page) has no data row to
            // calibrate from, we reuse the last good calibration for this
            // document rather than falling back to the (possibly wrong)
            // hardcoded constants.
            Dictionary<string, double>? cachedAnchors = null;

            foreach (var page in document.GetPages())
            {
                pageNum++;
                var words = page.GetWords().ToList();
                var letters = page.Letters;

                if (!masterCaptured)
                {
                    CaptureMasterInfo(words, masterTable);
                    masterCaptured = true;
                }

                // ---- 0. Self-calibrate this page's numeric column anchors ----
                var detectedAnchors = DetectNumericColumnAnchors(words);
                Dictionary<string, double> columnAnchors;
                if (detectedAnchors != null)
                {
                    columnAnchors = detectedAnchors;
                    cachedAnchors = detectedAnchors;
                    debugTable.Rows.Add($"Page {pageNum}", "Self-calibrated column anchors from this page's data row.");
                }
                else if (cachedAnchors != null)
                {
                    columnAnchors = cachedAnchors;
                    debugTable.Rows.Add($"Page {pageNum}", "No data row to self-calibrate from -- reusing previous page's anchors.");
                }
                else
                {
                    columnAnchors = NumericColumnRightEdge;
                    debugTable.Rows.Add($"Page {pageNum}", "No data row to self-calibrate from -- falling back to hardcoded anchors (may be wrong for this bill).");
                }

                // ---- 1. Right-aligned numeric columns, matched by right edge ----
                var numericBuckets = columnAnchors.Keys.ToDictionary(k => k, k => new List<(double Y, string Text)>());

                foreach (var word in words)
                {
                    if (!PlainNumber.IsMatch(word.Text)) continue;
                    double right = word.BoundingBox.Right;
                    foreach (var kv in columnAnchors)
                    {
                        if (Math.Abs(right - kv.Value) <= ColumnTolerance)
                        {
                            numericBuckets[kv.Key].Add((word.BoundingBox.Centroid.Y, word.Text));
                            break;
                        }
                    }
                }
                foreach (var key in numericBuckets.Keys.ToList())
                    numericBuckets[key] = numericBuckets[key].OrderByDescending(t => t.Y).ToList(); // top -> bottom

                int n = numericBuckets["basic"].Count;
                debugTable.Rows.Add($"Page {pageNum}: employee rows detected (Basic column count)", n.ToString());

                if (n == 0)
                {
                    debugTable.Rows.Add($"Page {pageNum}", "No numeric data rows found -- skipping (likely a non-data page).");
                    continue;
                }

                var mainY = numericBuckets["basic"].Select(t => t.Y).ToList();

                // ---- 2. PRAN, from raw letters in [PranColMinX, PranColMaxX) ----
                var pranLetters = letters
                    .Where(l => l.GlyphRectangle.Left >= PranColMinX && l.GlyphRectangle.Left < PranColMaxX
                                && l.Value.Length > 0 && char.IsDigit(l.Value[0]))
                    .OrderByDescending(l => l.GlyphRectangle.Centroid.Y)
                    .ThenBy(l => l.GlyphRectangle.Left)
                    .ToList();

                var pranRows = GroupLettersIntoRows(pranLetters);
                var pranTokens = pranRows
                    .Select(r => (Y: r[0].GlyphRectangle.Centroid.Y,
                                   Text: string.Concat(r.OrderBy(c => c.GlyphRectangle.Left).Select(c => c.Value))))
                    .OrderByDescending(t => t.Y)
                    .ToList();
                var pran1 = pranTokens.Where(t => t.Text.Length >= 8).ToList(); // long half of the PRAN
                var pran2 = pranTokens.Where(t => t.Text.Length < 8).ToList();  // short trailing half

                // ---- 3. Name + Code, from raw letters left of NameColMaxX, banded per record ----
                var nameCodeLetters = letters.Where(l => l.GlyphRectangle.Left < NameColMaxX).ToList();

                var bands = new List<(double Upper, double Lower)>();
                for (int i = 0; i < n; i++)
                {
                    double upper = (i == 0) ? mainY[i] + PageEdgeBuffer : (mainY[i - 1] + mainY[i]) / 2.0;
                    double lower = (i == n - 1) ? mainY[i] - PageEdgeBuffer : (mainY[i] + mainY[i + 1]) / 2.0;
                    bands.Add((upper, lower));
                }

                int matchFailCount = 0;

                for (int i = 0; i < n; i++)
                {
                    var (upper, lower) = bands[i];
                    var bandLetters = nameCodeLetters
                        .Where(l => l.GlyphRectangle.Centroid.Y <= upper && l.GlyphRectangle.Centroid.Y > lower)
                        .OrderByDescending(l => l.GlyphRectangle.Centroid.Y)
                        .ThenBy(l => l.GlyphRectangle.Left)
                        .ToList();

                    var rowGroups = GroupLettersIntoRows(bandLetters);

                    var nameParts = new List<string>();
                    string codeVal = null;

                    foreach (var row in rowGroups)
                    {
                        var ordered = row.OrderBy(c => c.GlyphRectangle.Left).ToList();

                        StringBuilder sb = new StringBuilder();

                        for (int j = 0; j < ordered.Count; j++)
                        {
                            if (j > 0)
                            {
                                double gap = ordered[j].GlyphRectangle.Left - ordered[j - 1].GlyphRectangle.Right;

                                // Tune this value if necessary
                                if (gap > 1.8)
                                    sb.Append(' ');
                            }

                            sb.Append(ordered[j].Value);
                        }

                        string txt = sb.ToString();

                        if (txt.Contains('('))
                        {
                            var m = CodeInParens.Match(txt);
                            if (m.Success) codeVal = m.Groups[1].Value;
                        }
                        else
                        {
                            // Strip a glued leading Sr.No (e.g. "1Gaikwad" -> "Gaikwad").
                            string stripped = LeadingDigits.Replace(txt, "").Trim();
                            if (stripped.Length > 0) nameParts.Add(stripped);
                        }
                    }

                    string basic = numericBuckets["basic"][i].Text;
                    string da = numericBuckets["da"].Count > i ? numericBuckets["da"][i].Text : null;
                    string totalBasicDa = numericBuckets["totalbasicda"].Count > i ? numericBuckets["totalbasicda"][i].Text : null;
                    string npsRegular = numericBuckets["npsregular"].Count > i ? numericBuckets["npsregular"][i].Text : null;
                    string emp10 = numericBuckets["emp10"].Count > i ? numericBuckets["emp10"][i].Text : null;
                    string emp14 = numericBuckets["emp14"].Count > i ? numericBuckets["emp14"][i].Text : null;
                    string total24 = numericBuckets["total24"].Count > i ? numericBuckets["total24"][i].Text : null;
                    string pranFull = (pran1.Count > i ? pran1[i].Text : "") + (pran2.Count > i ? pran2[i].Text : "");

                    if (da == null || totalBasicDa == null || total24 == null)
                    {
                        matchFailCount++;
                        if (matchFailCount <= 10)
                            debugTable.Rows.Add($"Page {pageNum} record #{i + 1} incomplete",
                                $"basic={basic} da={da} totalBasicDa={totalBasicDa} total24={total24}");
                        continue;
                    }

                    srNo++;
                    var row2 = dataTable.NewRow();
                    row2["SrNo"] = srNo;
                    row2["EmployeeName"] = string.Join(" ", nameParts);
                    row2["EmployeeCode"] = codeVal ?? string.Empty;
                    row2["PranNo"] = pranFull;
                    row2["BasicPay"] = ParseAmount(basic);
                    row2["DaAmount"] = ParseAmount(da);
                    row2["TotalBasicPlusDa"] = ParseAmount(totalBasicDa);
                    row2["NpsRegularAmt"] = npsRegular != null ? ParseAmount(npsRegular) : 0m;
                    row2["Total10PercentEmployee"] = emp10 != null ? ParseAmount(emp10) : 0m;
                    row2["Total14PercentEmployer"] = emp14 != null ? ParseAmount(emp14) : 0m;
                    row2["Total24PercentContribution"] = ParseAmount(total24);
                    dataTable.Rows.Add(row2);
                }

                debugTable.Rows.Add($"Page {pageNum} records where a required field was missing", matchFailCount.ToString());
            }

            debugTable.Rows.Add("Rows successfully parsed", dataTable.Rows.Count.ToString());
            CaptureGrandTotal(dataTable, masterTable);

            return ds;
        }

        // ---------------------------------------------------------------
        // Derives this page's own numeric-column right-edge anchors from
        // its first fully-populated data row, instead of relying on
        // constants calibrated against a different bill's layout.
        //
        // Every genuine employee row has exactly 11 plain-number tokens in
        // a fixed left-to-right order once the Sr.No and PRAN columns are
        // excluded (x0 > 195, i.e. past the PRAN column):
        //   Basic, DA, Total(Basic+DA), NPSRegularAmt,
        //   Delay(0), DAArrears(0), PayArrears(0), PayArrearsDiff(0),
        //   Emp10%, Emp14%, Total24%
        // The first row on the page with exactly 11 such tokens is treated
        // as a genuine data row; its tokens are sorted by X and their right
        // edges are read off as the anchors.
        //
        // Returns null if no clean 11-number row is found on this page
        // (e.g. a certificate/footer-only page), so the caller can fall
        // back to a cached calibration or the hardcoded constants.
        // ---------------------------------------------------------------
        private static Dictionary<string, double> DetectNumericColumnAnchors(List<Word> words)
        {
            var candidates = words
                .Where(w => PlainNumber.IsMatch(w.Text) && w.BoundingBox.Left > 195.0)
                .ToList();

            var rows = new List<List<Word>>();
            foreach (var w in candidates
                         .OrderByDescending(w => w.BoundingBox.Centroid.Y)
                         .ThenBy(w => w.BoundingBox.Left))
            {
                var match = rows.FirstOrDefault(r =>
                    Math.Abs(r[0].BoundingBox.Centroid.Y - w.BoundingBox.Centroid.Y) <= RowGroupTolerance);
                if (match != null) match.Add(w);
                else rows.Add(new List<Word> { w });
            }

            var dataRow = rows.FirstOrDefault(r => r.Count == CalibrationColumnOrder.Length);
            if (dataRow == null) return null;

            var ordered = dataRow.OrderBy(w => w.BoundingBox.Left).ToList();
            var full = new Dictionary<string, double>();
            for (int i = 0; i < CalibrationColumnOrder.Length; i++)
                full[CalibrationColumnOrder[i]] = ordered[i].BoundingBox.Right;

            return KeptAnchorKeys.ToDictionary(k => k, k => full[k]);
        }

        // ---------------------------------------------------------------
        // Clusters letters (already ordered by descending Y then X) into
        // printed lines using RowGroupTolerance.
        // ---------------------------------------------------------------
        private static List<List<Letter>> GroupLettersIntoRows(List<Letter> orderedLetters)
        {
            var rows = new List<List<Letter>>();
            foreach (var l in orderedLetters)
            {
                if (rows.Count > 0 &&
                    Math.Abs(rows[^1][0].GlyphRectangle.Centroid.Y - l.GlyphRectangle.Centroid.Y) <= RowGroupTolerance)
                {
                    rows[^1].Add(l);
                }
                else
                {
                    rows.Add(new List<Letter> { l });
                }
            }
            return rows;
        }

        // Single source of truth for the output schema. To add, remove, or
        // reorder a column, edit this list only -- BuildEmptyDataTable() below
        // builds the DataTable from it dynamically instead of via repeated,
        // hand-written Columns.Add calls.
        private static readonly (string Name, Type Type)[] DataColumns =
        {
            ("SrNo", typeof(int)),
            ("EmployeeName", typeof(string)),
            ("EmployeeCode", typeof(string)),
            ("PranNo", typeof(string)),
            ("BasicPay", typeof(decimal)),
            ("DaAmount", typeof(decimal)),
            ("TotalBasicPlusDa", typeof(decimal)),
            ("NpsRegularAmt", typeof(decimal)),
            ("Total10PercentEmployee", typeof(decimal)),
            ("Total14PercentEmployer", typeof(decimal)), // <-- highlighted column
            ("Total24PercentContribution", typeof(decimal)),
        };

        private static DataTable BuildEmptyDataTable()
        {
            var table = new DataTable("NpsContribution");
            foreach (var (name, type) in DataColumns)
                table.Columns.Add(name, type);
            return table;
        }

        private static decimal ParseAmount(string value) =>
            decimal.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------
        // Report header / master details -- these live in a region of the
        // page where words aren't right-aligned or glued, so simple word-Y
        // row reconstruction (the ORIGINAL approach) is fine here. Only the
        // employee data table needed the character-level rework above.
        // ---------------------------------------------------------------
        private static void CaptureMasterInfo(List<Word> words, DataTable masterTable)
        {
            var rows = new List<(double Y, string Text)>();
            foreach (var w in words.OrderByDescending(w => w.BoundingBox.Centroid.Y).ThenBy(w => w.BoundingBox.Centroid.X))
            {
                double y = w.BoundingBox.Centroid.Y;
                int idx = rows.FindIndex(r => Math.Abs(r.Y - y) <= 3.0);
                if (idx == -1) rows.Add((y, w.Text));
                else rows[idx] = (rows[idx].Y, rows[idx].Text + " " + w.Text);
            }

            string fullText = string.Join("\n", rows.OrderByDescending(r => r.Y).Select(r => r.Text));

            AddIfMatch(masterTable, "Treasury", fullText, @"Treasury\s*:\s*(.+?)(?:\n|Name of the Office)");
            AddIfMatch(masterTable, "Office Name", fullText, @"Name of the Office\s*:\s*(.+?)(?:\n|Bill Group Name)");
            AddIfMatch(masterTable, "Bill Group Name", fullText, @"Bill Group Name\s*:\s*(.+?)(?:\n|$)");
            AddIfMatch(masterTable, "Report Month", fullText, @"For the month of\s*(.+?)(?:\n|Treasury)");
            AddIfMatch(masterTable, "Report Generated On", fullText, @"Employee Projected Report\s*([\d\-: ]+)");
        }

        private static void CaptureGrandTotal(DataTable dataTable, DataTable masterTable)
        {
            if (dataTable.Rows.Count == 0) return;

            decimal totalBasic = dataTable.AsEnumerable().Sum(r => r.Field<decimal>("BasicPay"));
            decimal totalDa = dataTable.AsEnumerable().Sum(r => r.Field<decimal>("DaAmount"));
            decimal totalBasicDa = dataTable.AsEnumerable().Sum(r => r.Field<decimal>("TotalBasicPlusDa"));
            decimal total14 = dataTable.AsEnumerable().Sum(r => r.Field<decimal>("Total14PercentEmployer"));

            masterTable.Rows.Add("Grand Total Basic Pay", totalBasic.ToString("N0"));
            masterTable.Rows.Add("Grand Total DA", totalDa.ToString("N0"));
            masterTable.Rows.Add("Grand Total (Basic+DA)", totalBasicDa.ToString("N0"));
            masterTable.Rows.Add("Grand Total 14% Employer Contribution", total14.ToString("N0"));
            masterTable.Rows.Add("Employee Count", dataTable.Rows.Count.ToString());
        }

        private static void AddIfMatch(DataTable table, string field, string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (m.Success)
                table.Rows.Add(field, m.Groups[1].Value.Trim());
        }
    }
}
