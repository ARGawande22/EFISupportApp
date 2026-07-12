using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;

namespace EFISupportApp.Models.PaySlip
{
    // ---------------------------------------------------------------------
    // Models
    // ---------------------------------------------------------------------

    public class SalaryLineItem
    {
        public string Particular { get; set; } = "";
        public decimal? Amount { get; set; }
        public string InstNo { get; set; } = "";

        public override string ToString() =>
            $"{Particular,-18} {Amount,10}  {InstNo}";
    }

    public class Employee
    {
        public string EmployeeCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string Designation { get; set; } = "";
        public string SalaryMonth { get; set; } = "";

        public string DateOfBirth { get; set; } = "";
        public string DateOfJoining { get; set; } = "";
        public string DateOfRetirement { get; set; } = "";

        public string UidNo { get; set; } = "";
        public string PayCommission { get; set; } = "";
        public string Level { get; set; } = "";

        public string GpfDcpsAccNo { get; set; } = "";
        public string PranNo { get; set; } = "";
        public string BankAccNo { get; set; } = "";
        public string IfscCode { get; set; } = "";
        public decimal? BasicPay { get; set; }

        public string MobileNo { get; set; } = "";
        public string PanNo { get; set; } = "";

        public decimal? NetPayAmount { get; set; }
        public string NetPayWords { get; set; } = "";

        public string BillNo { get; set; } = "";
        public string BillDescription { get; set; } = "";
        public decimal? GrossAmt { get; set; }
        public decimal? NetAmt { get; set; }
        public string VoucherNumber { get; set; } = "";
        public string VoucherDate { get; set; } = "";
        public string Office { get; set; } = "";
        public string DdoCode { get; set; } = "";

        public decimal? TotalEmolument { get; set; }
        public decimal? TotalGovtRecoveries { get; set; }
        public decimal? TotalNGRecoveries { get; set; }

        public List<SalaryLineItem> Emoluments { get; set; } = new();
        public List<SalaryLineItem> GovtRecoveries { get; set; } = new();
        public List<SalaryLineItem> NonGovtRecoveries { get; set; } = new();

        // Pulls the "I.Tax" (income tax) line out of Govt. Recoveries, if
        // present for this employee. Matched loosely (ignoring spaces/dots
        // and also accepting "Income Tax") since not every payslip spells
        // it identically, and most employees won't have this line at all
        // (only higher pay-band employees seem to have I.Tax deducted).
        public decimal ITaxAmount =>
            (GovtRecoveries ?? new List<SalaryLineItem>())
                .Where(i =>
                    (i.Particular ?? "").Replace(".", "").Replace(" ", "")
                        .Equals("ITax", StringComparison.OrdinalIgnoreCase)
                    || (i.Particular ?? "").IndexOf("Income Tax", StringComparison.OrdinalIgnoreCase) >= 0)
                .Sum(i => i.Amount ?? 0);

        public void Print()
        {
            Console.WriteLine(new string('=', 90));
            Console.WriteLine($"{Name}  ({EmployeeCode})  -  {Designation}  -  {SalaryMonth}");
            Console.WriteLine($"Office: {Office}   DDO Code: {DdoCode}");
            Console.WriteLine($"Bank A/c: {BankAccNo}  IFSC: {IfscCode}  Basic Pay: {BasicPay}");
            Console.WriteLine($"Net Pay: {NetPayAmount} ({NetPayWords})");
            Console.WriteLine();

            Console.WriteLine("-- Emoluments --");
            foreach (var i in Emoluments) Console.WriteLine("  " + i);
            Console.WriteLine($"  Total Emolument: {TotalEmolument}");

            Console.WriteLine("-- Govt. Recoveries --");
            foreach (var i in GovtRecoveries) Console.WriteLine("  " + i);
            Console.WriteLine($"  Total Govt. Recoveries: {TotalGovtRecoveries}");

            Console.WriteLine("-- Non-Govt. Recoveries --");
            foreach (var i in NonGovtRecoveries) Console.WriteLine("  " + i);
            Console.WriteLine($"  Total NG Recoveries: {TotalNGRecoveries}");
            Console.WriteLine();
        }
    }

    // A single "bill" run - one VoucherNumber/VoucherDate/BillNo covers many
    // employees (e.g. all "DCPS ... REGULAR BILL" staff paid together).
    public class VoucherGroup
    {
        public string VoucherNumber { get; set; } = "";
        public string VoucherDate { get; set; } = "";
        public string BillNo { get; set; } = "";
        public string BillDescription { get; set; } = "";
        public decimal? GrossAmt { get; set; }
        public decimal? NetAmt { get; set; }

        public string DdoCode { get; set; } = "";
        public string SalaryMonth { get; set; } = "";

        // Sum of every employee's I.Tax (income tax) deduction in this group.
        public decimal VoucherAmount { get; set; }

        public List<Employee> Employees { get; set; } = new();

        public void Print()
        {
            Console.WriteLine(new string('#', 90));
            Console.WriteLine($"Voucher No: {VoucherNumber}   Voucher Date: {VoucherDate}   Bill No: {BillNo}");
            Console.WriteLine($"DDO Code: {DdoCode}   Salary Month: {SalaryMonth}");
            Console.WriteLine($"Bill Description: {BillDescription}   Gross Amt: {GrossAmt}   Net Amt: {NetAmt}");
            Console.WriteLine($"Voucher Amount (Sum of I.Tax): {VoucherAmount}");
            Console.WriteLine($"Employees in this bill: {Employees.Count}");
            foreach (var e in Employees)
                Console.WriteLine($"  - {e.Name} ({e.EmployeeCode})  Designation: {e.Designation}  Net Pay: {e.NetPayAmount}  I.Tax: {e.ITaxAmount}");
            Console.WriteLine();
        }
    }

    // Internal helper: one visual "line" of text on the page, with its words
    // still individually addressable (so we can look at each word's X pos).
    internal class PdfLine
    {
        public double Y { get; set; }
        public List<Word> Words { get; set; } = new();
        public string Text => string.Join(" ", Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
    }
}
