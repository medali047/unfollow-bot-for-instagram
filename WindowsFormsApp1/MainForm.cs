using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace InstagramUnfollowBot
{

    public partial class MainForm : Form
    {
        private SimpleLicenseManager _licenseManager;
        private InstagramBot _bot;
        private List<string> _usersToUnfollow;
        private string _selectedFilePath;
        private SessionManager _sessionManager;

        public MainForm()
        {
            // Initialize license manager first
            _licenseManager = new SimpleLicenseManager();

            // Check if license is valid
            if (!_licenseManager.CheckLicense())
            {
                // Show activation form
                var activationForm = new ActivationForm(_licenseManager);
                var result = activationForm.ShowDialog();

                if (result != DialogResult.OK)
                {
                    // User cancelled activation, close application
                    MessageBox.Show("License activation required to use the application.",
                        "License Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                    return;
                }
            }
            // License is valid, continue with normal initialization
            InitializeComponent();
            InitializeCustomComponents();

            // Add license menu
            AddLicenseMenu();
            UpdateLicenseStatus();
        }
        private void AddLicenseMenu()
        {
            // Create license menu
            var licenseMenu = new ToolStripMenuItem("License");

            var activateItem = new ToolStripMenuItem("Activate License");
            var deactivateItem = new ToolStripMenuItem("Deactivate License");
            var infoItem = new ToolStripMenuItem("License Info");

            activateItem.Click += (s, e) =>
            {
                var activationForm = new ActivationForm(_licenseManager);
                activationForm.ShowDialog();
                UpdateLicenseStatus();
            };

            deactivateItem.Click += (s, e) =>
            {
                if (MessageBox.Show("Are you sure you want to deactivate the current license?", "Confirm Deactivation",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _licenseManager.DeactivateLicense();
                    MessageBox.Show("License deactivated. Application will now restart.", "Info",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Restart();
                }
            };

            infoItem.Click += (s, e) =>
            {
                var license = _licenseManager.GetLicenseInfo();
                if (license != null)
                {
                    MessageBox.Show(
                        $"Plan: {license.PlanType}\n" +
                        $"Customer: {license.CustomerName}\n" +
                        $"Expires: {license.ExpiryDate:yyyy-MM-dd}\n" +
                        $"Key: {license.LicenseKey}",
                        "License Information",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("No active license found.", "License Info",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            licenseMenu.DropDownItems.AddRange(new[] { activateItem, deactivateItem, infoItem });

            // Add to existing menu strip - check if you have one first
            if (this.Controls.OfType<MenuStrip>().Any())
            {
                var menuStrip = this.Controls.OfType<MenuStrip>().First();
                menuStrip.Items.Add(licenseMenu);
            }
            else
            {
                // Create a new menu strip if one doesn't exist
                var menuStrip = new MenuStrip();
                menuStrip.Items.Add(licenseMenu);
                this.Controls.Add(menuStrip);
                this.MainMenuStrip = menuStrip;
            }
        }

        private void UpdateLicenseStatus()
        {
            var license = _licenseManager.GetLicenseInfo();
            if (license != null)
            {
                // Update status label if you have one
                if (lblStatus != null) // Use your actual status label name
                {
                    lblStatus.Text = $"Licensed: {license.PlanType} | Expires: {license.ExpiryDate:MMM dd}";
                }

                // Update button states based on license
                UpdateControlStates(_bot?.IsRunning ?? false);
            }
        }
        private void InitializeCustomComponents()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;

            _bot = new InstagramBot();
            _sessionManager = new SessionManager();

            _bot.LogMessage += OnLogMessage;
            _bot.StatisticsUpdated += OnStatisticsUpdated;
            _bot.ProgressUpdated += OnProgressUpdated;
            _bot.StatusUpdated += OnStatusUpdated;
            _bot.RunningStateChanged += OnRunningStateChanged;

            UpdateStatistics(0, 0, 0, 0);
            UpdateStatus("Ready", StatusType.Ready);

            // Initialize session controls after form is loaded
            this.Load += MainForm_Load;

            UpdateControlStates(false);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Load saved accounts after form is fully loaded
            LoadSavedAccounts();
        }

        private void LoadSavedAccounts()
        {
            try
            {
                cmbSavedAccounts.Items.Clear();
                var sessions = _bot.GetSavedSessions();

                // FILTER: Only show sessions that have actual files
                var availableSessions = new List<string>();
                foreach (var session in sessions)
                {
                    // Check if the session file actually exists
                    if (_sessionManager.SessionFileExists(session.Username))
                    {
                        cmbSavedAccounts.Items.Add($"{session.Username} - {session.LastLogin:g}");
                        availableSessions.Add(session.Username);
                    }
                }

                if (cmbSavedAccounts.Items.Count > 0)
                {
                    cmbSavedAccounts.SelectedIndex = 0;
                    AddLog($"Loaded {availableSessions.Count} available sessions", LogType.Info);

                    btnLoadSession.Enabled = true;
                    btnDeleteSession.Enabled = true;
                }
                else
                {
                    AddLog("No available sessions found", LogType.Info);
                    btnLoadSession.Enabled = false;
                    btnDeleteSession.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error loading saved accounts: {ex.Message}", LogType.Error);
            }
        }
        private void OnLogMessage(string message, LogType type)
        {
            AddLog(message, type);
        }

        private void OnStatisticsUpdated(int total, int success, int failed, int remaining)
        {
            UpdateStatistics(total, success, failed, remaining);
        }

        private void OnProgressUpdated(int value, int maximum)
        {
            UpdateProgress(value, maximum);
        }

        private void OnStatusUpdated(string status, StatusType type)
        {
            UpdateStatus(status, type);
        }

        private void OnRunningStateChanged(bool isRunning)
        {
            UpdateControlStates(isRunning);
        }

        public void UpdateStatistics(int total, int success, int failed, int remaining)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int, int, int, int>(UpdateStatistics), total, success, failed, remaining);
                return;
            }

            lblTotalUsers.Text = total.ToString();
            lblUnfollowed.Text = success.ToString();
            lblFailed.Text = failed.ToString();
            lblRemaining.Text = remaining.ToString();

            if (total > 0)
            {
                double successRate = (double)success / total * 100;
                lblSuccessRate.Text = $"{successRate:F1}%";
            }
            else
            {
                lblSuccessRate.Text = "0%";
            }
        }

        public void UpdateProgress(int value, int maximum = 100)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int, int>(UpdateProgress), value, maximum);
                return;
            }

            progressBar.Maximum = maximum;
            progressBar.Value = value;
            lblProgress.Text = $"{value}/{maximum}";
        }

        public void UpdateStatus(string status, StatusType type = StatusType.Ready)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, StatusType>(UpdateStatus), status, type);
                return;
            }

            lblStatus.Text = status;

            switch (type)
            {
                case StatusType.Ready:
                    lblStatus.ForeColor = Color.FromArgb(76, 175, 80);
                    break;
                case StatusType.Processing:
                    lblStatus.ForeColor = Color.FromArgb(33, 150, 243);
                    break;
                case StatusType.Warning:
                    lblStatus.ForeColor = Color.FromArgb(255, 152, 0);
                    break;
                case StatusType.Error:
                    lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
                    break;
                case StatusType.Paused:
                    lblStatus.ForeColor = Color.FromArgb(255, 193, 7);
                    break;
            }
        }

        public void AddLog(string message, LogType type = LogType.Info)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, LogType>(AddLog), message, type);
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logMessage = $"[{timestamp}] {message}";

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;

            switch (type)
            {
                case LogType.Success:
                    txtLog.SelectionColor = Color.FromArgb(76, 175, 80);
                    break;
                case LogType.Error:
                    txtLog.SelectionColor = Color.FromArgb(244, 67, 54);
                    break;
                case LogType.Warning:
                    txtLog.SelectionColor = Color.FromArgb(255, 152, 0);
                    break;
                default:
                    txtLog.SelectionColor = Color.FromArgb(33, 150, 243);
                    break;
            }

            txtLog.AppendText(logMessage + Environment.NewLine);
            txtLog.ScrollToCaret();
        }

        private void UpdateControlStates(bool isRunning)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(UpdateControlStates), isRunning);
                return;
            }

            bool isLoggedIn = _bot.IsLoggedIn();
            bool hasUsersFile = _usersToUnfollow?.Count > 0;

            btnStart.Enabled = !isRunning && isLoggedIn && hasUsersFile;
            btnPause.Enabled = isRunning;
            btnStop.Enabled = isRunning;
            btnSelectFile.Enabled = !isRunning;
            btnReload.Enabled = !isRunning && !string.IsNullOrEmpty(_selectedFilePath);

            // Session controls
            btnLoadSession.Enabled = !isRunning && cmbSavedAccounts.Items.Count > 0;
            btnDeleteSession.Enabled = !isRunning && cmbSavedAccounts.Items.Count > 0;

            // Login controls
            btnLogin.Enabled = !isRunning && !isLoggedIn;
            txtUsername.Enabled = !isRunning && !isLoggedIn;
            txtPassword.Enabled = !isRunning && !isLoggedIn;

            if (isRunning && _bot.IsPaused)
            {
                btnPause.Text = "RESUME";
            }
            else
            {
                btnPause.Text = "PAUSE";
            }

            if (isLoggedIn)
            {
                btnLogin.Text = "LOGGED IN";
                btnLogin.BackColor = Color.FromArgb(76, 175, 80);
                btnLogin.Enabled = false;
                txtUsername.Enabled = false;
                txtPassword.Enabled = false;
            }
            else
            {
                btnLogin.Text = "LOGIN";
                btnLogin.BackColor = Color.FromArgb(33, 150, 243);
                btnLogin.Enabled = !isRunning;
                txtUsername.Enabled = !isRunning;
                txtPassword.Enabled = !isRunning;
            }
        }

        // Event Handlers
        private async void btnLogin_Click(object sender, EventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                AddLog("Please enter both username and password", LogType.Error);
                return;
            }

            AddLog("Starting login process...", LogType.Info);
            btnLogin.Enabled = false;
            btnLogin.Text = "LOGGING IN...";

            try
            {
                bool loginSuccess = await _bot.LoginOnly(username, password);

                if (loginSuccess)
                {
                    AddLog("Login process started. Please complete any verification in browser.", LogType.Success);
                    AddLog("After completing login in browser, click 'Check Login Status'", LogType.Info);

                    btnLogin.Text = "CHECK STATUS";
                    btnLogin.BackColor = Color.FromArgb(255, 152, 0);
                    btnLogin.Enabled = true;

                    btnLogin.Click -= btnLogin_Click;
                    btnLogin.Click += CheckLoginStatus;
                }
                else
                {
                    AddLog("Login failed. Please check credentials and try again.", LogType.Error);
                    ResetLoginButton();
                }
            }
            catch (Exception ex)
            {
                AddLog($"Login error: {ex.Message}", LogType.Error);
                ResetLoginButton();
            }
        }

        private void CheckLoginStatus(object sender, EventArgs e)
        {
            AddLog("Checking login status...", LogType.Info);

            bool isLoggedIn = _bot.IsLoggedIn();

            if (isLoggedIn)
            {
                AddLog("✓ Login successful! Start button enabled.", LogType.Success);

                btnLogin.Text = "LOGGED IN";
                btnLogin.BackColor = Color.FromArgb(76, 175, 80);
                btnLogin.Enabled = false;

                btnLogin.Click -= CheckLoginStatus;
                btnLogin.Click += btnLogin_Click;

                UpdateControlStates(_bot.IsRunning);
                LoadSavedAccounts();

                if (_usersToUnfollow?.Count > 0)
                {
                    btnStart.Enabled = true;
                    AddLog("✓ Start button is now enabled and ready!", LogType.Success);
                }
            }
            else
            {
                AddLog("✗ Not logged in yet. Please complete login in browser and try again.", LogType.Warning);
                btnLogin.Text = "CHECK STATUS";
                btnLogin.BackColor = Color.FromArgb(255, 152, 0);
            }
        }

        private async void btnLoadSession_Click(object sender, EventArgs e)
        {
            if (cmbSavedAccounts.SelectedItem == null)
            {
                AddLog("Please select a saved account", LogType.Warning);
                return;
            }

            var selectedText = cmbSavedAccounts.SelectedItem.ToString();
            var username = selectedText.Split('-')[0].Trim();

            AddLog($"Loading session for {username}...", LogType.Info);

            // Disable buttons during loading
            btnLoadSession.Enabled = false;
            btnDeleteSession.Enabled = false;
            btnLogin.Enabled = false;
            btnLoadSession.Text = "LOADING...";

            try
            {
                bool success = await _bot.LoginWithCookies(username);

                if (success)
                {
                    AddLog($"✅ Session loaded successfully for {username}", LogType.Success);
                    txtUsername.Text = username;

                    // Update UI to show logged in state
                    UpdateControlStates(false);

                    if (_usersToUnfollow?.Count > 0)
                    {
                        btnStart.Enabled = true;
                        AddLog("✓ Start button enabled - ready to go!", LogType.Success);
                    }
                }
                else
                {
                    AddLog($"❌ Failed to load session for {username}", LogType.Error);
                    AddLog("Session may be expired. Please login with username/password", LogType.Warning);
                    LoadSavedAccounts(); // Refresh the list in case session was deleted
                }
            }
            catch (Exception ex)
            {
                AddLog($"Session load error: {ex.Message}", LogType.Error);
            }
            finally
            {
                // Re-enable buttons
                btnLoadSession.Enabled = cmbSavedAccounts.Items.Count > 0;
                btnDeleteSession.Enabled = cmbSavedAccounts.Items.Count > 0;
                btnLogin.Enabled = true;
                btnLoadSession.Text = "Load Session";
            }
        }

        private void btnDeleteSession_Click(object sender, EventArgs e)
        {
            if (cmbSavedAccounts.SelectedItem == null)
            {
                AddLog("Please select a session to delete", LogType.Warning);
                return;
            }

            var selectedText = cmbSavedAccounts.SelectedItem.ToString();
            var username = selectedText.Split('-')[0].Trim();

            var result = MessageBox.Show(
                $"Delete saved session for {username}?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                _sessionManager.DeleteSession(username);
                AddLog($"Session deleted for {username}", LogType.Info);
                LoadSavedAccounts();
            }
        }

        private void cmbSavedAccounts_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbSavedAccounts.SelectedItem != null)
            {
                var selectedText = cmbSavedAccounts.SelectedItem.ToString();
                var username = selectedText.Split('-')[0].Trim();
                txtUsername.Text = username;
            }
        }

        private void ResetLoginButton()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ResetLoginButton));
                return;
            }

            btnLogin.Text = "LOGIN";
            btnLogin.BackColor = Color.FromArgb(33, 150, 243);
            btnLogin.Enabled = true;

            btnLogin.Click -= CheckLoginStatus;
            btnLogin.Click += btnLogin_Click;
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (!_licenseManager.CanPerformUnfollow())
            {
                MessageBox.Show("License validation failed. Please check your license status.",
                    "License Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AddLog("Start button clicked...", LogType.Info);

            bool isLoggedIn = _bot.IsLoggedIn();
            bool hasFile = _usersToUnfollow?.Count > 0;
            bool isRunning = _bot.IsRunning;
          

            AddLog($"Debug - Logged in: {isLoggedIn}, Has file: {hasFile}, Is running: {isRunning}", LogType.Info);

            if (lblFileStatus.Text == "No file selected")
            {
                AddLog("❌ Please select a users file first", LogType.Error);
                return;
            }

            if (!isLoggedIn)
            {
                AddLog("❌ Please login first", LogType.Error);
                return;
            }

            if (isRunning)
            {
                AddLog("❌ Bot is already running", LogType.Error);
                return;
            }

            _bot.DelayBetweenUnfollows = (int)numDelay.Value;
            _bot.BreakAfterUsers = (int)numBreakAfter.Value;
            _bot.BreakDurationMinutes = (int)numBreakDuration.Value;
            _bot.HeadlessMode = chkHeadless.Checked;

            AddLog("✅ Starting unfollow process...", LogType.Info);

            try
            {
                await _bot.StartUnfollowProcessOnly(
                    _selectedFilePath,
                    new List<string>(_usersToUnfollow)
                );
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error starting process: {ex.Message}", LogType.Error);
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_bot.IsRunning)
            {
                if (_bot.IsPaused)
                {
                    _bot.Resume();
                    AddLog("Process resumed", LogType.Info);
                }
                else
                {
                    _bot.Pause();
                    AddLog("Process paused", LogType.Warning);
                }
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (_bot.IsRunning)
            {
                _bot.Stop();
                AddLog("Process stopped by user", LogType.Warning);
            }
        }

        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.Title = "Select users list file";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    _selectedFilePath = openFileDialog.FileName;
                    lblFileStatus.Text = Path.GetFileName(_selectedFilePath);

                    try
                    {
                        _usersToUnfollow = LoadUsernamesFromFile(_selectedFilePath);
                        UpdateStatistics(_usersToUnfollow.Count, 0, 0, _usersToUnfollow.Count);
                        UpdateProgress(0, _usersToUnfollow.Count);
                        AddLog($"✅ Loaded {_usersToUnfollow.Count} users from file", LogType.Success);

                        UpdateControlStates(_bot.IsRunning);

                        if (_bot.IsLoggedIn() && _usersToUnfollow.Count > 0)
                        {
                            btnStart.Enabled = true;
                            AddLog("✅ Start button enabled - ready to go!", LogType.Success);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"❌ Error loading file: {ex.Message}", LogType.Error);
                        lblFileStatus.Text = "No file selected";
                        _usersToUnfollow = new List<string>();
                    }
                }
            }
        }

        private List<string> LoadUsernamesFromFile(string filePath)
        {
            return File.ReadAllLines(filePath)
                      .Where(line => !string.IsNullOrWhiteSpace(line))
                      .Select(line => line.Trim())
                      .ToList();
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            AddLog("Log cleared", LogType.Info);
        }

        private void chkHeadless_CheckedChanged(object sender, EventArgs e)
        {
            AddLog($"Headless mode: {(chkHeadless.Checked ? "Enabled" : "Disabled")}", LogType.Info);
        }

        private void btnReload_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedFilePath) && File.Exists(_selectedFilePath))
            {
                try
                {
                    _usersToUnfollow = LoadUsernamesFromFile(_selectedFilePath);
                    UpdateStatistics(_usersToUnfollow.Count, 0, 0, _usersToUnfollow.Count);
                    UpdateProgress(0, _usersToUnfollow.Count);
                    AddLog($"Reloaded {_usersToUnfollow.Count} users from file", LogType.Success);
                }
                catch (Exception ex)
                {
                    AddLog($"Error reloading file: {ex.Message}", LogType.Error);
                }
            }
            else
            {
                AddLog("No file selected to reload", LogType.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_bot.IsRunning)
            {
                var result = MessageBox.Show(
                    "The unfollow process is still running. Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _bot.Stop();
            }

            _bot.Dispose();
            base.OnFormClosing(e);
        }

        private async void btnLoadSession_Click_1(object sender, EventArgs e)
        {
            if (cmbSavedAccounts.SelectedItem == null)
            {
                AddLog("Please select a saved account", LogType.Warning);
                return;
            }

            var selectedText = cmbSavedAccounts.SelectedItem.ToString();
            var username = selectedText.Split('-')[0].Trim();

            AddLog($"Loading session for {username}...", LogType.Info);

            // Disable buttons during loading
            btnLoadSession.Enabled = false;
            btnDeleteSession.Enabled = false;
            btnLogin.Enabled = false;
            btnLoadSession.Text = "LOADING...";

            try
            {
                // Try to login with cookies
                bool success = await _bot.LoginWithCookies(username);

                if (success)
                {
                    AddLog($"✅ Session loaded successfully for {username}", LogType.Success);
                    txtUsername.Text = username;

                    // Update UI to show logged in state
                    UpdateControlStates(false); // This will update the login button

                    if (_usersToUnfollow?.Count > 0)
                    {
                        btnStart.Enabled = true;
                        AddLog("✓ Start button enabled - ready to go!", LogType.Success);
                    }
                }
                else
                {
                    AddLog($"❌ Failed to load session for {username}", LogType.Error);
                    AddLog("Please login with username/password instead", LogType.Warning);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Session load error: {ex.Message}", LogType.Error);
            }
            finally
            {
                // Re-enable buttons
                btnLoadSession.Enabled = cmbSavedAccounts.Items.Count > 0;
                btnDeleteSession.Enabled = cmbSavedAccounts.Items.Count > 0;
                btnLogin.Enabled = true;
                btnLoadSession.Text = "Load Session";
            }
        }

        private void btnDeleteSession_Click_1(object sender, EventArgs e)
        {

        }

        private void cmbSavedAccounts_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }
    }

   
}