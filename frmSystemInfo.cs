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
    public partial class frmSystemInfo : Form
    {
        public frmSystemInfo()
        {
            InitializeComponent();
        }

        private void frmSystemInfo_Load(object sender, EventArgs e)
        {
            #region Manual Checks
            //# System UUID
            //Get - WmiObject Win32_ComputerSystemProduct | Select - Object UUID

            //# Motherboard Serial
            //Get - WmiObject Win32_BaseBoard | Select - Object SerialNumber

            //# BIOS Serial
            //Get - WmiObject Win32_BIOS | Select - Object SerialNumber

            //# Processor ID
            //Get - WmiObject Win32_Processor | Select - Object ProcessorId
            #endregion

            //string Details = GetSystemDetails.GenerateUniqueId();

            txtSystemId.Text= GetSystemDetails.GetSystemUUID();
            txtSerialNo.Text= GetSystemDetails.GetMotherboardSerial();
            txtBIOSSerial.Text= GetSystemDetails.GetBiosSerial();
            txtPrcessorId.Text= GetSystemDetails.GetProcessorId();
            txtComputerName.Text = GetSystemDetails.GetGetMachineName();
            //txtComputerName.Text = Environment.MachineName;
            //txtComputerName.Text = Environment.GetEnvironmentVariable("COMPUTERNAME");
            lblUniqueId.Text= GetSystemDetails.GenerateUniqueId();

        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        
    }
}
