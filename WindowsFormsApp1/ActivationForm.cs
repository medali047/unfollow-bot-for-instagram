using System;
using System.Windows.Forms;
using System.Xml.Linq;

namespace InstagramUnfollowBot
{
    public partial class ActivationForm : Form
    {
        private readonly SimpleLicenseManager _licenseManager;

        public ActivationForm(SimpleLicenseManager licenseManager)
        {
            _licenseManager = licenseManager;
            InitializeComponent();
        }

        public ActivationForm()
        {
            InitializeComponent();
        }

        private void btnActivate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtName.Text) || string.IsNullOrEmpty(txtLicense.Text))
            {
                MessageBox.Show("Please enter both name and license key.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_licenseManager.ActivateLicense(txtLicense.Text.Trim(), txtName.Text.Trim()))
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}