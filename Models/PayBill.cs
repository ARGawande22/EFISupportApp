using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;

namespace EFISupportApp.Models.PayBill
{
    /// <summary>A single printed line reconstructed from word bounding boxes.</summary>
    public class PdfRow
    {
        public double Y;
        public List<Word> Words = new();
        public string FirstWordText => Words.Count > 0 ? Words[0].Text : string.Empty;
        public string FullText => string.Join(" ", Words.Select(w => w.Text));
    }

    /// <summary>One detected employee column anchor (X position) within a section.</summary>
    public class ColumnAnchor
    {
        public int Index;       // 0-based employee column index within the section
        public double X;        // anchor X position (center of a clean numeric value in this column)
        public string Code = "";
    }

}
