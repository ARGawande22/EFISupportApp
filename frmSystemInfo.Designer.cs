namespace EFISupportApp
{
    partial class frmSystemInfo
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
            pnlMain = new Panel();
            lblUniqueId = new Label();
            txtPrcessorId = new TextBox();
            txtBIOSSerial = new TextBox();
            txtSerialNo = new TextBox();
            txtSystemId = new TextBox();
            label3 = new Label();
            label2 = new Label();
            label1 = new Label();
            lblSystemId = new Label();
            btnClose = new Button();
            txtComputerName = new TextBox();
            lblComputerName = new Label();
            pnlMain.SuspendLayout();
            SuspendLayout();
            // 
            // pnlMain
            // 
            pnlMain.BorderStyle = BorderStyle.Fixed3D;
            pnlMain.Controls.Add(txtComputerName);
            pnlMain.Controls.Add(lblComputerName);
            pnlMain.Controls.Add(lblUniqueId);
            pnlMain.Controls.Add(txtPrcessorId);
            pnlMain.Controls.Add(txtBIOSSerial);
            pnlMain.Controls.Add(txtSerialNo);
            pnlMain.Controls.Add(txtSystemId);
            pnlMain.Controls.Add(label3);
            pnlMain.Controls.Add(label2);
            pnlMain.Controls.Add(label1);
            pnlMain.Controls.Add(lblSystemId);
            pnlMain.Controls.Add(btnClose);
            pnlMain.Dock = DockStyle.Fill;
            pnlMain.Location = new Point(0, 0);
            pnlMain.Name = "pnlMain";
            pnlMain.Size = new Size(459, 300);
            pnlMain.TabIndex = 0;
            // 
            // lblUniqueId
            // 
            lblUniqueId.AutoSize = true;
            lblUniqueId.Location = new Point(10, 234);
            lblUniqueId.Name = "lblUniqueId";
            lblUniqueId.Size = new Size(78, 18);
            lblUniqueId.TabIndex = 51;
            lblUniqueId.Text = "Unique Id : ";
            // 
            // txtPrcessorId
            // 
            txtPrcessorId.Font = new Font("Calibri", 12F);
            txtPrcessorId.Location = new Point(135, 142);
            txtPrcessorId.Name = "txtPrcessorId";
            txtPrcessorId.Size = new Size(310, 27);
            txtPrcessorId.TabIndex = 50;
            // 
            // txtBIOSSerial
            // 
            txtBIOSSerial.Font = new Font("Calibri", 12F);
            txtBIOSSerial.Location = new Point(135, 109);
            txtBIOSSerial.Name = "txtBIOSSerial";
            txtBIOSSerial.Size = new Size(310, 27);
            txtBIOSSerial.TabIndex = 49;
            // 
            // txtSerialNo
            // 
            txtSerialNo.Font = new Font("Calibri", 12F);
            txtSerialNo.Location = new Point(135, 76);
            txtSerialNo.Name = "txtSerialNo";
            txtSerialNo.Size = new Size(310, 27);
            txtSerialNo.TabIndex = 48;
            // 
            // txtSystemId
            // 
            txtSystemId.Font = new Font("Calibri", 12F);
            txtSystemId.Location = new Point(135, 43);
            txtSystemId.Name = "txtSystemId";
            txtSystemId.Size = new Size(310, 27);
            txtSystemId.TabIndex = 47;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(10, 146);
            label3.Name = "label3";
            label3.Size = new Size(93, 18);
            label3.TabIndex = 4;
            label3.Text = "Processor Id : ";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(10, 113);
            label2.Name = "label2";
            label2.Size = new Size(85, 18);
            label2.TabIndex = 3;
            label2.Text = "BIOS Serial : ";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(10, 80);
            label1.Name = "label1";
            label1.Size = new Size(71, 18);
            label1.TabIndex = 2;
            label1.Text = "Serial No :";
            // 
            // lblSystemId
            // 
            lblSystemId.AutoSize = true;
            lblSystemId.Location = new Point(10, 43);
            lblSystemId.Name = "lblSystemId";
            lblSystemId.Size = new Size(78, 18);
            lblSystemId.TabIndex = 1;
            lblSystemId.Text = "System Id : ";
            // 
            // btnClose
            // 
            btnClose.BackColor = Color.WhiteSmoke;
            btnClose.ForeColor = SystemColors.ActiveCaptionText;
            btnClose.Image = Properties.Resources.Wrong;
            btnClose.Location = new Point(425, 3);
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(27, 27);
            btnClose.TabIndex = 0;
            btnClose.UseVisualStyleBackColor = false;
            btnClose.Click += btnClose_Click;
            // 
            // txtComputerName
            // 
            txtComputerName.Font = new Font("Calibri", 12F);
            txtComputerName.Location = new Point(135, 178);
            txtComputerName.Name = "txtComputerName";
            txtComputerName.Size = new Size(310, 27);
            txtComputerName.TabIndex = 53;
            // 
            // lblComputerName
            // 
            lblComputerName.AutoSize = true;
            lblComputerName.Location = new Point(10, 182);
            lblComputerName.Name = "lblComputerName";
            lblComputerName.Size = new Size(120, 18);
            lblComputerName.TabIndex = 52;
            lblComputerName.Text = "Computer Name : ";
            // 
            // frmSystemInfo
            // 
            AutoScaleDimensions = new SizeF(8F, 18F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(459, 300);
            Controls.Add(pnlMain);
            Font = new Font("Calibri", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            FormBorderStyle = FormBorderStyle.None;
            Margin = new Padding(3, 4, 3, 4);
            Name = "frmSystemInfo";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "System Info";
            Load += frmSystemInfo_Load;
            pnlMain.ResumeLayout(false);
            pnlMain.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel pnlMain;
        private Button btnClose;
        private Label label3;
        private Label label2;
        private Label label1;
        private Label lblSystemId;
        private TextBox txtBIOSSerial;
        private TextBox txtSerialNo;
        private TextBox txtSystemId;
        private TextBox txtPrcessorId;
        private Label lblUniqueId;
        private TextBox txtComputerName;
        private Label lblComputerName;
    }
}