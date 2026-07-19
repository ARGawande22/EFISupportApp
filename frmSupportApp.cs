using EFISupportApp.Models.BankStatement;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UglyToad.PdfPig.Graphics;

namespace EFISupportApp
{
    public partial class frmSupportApp : Form
    {
        Encrypt_Decrypt _encrypt_Decrypt;
        public string _encryptDecryptVal = string.Empty;

        public frmSupportApp()
        {
            InitializeComponent();
            _encrypt_Decrypt = new Encrypt_Decrypt();
        }

        private void btnEncrypt_Click(object sender, EventArgs e)
        {
            _encryptDecryptVal = string.Empty;
            _encrypt_Decrypt.EncryptDecrypt("Encrypt", txtValue.Text, out _encryptDecryptVal);
            txtEncrypt.Text = _encryptDecryptVal;
        }

        private void btnDecrypt_Click(object sender, EventArgs e)
        {
            _encryptDecryptVal = string.Empty;
            _encrypt_Decrypt.EncryptDecrypt("Decrypt", txtValue.Text, out _encryptDecryptVal);
            txtDecrypt.Text = _encryptDecryptVal;
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog fileDialog = new OpenFileDialog())
            {
                fileDialog.Filter = "PDF Files (*.pdf)|*.pdf";
                fileDialog.Title = "Select a PDF file";
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = fileDialog.FileName;
                    pnlFooter.Enabled = true;
                }
            }
        }

        private void btnPaySlip_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay slip PDF...!");
                return;
            }

            var employees = ReadPaySlipPDF.Parse(txtPath.Text);
            Console.WriteLine($"Parsed {employees.Count} employee pay slips.\n");

            //foreach (var emp in employees)
            //    emp.Print();

            var groups = ReadPaySlipPDF.GroupByVoucher(employees);
            Console.WriteLine($"\nGrouped into {groups.Count} voucher/bill runs.\n");

            //foreach (var group in groups)
            //    group.Print();
        }

        private void btnPayBill_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay bill PDF...!");
                return;
            }

            var ds = new ReadPayBillPDF().ExtractFromPdf(txtPath.Text);
        }

        private void btnNPS14_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay bill 14% NPS PDF...!");
                return;
            }

            //DataSet ds = ReadPayBillNPS14PerScheduledPDF.ExtractFromPdf(txtPath.Text);
            DataSet ds = ReadPayBillNPS14PerScheduledPDFv1.ExtractFromPdf(txtPath.Text);
        }

        private void btnNGRec_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay bill NG-Recoveries PDF...!");
                return;
            }

            DataSet ds = ReadPayBillNGRecoveriesPDF.ExtractFromPdf(txtPath.Text);
        }


        private void btnPaybillOuter_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay bill Outer PDF...!");
                return;
            }

            DataSet ds = ReadPayBillOuterPDF.ExtractFromPdf(txtPath.Text);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select pay bill Bank statement PDF...!");
                return;
            }

            ParsedStatement _parsedStatement = ReadPayBillBankStatement.ExtractFromPdf(txtPath.Text);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtPath.Clear();
            pnlFooter.Enabled = false;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        
    }
}
