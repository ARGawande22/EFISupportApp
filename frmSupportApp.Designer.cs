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
            txtValue = new TextBox();
            btnEncrypt = new Button();
            btnDecrypt = new Button();
            txtEncrypt = new TextBox();
            txtDecrypt = new TextBox();
            SuspendLayout();
            // 
            // txtValue
            // 
            txtValue.Font = new Font("Calibri", 12F);
            txtValue.Location = new Point(27, 70);
            txtValue.Name = "txtValue";
            txtValue.Size = new Size(261, 27);
            txtValue.TabIndex = 46;
            // 
            // btnEncrypt
            // 
            btnEncrypt.Anchor = AnchorStyles.Right;
            btnEncrypt.BackColor = Color.WhiteSmoke;
            btnEncrypt.Font = new Font("Calibri", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnEncrypt.ForeColor = SystemColors.ActiveCaptionText;
            btnEncrypt.ImageAlign = ContentAlignment.MiddleLeft;
            btnEncrypt.Location = new Point(333, 32);
            btnEncrypt.Name = "btnEncrypt";
            btnEncrypt.Size = new Size(82, 32);
            btnEncrypt.TabIndex = 47;
            btnEncrypt.Text = "Encrypt";
            btnEncrypt.UseVisualStyleBackColor = false;
            btnEncrypt.Click += btnEncrypt_Click;
            // 
            // btnDecrypt
            // 
            btnDecrypt.Anchor = AnchorStyles.Right;
            btnDecrypt.BackColor = Color.WhiteSmoke;
            btnDecrypt.Font = new Font("Calibri", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnDecrypt.ForeColor = SystemColors.ActiveCaptionText;
            btnDecrypt.ImageAlign = ContentAlignment.MiddleLeft;
            btnDecrypt.Location = new Point(333, 98);
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
            txtEncrypt.Location = new Point(445, 35);
            txtEncrypt.Name = "txtEncrypt";
            txtEncrypt.Size = new Size(332, 27);
            txtEncrypt.TabIndex = 49;
            // 
            // txtDecrypt
            // 
            txtDecrypt.Font = new Font("Calibri", 12F);
            txtDecrypt.Location = new Point(445, 101);
            txtDecrypt.Name = "txtDecrypt";
            txtDecrypt.Size = new Size(332, 27);
            txtDecrypt.TabIndex = 50;
            // 
            // frmSupportApp
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(800, 450);
            Controls.Add(txtDecrypt);
            Controls.Add(txtEncrypt);
            Controls.Add(btnDecrypt);
            Controls.Add(btnEncrypt);
            Controls.Add(txtValue);
            Font = new Font("Calibri", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "frmSupportApp";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EFI Support App";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtValue;
        private Button btnEncrypt;
        private Button btnDecrypt;
        private TextBox txtEncrypt;
        private TextBox txtDecrypt;
    }
}