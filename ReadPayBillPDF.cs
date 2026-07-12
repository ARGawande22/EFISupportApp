using EFISupportApp.Models.PayBill;
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
    public class ReadPayBillPDF
    {
        // Row-clustering tolerance: words within this many PDF points of Y are
        // considered to be on the same printed line.
        private const double YTolerance = 3.0;

        // Legend/placeholder tokens that appear literally inside header rows but
        // are NOT per-employee data. These must never be assigned to a column.
        private static readonly HashSet<string> PlaceholderTokens =
            new(StringComparer.OrdinalIgnoreCase) { "Cell", "@", "Total" };

        public DataSet ExtractFromPdf(string pdfPath)
        {
            var ds = new DataSet("Paybill");

            using var document = PdfDocument.Open(pdfPath);

            // Master/header info only needs to be captured once (first page).
            var masterTable = new DataTable("Master");
            masterTable.Columns.Add("Field");
            masterTable.Columns.Add("Value");
            ds.Tables.Add(masterTable);

            bool masterCaptured = false;
            int sectionNumber = 0;

            foreach (var page in document.GetPages())
            {
                var rows = GroupWordsIntoRows(page.GetWords());

                if (!masterCaptured)
                {
                    CaptureMasterInfo(rows, masterTable);
                    masterCaptured = true;
                }

                // A page can contain one salary section (as in this bill format).
                // Detect it via the "Code" row, which tells us the employee codes
                // (and therefore the employee COUNT) for this section.
                var codeRow = rows.FirstOrDefault(r => r.FirstWordText.Equals("Code", StringComparison.OrdinalIgnoreCase));
                if (codeRow == null)
                    continue; // no salary table on this page (e.g. a pure cover page)

                sectionNumber++;
                var codes = ExtractEmployeeCodes(codeRow);
                if (codes.Count == 0)
                    continue;

                if (!TryBuildAnchorsFromDataRow(rows, codes.Count, out var employeeAnchors, out var totalAnchor))
                {
                    // Could not find a clean fully-numeric row to anchor on;
                    // skip this section rather than emit misaligned data.
                    continue;
                }

                var sectionTable = BuildSectionTable(rows, codes, employeeAnchors, totalAnchor, sectionNumber);
                ds.Tables.Add(sectionTable);
            }

            return ds;
        }

        // ---------------------------------------------------------------
        // Row reconstruction: cluster words by Y, order each row by X
        // ---------------------------------------------------------------
        private List<PdfRow> GroupWordsIntoRows(IEnumerable<Word> words)
        {
            var rows = new List<PdfRow>();

            foreach (var word in words.OrderByDescending(w => w.BoundingBox.Centroid.Y)
                                       .ThenBy(w => w.BoundingBox.Centroid.X))
            {
                double y = word.BoundingBox.Centroid.Y;
                var row = rows.FirstOrDefault(r => Math.Abs(r.Y - y) <= YTolerance);
                if (row == null)
                {
                    row = new PdfRow { Y = y };
                    rows.Add(row);
                }
                row.Words.Add(word);
            }

            foreach (var row in rows)
                row.Words = row.Words.OrderBy(w => w.BoundingBox.Centroid.X).ToList();

            return rows.OrderByDescending(r => r.Y).ToList();
        }

        // ---------------------------------------------------------------
        // Employee codes: only used for NAMING columns and getting the
        // employee count. X-positions are NOT taken from this row (see
        // TryBuildAnchorsFromDataRow) because the bracketed code text is a
        // different width than the numbers beneath it, which was the
        // original cause of misalignment.
        // ---------------------------------------------------------------
        private List<string> ExtractEmployeeCodes(PdfRow codeRow)
        {
            var bracketGroups = GroupBracketedTokens(codeRow.Words.Skip(1)); // skip the literal "Code" label
            return bracketGroups
                .Select(g => string.Join(" ", g.Select(w => w.Text)).Trim('[', ']', ' '))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        // Matches either a comma-grouped number ("81,200") or a plain digit
        // sequence of any length ("81200", "433800") -- this document prints
        // amounts WITHOUT thousands separators, so the plain-digit branch is
        // the one that actually matters for values like Basic Pay (previously
        // missing, which silently blanked out the whole Basic Pay row since
        // every value there is a multi-digit number with no comma).
        private static readonly Regex PureNumberRegex = new(
            @"^\d{1,3}(,\d{3})*(\.\d+)?$|^\d+(\.\d+)?$", RegexOptions.Compiled);

        /// <summary>
        /// Finds a fully-numeric line-item row (e.g. "1 Offici Pay 81200 ... 433800 1")
        /// and uses ITS word X-positions as the true column anchors: one per employee,
        /// plus one dedicated anchor for the Total column. Rows with compound cell
        /// values (e.g. FA's "8/10 1250.00") are automatically skipped because their
        /// token count won't match employeeCount + 1.
        /// </summary>
        private bool TryBuildAnchorsFromDataRow(List<PdfRow> rows, int employeeCount,
            out List<ColumnAnchor> employeeAnchors, out ColumnAnchor totalAnchor)
        {
            employeeAnchors = new List<ColumnAnchor>();
            totalAnchor = new ColumnAnchor { Index = employeeCount, X = 0 };

            foreach (var row in rows)
            {
                if (row.Words.Count < 3) continue;
                if (!int.TryParse(row.Words[0].Text, out int slNo)) continue;
                if (slNo < 1 || slNo > 30) continue;

                var last = row.Words[^1];
                bool trailingRepeats = last.Text == row.Words[0].Text;
                var core = trailingRepeats ? row.Words.Skip(1).Take(row.Words.Count - 2) : row.Words.Skip(1);

                var numericTokens = core.Where(w => PureNumberRegex.IsMatch(w.Text)).ToList();

                if (numericTokens.Count == employeeCount + 1)
                {
                    for (int i = 0; i < employeeCount; i++)
                        employeeAnchors.Add(new ColumnAnchor { Index = i, X = numericTokens[i].BoundingBox.Centroid.X });

                    totalAnchor.X = numericTokens[employeeCount].BoundingBox.Centroid.X;
                    totalAnchor.Code = "Total";
                    return true;
                }
            }

            return false; // no clean anchor row found for this section
        }

        /// <summary>Groups consecutive words that together form one [bracket ...] token.</summary>
        private List<List<Word>> GroupBracketedTokens(IEnumerable<Word> words)
        {
            var groups = new List<List<Word>>();
            List<Word>? current = null;

            foreach (var w in words)
            {
                if (w.Text.Contains('['))
                {
                    current = new List<Word>();
                    groups.Add(current);
                }
                current?.Add(w);
            }
            return groups;
        }

        // ---------------------------------------------------------------
        // ONE table per section/page: header rows (Name, Code, PayLevel,
        // BasicPay) followed by all SL-NO line items, sharing the same
        // dynamic employee columns + a Total column.
        // ---------------------------------------------------------------
        private DataTable BuildSectionTable(List<PdfRow> rows, List<string> codes,
            List<ColumnAnchor> employeeAnchors, ColumnAnchor totalAnchor, int sectionNumber)
        {
            // Assign the employee code strings onto the anchors (anchors
            // were built purely from X-positions in TryBuildAnchorsFromDataRow).
            for (int i = 0; i < employeeAnchors.Count && i < codes.Count; i++)
                employeeAnchors[i].Code = codes[i];

            var allAnchors = new List<ColumnAnchor>(employeeAnchors) { totalAnchor };

            var table = new DataTable($"Section{sectionNumber}");
            table.Columns.Add("SlNo", typeof(string)); // string: blank for header rows, "1".."23" for line items
            table.Columns.Add("RowLabel", typeof(string));
            foreach (var anchor in employeeAnchors)
                table.Columns.Add(anchor.Code, typeof(string)); // raw text; parse via CleanNumber() as needed
            table.Columns.Add("Total", typeof(string));

            // Average gap between anchors -- used to set the label/data
            // boundary for line items (header rows below no longer use
            // anchor X-positions at all -- see the header-row parsers).
            double gap = employeeAnchors.Count >= 2
                ? (employeeAnchors[^1].X - employeeAnchors[0].X) / (employeeAnchors.Count - 1)
                : 60;
            double labelBoundary = employeeAnchors[0].X - gap / 2.0;

            // --- Header rows: Name, Code, Pay Level, Basic Pay ---
            var nameRow = rows.FirstOrDefault(r => r.FirstWordText.Equals("Name", StringComparison.OrdinalIgnoreCase));
            var levelRow = rows.FirstOrDefault(r => r.FirstWordText.Equals("Level", StringComparison.OrdinalIgnoreCase));
            var basicPayRow = rows.FirstOrDefault(r => r.FullText.StartsWith("Basic Pay", StringComparison.OrdinalIgnoreCase));

            int employeeCount = employeeAnchors.Count;

            AddHeaderRow(table, "Code", employeeAnchors, codes);
            if (nameRow != null)
                AddHeaderRow(table, "Name", employeeAnchors, ParseNameRow(nameRow, employeeCount));
            if (levelRow != null)
                AddHeaderRow(table, "PayLevel", employeeAnchors, ParseLevelRow(levelRow, employeeCount));
            if (basicPayRow != null)
                AddHeaderRow(table, "BasicPay", employeeAnchors, ParseBasicPayRow(basicPayRow, employeeCount));

            // --- Line-item rows: "1 Offici Pay 81200 ... 433800 1" ---
            foreach (var row in rows)
            {
                if (row.Words.Count < 3) continue;
                if (!int.TryParse(row.Words[0].Text, out int slNo)) continue;
                if (slNo < 1 || slNo > 30) continue;

                var last = row.Words[^1];
                bool trailingRepeats = last.Text == row.Words[0].Text;
                var core = (trailingRepeats ? row.Words.Skip(1).Take(row.Words.Count - 2) : row.Words.Skip(1)).ToList();

                var labelWords = core.Where(w => w.BoundingBox.Centroid.X < labelBoundary).ToList();
                var valueWords = core.Where(w => w.BoundingBox.Centroid.X >= labelBoundary).ToList();

                string label = string.Join(" ", labelWords.Select(w => w.Text)).Trim();
                if (string.IsNullOrWhiteSpace(label)) continue;

                var assigned = AssignWordsToColumns(valueWords, allAnchors);

                var newRow = table.NewRow();
                newRow["SlNo"] = slNo.ToString(CultureInfo.InvariantCulture);
                newRow["RowLabel"] = label;
                foreach (var anchor in employeeAnchors)
                {
                    assigned.TryGetValue(anchor.Index, out var cellText);
                    newRow[anchor.Code] = cellText?.Trim() ?? "";
                }
                assigned.TryGetValue(totalAnchor.Index, out var totalText);
                newRow["Total"] = totalText?.Trim() ?? "";

                table.Rows.Add(newRow);
            }

            return table;
        }

        // ---------------------------------------------------------------
        // Header-row parsers
        //
        // IMPORTANT: these do NOT use X-coordinates / column anchors at all.
        // Earlier versions tried to place each header cell by nearest-anchor
        // X position, but in this PDF format a long name or value routinely
        // overflows past its own column's visual width -- so its actual
        // printed X-coordinate can sit closer to a NEIGHBORING column's
        // anchor than to its own. No coordinate threshold can fix that,
        // because the words are, spatially, genuinely in the wrong place.
        //
        // Instead, each row is split into per-employee "cells" using rules
        // about the row's own text structure (an initial-letter pattern for
        // names, bracket-pairing for pay level, a plain numeric-token count
        // for basic pay). Because the underlying words are already ordered
        // left-to-right (see GroupWordsIntoRows) and employees are always
        // listed in that same left-to-right order everywhere on the page
        // (Code row, Name row, line items, etc.), cell #1 -> employee #1,
        // cell #2 -> employee #2, and so on -- purely by POSITION, not X.
        // ---------------------------------------------------------------

        private static readonly Regex InitialRegex = new(@"^[A-Za-z]\.$", RegexOptions.Compiled);

        /// <summary>
        /// Name row: e.g. "Name A. G. Durgude C. V. Dhaigode K. V. Harpale ...".
        /// Each employee's name is one or more "X." initials followed by a
        /// (possibly multi-word) surname. A new employee's name begins the
        /// moment we see another initial AFTER a surname word has already
        /// been collected for the current cell -- this is a pure text-pattern
        /// rule and does not depend on where the words happen to sit on the
        /// page, so column-overflow cannot break it.
        /// </summary>
        private List<string> ParseNameRow(PdfRow nameRow, int employeeCount)
        {
            var words = nameRow.Words.Skip(1).ToList(); // skip the "Name" label
            var clusters = SplitNameClusters(words);

            // Sanity fallback: if the initials pattern didn't yield exactly
            // one cell per employee (e.g. an unusual name format), fall back
            // to splitting evenly by the largest gaps -- still an ordinal,
            // position-based split, just using a different signal.
            if (clusters.Count != employeeCount && words.Count > 0)
                clusters = SplitByLargestGaps(words, employeeCount);

            return ToOrderedCellTexts(clusters, employeeCount);
        }

        private List<List<Word>> SplitNameClusters(List<Word> words)
        {
            var clusters = new List<List<Word>>();
            var current = new List<Word>();

            foreach (var w in words)
            {
                bool isInitial = InitialRegex.IsMatch(w.Text);
                bool currentHasSurname = current.Any(x => !InitialRegex.IsMatch(x.Text));

                if (isInitial && currentHasSurname)
                {
                    clusters.Add(current);
                    current = new List<Word>();
                }
                current.Add(w);
            }
            if (current.Count > 0) clusters.Add(current);

            return clusters;
        }

        /// <summary>
        /// Pay Level row: e.g. "Level [ Cell ] [ Pay Level 16 ][21] [ Pay Level 15 ][12] ...".
        /// The leading "[ Cell ]" is a legend, not data. Each employee's real cell is made
        /// of exactly TWO bracket groups ("[ Pay Level 16 ]" + "[21]") that are paired
        /// together, then assigned to employees strictly in left-to-right order.
        /// </summary>
        private List<string> ParseLevelRow(PdfRow levelRow, int employeeCount)
        {
            var words = levelRow.Words.Skip(1).ToList(); // skip the "Level" label
            var bracketGroups = GroupBracketedTokens(words);

            // Drop the leading "[ Cell ]" legend group if present.
            int startIdx = 0;
            if (bracketGroups.Count > 0)
            {
                var firstText = string.Join(" ", bracketGroups[0].Select(w => w.Text)).Trim('[', ']', ' ');
                if (firstText.Equals("Cell", StringComparison.OrdinalIgnoreCase))
                    startIdx = 1;
            }
            var dataGroups = bracketGroups.Skip(startIdx).ToList();

            // Pair up consecutive bracket groups, in order: [PayLevel] + [CellNumber] = one cell.
            var cellTexts = new List<string>();
            for (int i = 0; i + 1 < dataGroups.Count; i += 2)
            {
                string levelText = string.Join(" ", dataGroups[i].Select(w => w.Text)).Trim('[', ']', ' ');
                string cellText = string.Join(" ", dataGroups[i + 1].Select(w => w.Text)).Trim('[', ']', ' ');
                cellTexts.Add($"{levelText} [{cellText}]");
            }

            return ToOrderedCellTexts(cellTexts, employeeCount);
        }

        /// <summary>Basic Pay row: e.g. "Basic Pay @ 81200 57900 ... 72100 Total".</summary>
        private List<string> ParseBasicPayRow(PdfRow basicPayRow, int employeeCount)
        {
            var words = basicPayRow.Words.Skip(2) // skip "Basic" "Pay" labels
                                          .Where(w => !PlaceholderTokens.Contains(w.Text.Trim('[', ']', ' ')))
                                          .Where(w => PureNumberRegex.IsMatch(w.Text))
                                          .OrderBy(w => w.BoundingBox.Centroid.X)
                                          .ToList();

            List<List<Word>> clusters;
            if (words.Count == employeeCount)
            {
                // The common case: exactly one clean basic-pay figure per employee.
                clusters = words.Select(w => new List<Word> { w }).ToList();
            }
            else
            {
                // Mismatch -- e.g. an employee has two basic-pay figures because of a
                // mid-period revision. Fall back to a position-based split into exactly
                // employeeCount groups (largest gaps become the cell boundaries), which
                // keeps such an extra figure together with its own employee's cell
                // instead of bleeding into a neighbor.
                clusters = words.Count > 0 ? SplitByLargestGaps(words, employeeCount) : new List<List<Word>>();
            }

            return ToOrderedCellTexts(clusters, employeeCount);
        }

        /// <summary>
        /// Splits a left-to-right ordered word list into exactly n groups by cutting at
        /// the n-1 LARGEST gaps between consecutive words. This guarantees exactly n
        /// clusters regardless of the absolute gap size (no threshold to mis-tune),
        /// as long as gaps between different cells are larger than gaps within one cell
        /// -- which holds for this document's layout.
        /// </summary>
        private List<List<Word>> SplitByLargestGaps(List<Word> words, int n)
        {
            var ordered = words.OrderBy(w => w.BoundingBox.Centroid.X).ToList();
            if (n <= 1 || ordered.Count <= n)
                return ordered.Select(w => new List<Word> { w }).ToList();

            var gaps = new List<(int Index, double Gap)>();
            for (int i = 0; i < ordered.Count - 1; i++)
                gaps.Add((i, ordered[i + 1].BoundingBox.Left - ordered[i].BoundingBox.Right));

            var splitAfterIndices = gaps.OrderByDescending(g => g.Gap)
                                         .Take(n - 1)
                                         .Select(g => g.Index)
                                         .ToHashSet();

            var clusters = new List<List<Word>>();
            var current = new List<Word>();
            for (int i = 0; i < ordered.Count; i++)
            {
                current.Add(ordered[i]);
                if (splitAfterIndices.Contains(i))
                {
                    clusters.Add(current);
                    current = new List<Word>();
                }
            }
            if (current.Count > 0) clusters.Add(current);

            return clusters;
        }

        /// <summary>Joins each Word cluster into cell text, ordered left-to-right, padded/truncated to employeeCount.</summary>
        private List<string> ToOrderedCellTexts(List<List<Word>> clusters, int employeeCount)
        {
            var texts = clusters.Select(c => string.Join(" ", c.Select(w => w.Text)).Trim()).ToList();
            return ToOrderedCellTexts(texts, employeeCount);
        }

        /// <summary>Pads/truncates an already-ordered list of cell texts to exactly employeeCount entries.</summary>
        private List<string> ToOrderedCellTexts(List<string> texts, int employeeCount)
        {
            if (texts.Count > employeeCount)
                texts = texts.Take(employeeCount).ToList();
            while (texts.Count < employeeCount)
                texts.Add("");
            return texts;
        }

        /// <summary>Adds a header-style row (Name/Code/PayLevel/BasicPay) to the unified section table.</summary>
        private void AddHeaderRow(DataTable table, string rowLabel, List<ColumnAnchor> employeeAnchors, List<string> directValues)
        {
            var newRow = table.NewRow();
            newRow["SlNo"] = "";
            newRow["RowLabel"] = rowLabel;

            for (int i = 0; i < employeeAnchors.Count; i++)
                newRow[employeeAnchors[i].Code] = i < directValues.Count ? directValues[i] : "";

            newRow["Total"] = "";
            table.Rows.Add(newRow);
        }

        /// <summary>
        /// Assigns a sequence of words to the nearest column anchor by X position,
        /// concatenating multiple words that land on the same anchor. Used for
        /// line-item rows, where each cell is reliably a single clean number and
        /// per-word assignment is safe (unlike the header rows above).
        /// </summary>
        private Dictionary<int, string> AssignWordsToColumns(IEnumerable<Word> words, List<ColumnAnchor> anchors)
        {
            var result = new Dictionary<int, string>();
            foreach (var w in words)
            {
                int idx = NearestAnchorIndex(w, anchors);
                if (idx < 0) continue;
                if (!result.ContainsKey(idx)) result[idx] = w.Text;
                else result[idx] += " " + w.Text;
            }
            return result;
        }

        private int NearestAnchorIndex(Word word, List<ColumnAnchor> anchors)
        {
            if (anchors.Count == 0) return -1;
            double x = word.BoundingBox.Centroid.X;
            var nearest = anchors.OrderBy(a => Math.Abs(a.X - x)).First();
            return nearest.Index;
        }

        private string CleanNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "0";
            // Keep only the last numeric token, e.g. "11/36 37580.00" -> "37580.00"
            var match = Regex.Match(raw, @"[\d,]+\.?\d*$");
            return match.Success ? match.Value.Replace(",", "") : "0";
        }

        // ---------------------------------------------------------------
        // Bill master/header details (Office, Bill ID, Voucher, dates, etc.)
        // ---------------------------------------------------------------
        private void CaptureMasterInfo(List<PdfRow> rows, DataTable masterTable)
        {
            string fullText = string.Join("\n", rows.Select(r => r.FullText));

            AddIfMatch(masterTable, "Office Name", fullText, @"Office Name\s*-\s*(.+?)(?:\n|BILL ID)");
            AddIfMatch(masterTable, "Bill ID", fullText, @"BILL ID\s*\((\d+)\)");
            AddIfMatch(masterTable, "Bill Type", fullText, @"BILL ID\s*\(\d+\)\)?\s*(.+?TEMPORARY.*?BILL)");
            AddIfMatch(masterTable, "Salary Month/Year", fullText, @"Salary for the Month and Year\s*:\s*(.+?)(?:\n|Bill Generation)");
            AddIfMatch(masterTable, "Bill Generation Time", fullText, @"Bill Generation Time\s*:\s*([\d\-: ]+)");
            AddIfMatch(masterTable, "Treasury Voucher Number", fullText, @"Treasury Voucher Number\s*:\s*(\S+)");
            AddIfMatch(masterTable, "Treasury Voucher Date", fullText, @"Treasury Voucher Date\s*:\s*([\d/]+)");
            AddIfMatch(masterTable, "DDO Code", fullText, @"\n(\d{10})\s+Office Name");
        }

        private void AddIfMatch(DataTable table, string field, string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (m.Success)
                table.Rows.Add(field, m.Groups[1].Value.Trim());
        }
    }
}
