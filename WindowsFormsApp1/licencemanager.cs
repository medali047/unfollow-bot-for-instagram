using System;
using System.IO;
using System.Windows.Forms;

namespace InstagramUnfollowBot
{
    public class SimpleLicenseManager
    {
        private readonly string _licenseFolder;
        private LicenseInfo _currentLicense;

        public SimpleLicenseManager()
        {
            _licenseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Licenses");
            Directory.CreateDirectory(_licenseFolder);
        }

        public class LicenseInfo
        {
            public string LicenseKey { get; set; }
            public string PlanType { get; set; }
            public int MaxUnfollows { get; set; }
            public DateTime ExpiryDate { get; set; }
            public string CustomerName { get; set; }
            public bool IsValid { get; set; }
        }

        public bool ActivateLicense(string licenseKey, string customerName)
        {
            try
            {
                if (!IsValidLicensePattern(licenseKey))
                {
                    MessageBox.Show("Invalid license key format.", "Activation Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                var licenseInfo = DecodeLicense(licenseKey, customerName);

                if (licenseInfo != null && licenseInfo.IsValid)
                {
                    _currentLicense = licenseInfo;
                    SaveLicense(licenseInfo);

                    MessageBox.Show($"✅ License activated!\nPlan: {licenseInfo.PlanType}\nExpires: {licenseInfo.ExpiryDate:yyyy-MM-dd}",
                        "Activation Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }
                else
                {
                    MessageBox.Show("Invalid or expired license key.", "Activation Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Activation error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool CheckLicense()
        {
            try
            {
                var licenseFile = Path.Combine(_licenseFolder, "active.lic");
                if (!File.Exists(licenseFile))
                    return false;

                var licenseKey = File.ReadAllText(licenseFile).Trim();
                var licenseInfo = DecodeLicense(licenseKey, "Current User");

                if (licenseInfo == null || !licenseInfo.IsValid)
                    return false;

                if (DateTime.Now > licenseInfo.ExpiryDate)
                {
                    MessageBox.Show("Your license has expired. Please contact support.",
                        "License Expired", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                _currentLicense = licenseInfo;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool CanPerformUnfollow()
        {
            return _currentLicense != null && _currentLicense.IsValid;
        }

        public LicenseInfo GetLicenseInfo()
        {
            return _currentLicense;
        }

        public void DeactivateLicense()
        {
            try
            {
                var licenseFile = Path.Combine(_licenseFolder, "active.lic");
                if (File.Exists(licenseFile))
                    File.Delete(licenseFile);

                _currentLicense = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Deactivation error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool IsValidLicensePattern(string licenseKey)
        {
            return !string.IsNullOrEmpty(licenseKey) &&
                   licenseKey.Length >= 10 &&
                   licenseKey.Contains("-");
        }

        private LicenseInfo DecodeLicense(string licenseKey, string customerName)
        {
            try
            {
                string planType = "STARTER";
                int maxUnfollows = 1000;
                int monthsValid = 1;

                if (licenseKey.StartsWith("STARTER-"))
                {
                    planType = "STARTER";
                    maxUnfollows = 1000;
                    monthsValid = 1;
                }
                else if (licenseKey.StartsWith("PRO-"))
                {
                    planType = "PRO";
                    maxUnfollows = 10000;
                    monthsValid = 1;
                }
                else if (licenseKey.StartsWith("AGENCY-"))
                {
                    planType = "AGENCY";
                    maxUnfollows = 999999;
                    monthsValid = 1;
                }
                else if (licenseKey.StartsWith("TEST-"))
                {
                    planType = "TEST";
                    maxUnfollows = 50;
                    monthsValid = 12;
                }
                else
                {
                    return null;
                }

                return new LicenseInfo
                {
                    LicenseKey = licenseKey,
                    PlanType = planType,
                    MaxUnfollows = maxUnfollows,
                    ExpiryDate = DateTime.Now.AddMonths(monthsValid),
                    CustomerName = customerName,
                    IsValid = true
                };
            }
            catch
            {
                return null;
            }
        }

        private void SaveLicense(LicenseInfo licenseInfo)
        {
            var licenseFile = Path.Combine(_licenseFolder, "active.lic");
            File.WriteAllText(licenseFile, licenseInfo.LicenseKey);
        }
    }
}