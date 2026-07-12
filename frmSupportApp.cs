using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
    }
}
