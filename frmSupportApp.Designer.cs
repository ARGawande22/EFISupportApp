namespace EFISupportApp
{
    partial class frmSupportApp
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmSupportApp));
            txtValue = new TextBox();
            btnEncrypt = new Button();
            btnDecrypt = new Button();
            txtEncrypt = new TextBox();
            txtDecrypt = new TextBox();
            grpReadPDF = new GroupBox();
            pnlFooter = new Panel();
            btnNGRec = new Button();
            btnNPS14 = new Button();
            btnPayBill = new Button();
            btnPaySlip = new Button();
            btnClear = new Button();
            btnBrowse = new Button();
            lblImport = new Label();
            txtPath = new TextBox();
            btnCancel = new Button();
            groupBox1 = new GroupBox();
            grpReadPDF.SuspendLayout();
            pnlFooter.SuspendLayout();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // txtValue
            // 
            txtValue.Font = new Font("Calibri", 12F);
            txtValue.Location = new Point(15, 61);
            txtValue.Name = "txtValue";
            txtValue.Size = new Size(261, 27);
            txtValue.TabIndex = 46;
            // 
            // btnEncrypt
            // 
            btnEncrypt.BackColor = Color.WhiteSmoke;
            btnEncrypt.Font = new Font("Calibri", 9.75F, FontStyle.Bold | FontStyle.Italic);
            btnEncrypt.ForeColor = SystemColors.ActiveCaptionText;
            btnEncrypt.ImageAlign = ContentAlignment.MiddleLeft;
            btnEncrypt.Location = new Point(309, 23);
            btnEncrypt.Name = "btnEncrypt";
            btnEncrypt.Size = new Size(82, 32);
            btnEncrypt.TabIndex = 47;
            btnEncrypt.Text = "Encrypt";
            btnEncrypt.UseVisualStyleBackColor = false;
            btnEncrypt.Click += btnEncrypt_Click;
            // 
            // btnDecrypt
            // 
            btnDecrypt.BackColor = Color.WhiteSmoke;
            btnDecrypt.Font = new Font("Calibri", 9.75F, FontStyle.Bold | FontStyle.Italic);
            btnDecrypt.ForeColor = SystemColors.ActiveCaptionText;
            btnDecrypt.ImageAlign = ContentAlignment.MiddleLeft;
            btnDecrypt.Location = new Point(309, 89);
            btnDecrypt.Name = "btnDecrypt";
            btnDecrypt.Size = new Size(82, 32);
            btnDecrypt.TabIndex = 48;
            btnDecrypt.Text = "Decrypt";
            btnDecrypt.UseVisualStyleBackColor = false;
            btnDecrypt.Click += btnDecrypt_Click;
            // 
            // txtEncrypt
            // 
            txtEncrypt.Font = new Font("Calibri", 12F);
            txtEncrypt.Location = new Point(414, 26);
            txtEncrypt.Name = "txtEncrypt";
            txtEncrypt.Size = new Size(332, 27);
            txtEncrypt.TabIndex = 49;
            // 
            // txtDecrypt
            // 
            txtDecrypt.Font = new Font("Calibri", 12F);
            txtDecrypt.Location = new Point(414, 92);
            txtDecrypt.Name = "txtDecrypt";
            txtDecrypt.Size = new Size(332, 27);
            txtDecrypt.TabIndex = 50;
            // 
            // grpReadPDF
            // 
            grpReadPDF.Controls.Add(pnlFooter);
            grpReadPDF.Controls.Add(btnClear);
            grpReadPDF.Controls.Add(btnBrowse);
            grpReadPDF.Controls.Add(lblImport);
            grpReadPDF.Controls.Add(txtPath);
            grpReadPDF.Font = new Font("Calibri", 9.75F, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point, 0);
            grpReadPDF.Location = new Point(12, 154);
            grpReadPDF.Name = "grpReadPDF";
            grpReadPDF.Size = new Size(765, 194);
            grpReadPDF.TabIndex = 51;
            grpReadPDF.TabStop = false;
            grpReadPDF.Text = "Read New Sevaarth PDF's";
            // 
            // pnlFooter
            // 
            pnlFooter.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlFooter.Controls.Add(btnNGRec);
            pnlFooter.Controls.Add(btnNPS14);
            pnlFooter.Controls.Add(btnPayBill);
            pnlFooter.Controls.Add(btnPaySlip);
            pnlFooter.Location = new Point(0, 133);
            pnlFooter.Name = "pnlFooter";
            pnlFooter.Size = new Size(765, 40);
            pnlFooter.TabIndex = 78;
            // 
            // btnNGRec
            // 
            btnNGRec.BackColor = Color.WhiteSmoke;
            btnNGRec.ForeColor = SystemColors.ActiveCaptionText;
            btnNGRec.ImageAlign = ContentAlignment.MiddleLeft;
            btnNGRec.Location = new Point(550, 5);
            btnNGRec.Name = "btnNGRec";
            btnNGRec.Size = new Size(104, 30);
            btnNGRec.TabIndex = 15;
            btnNGRec.Text = "Non Gov Rec.";
            btnNGRec.UseVisualStyleBackColor = false;
            btnNGRec.Click += btnNGRec_Click;
            // 
            // btnNPS14
            // 
            btnNPS14.BackColor = Color.WhiteSmoke;
            btnNPS14.ForeColor = SystemColors.ActiveCaptionText;
            btnNPS14.ImageAlign = ContentAlignment.MiddleLeft;
            btnNPS14.Location = new Point(401, 5);
            btnNPS14.Name = "btnNPS14";
            btnNPS14.Size = new Size(73, 30);
            btnNPS14.TabIndex = 14;
            btnNPS14.Text = "NPS 14%";
            btnNPS14.UseVisualStyleBackColor = false;
            btnNPS14.Click += btnNPS14_Click;
            // 
            // btnPayBill
            // 
            btnPayBill.BackColor = Color.WhiteSmoke;
            btnPayBill.ForeColor = SystemColors.ActiveCaptionText;
            btnPayBill.ImageAlign = ContentAlignment.MiddleLeft;
            btnPayBill.Location = new Point(239, 5);
            btnPayBill.Name = "btnPayBill";
            btnPayBill.Size = new Size(73, 30);
            btnPayBill.TabIndex = 13;
            btnPayBill.Text = "Pay Bill";
            btnPayBill.UseVisualStyleBackColor = false;
            btnPayBill.Click += btnPayBill_Click;
            // 
            // btnPaySlip
            // 
            btnPaySlip.BackColor = Color.WhiteSmoke;
            btnPaySlip.ForeColor = SystemColors.ActiveCaptionText;
            btnPaySlip.ImageAlign = ContentAlignment.MiddleLeft;
            btnPaySlip.Location = new Point(83, 5);
            btnPaySlip.Name = "btnPaySlip";
            btnPaySlip.Size = new Size(73, 30);
            btnPaySlip.TabIndex = 12;
            btnPaySlip.Text = "Pay Slip";
            btnPaySlip.UseVisualStyleBackColor = false;
            btnPaySlip.Click += btnPaySlip_Click;
            // 
            // btnClear
            // 
            btnClear.BackColor = Color.WhiteSmoke;
            btnClear.ForeColor = SystemColors.ActiveCaptionText;
            btnClear.ImageAlign = ContentAlignment.MiddleLeft;
            btnClear.Location = new Point(702, 73);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(57, 30);
            btnClear.TabIndex = 77;
            btnClear.Text = "Clear";
            btnClear.UseVisualStyleBackColor = false;
            btnClear.Click += btnClear_Click;
            // 
            // btnBrowse
            // 
            btnBrowse.BackColor = Color.WhiteSmoke;
            btnBrowse.ForeColor = SystemColors.ActiveCaptionText;
            btnBrowse.ImageAlign = ContentAlignment.MiddleLeft;
            btnBrowse.Location = new Point(605, 73);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new Size(90, 30);
            btnBrowse.TabIndex = 54;
            btnBrowse.Text = "Browse....";
            btnBrowse.UseVisualStyleBackColor = false;
            btnBrowse.Click += btnBrowse_Click;
            // 
            // lblImport
            // 
            lblImport.AutoSize = true;
            lblImport.Font = new Font("Calibri", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblImport.ForeColor = SystemColors.ControlText;
            lblImport.Location = new Point(15, 43);
            lblImport.Name = "lblImport";
            lblImport.Size = new Size(85, 19);
            lblImport.TabIndex = 52;
            lblImport.Text = "Select PDF :";
            // 
            // txtPath
            // 
            txtPath.Enabled = false;
            txtPath.Font = new Font("Calibri", 12F);
            txtPath.Location = new Point(106, 40);
            txtPath.Name = "txtPath";
            txtPath.Size = new Size(653, 27);
            txtPath.TabIndex = 53;
            // 
            // btnCancel
            // 
            btnCancel.BackColor = Color.WhiteSmoke;
            btnCancel.ForeColor = SystemColors.ActiveCaptionText;
            btnCancel.ImageAlign = ContentAlignment.MiddleLeft;
            btnCancel.Location = new Point(715, 383);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(62, 30);
            btnCancel.TabIndex = 52;
            btnCancel.Text = "Close";
            btnCancel.UseVisualStyleBackColor = false;
            btnCancel.Click += btnCancel_Click;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(txtValue);
            groupBox1.Controls.Add(btnEncrypt);
            groupBox1.Controls.Add(txtDecrypt);
            groupBox1.Controls.Add(btnDecrypt);
            groupBox1.Controls.Add(txtEncrypt);
            groupBox1.Font = new Font("Calibri", 9.75F, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point, 0);
            groupBox1.Location = new Point(12, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(765, 127);
            groupBox1.TabIndex = 53;
            groupBox1.TabStop = false;
            groupBox1.Text = "Encryption & Descryption";
            // 
            // frmSupportApp
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(800, 425);
            Controls.Add(groupBox1);
            Controls.Add(btnCancel);
            Controls.Add(grpReadPDF);
            Font = new Font("Calibri", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "frmSupportApp";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EFI Support App";
            grpReadPDF.ResumeLayout(false);
            grpReadPDF.PerformLayout();
            pnlFooter.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TextBox txtValue;
        private Button btnEncrypt;
        private Button btnDecrypt;
        private TextBox txtEncrypt;
        private TextBox txtDecrypt;
        private GroupBox grpReadPDF;
        private Button btnBrowse;
        private Label lblImport;
        private TextBox txtPath;
        private Button btnClear;
        private Panel pnlFooter;
        private Button btnPaySlip;
        private Button btnCancel;
        private Button btnPayBill;
        private Button btnNGRec;
        private Button btnNPS14;
        private GroupBox groupBox1;
    }
}