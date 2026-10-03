using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EFISupportApp.Models.BankStatement
{
    /// <summary>
    /// One row per PDF / per voucher (the statement header + footer info).
    /// </summary>
    public class MasterRecord
    {
        public int Id { get; set; }                      // surrogate key, assigned after parsing
        public string OfficeName { get; set; } = "";
        public string Month { get; set; } = "";           // e.g. "June"
        public int Year { get; set; }                     // e.g. 2026
        public string Treasury { get; set; } = "";
        public string DdoCode { get; set; } = "";
        public string VoucherNo { get; set; } = "";
        public DateTime? VoucherDate { get; set; }
        public decimal TotalAmountByPayOrder { get; set; }
        public decimal TotalAmountCreditedToEmployeeAccount { get; set; }
        public decimal ChequeAmount { get; set; }
        public DateTime? ReportGeneratedDate { get; set; }
        public string SourceFile { get; set; } = "";

        public override string ToString() =>
            $"[Master] Voucher {VoucherNo} | {OfficeName} | {Month} {Year} | DDO {DdoCode} | " +
            $"Credited={TotalAmountCreditedToEmployeeAccount:N0} | VoucherDate={VoucherDate:d}";
    }

    /// <summary>
    /// One row per employee payment line inside a statement.
    /// MasterId links back to the MasterRecord this line belongs to.
    /// </summary>
    public class EmpBankDetail
    {
        public int Id { get; set; }                      // surrogate key
        public int MasterId { get; set; }                 // FK -> MasterRecord.Id
        public int SrNo { get; set; }
        public string AccountNumber { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public decimal NetAmount { get; set; }
        public string PaymentType { get; set; } = "";      // often blank in the source data
        public string IfscCode { get; set; } = "";

        public override string ToString() =>
            $"[Emp] {SrNo,3} | {AccountNumber,-16} | {EmployeeName,-35} | {NetAmount,10:N0} | {IfscCode}";
    }

    public class ParsedStatement
    {
        public MasterRecord Master { get; set; } = new();
        public List<EmpBankDetail> Employees { get; set; } = new();
    }
}
