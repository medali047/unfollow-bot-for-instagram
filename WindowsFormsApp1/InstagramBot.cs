using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Text.Json;


namespace InstagramUnfollowBot
{
    public class InstagramBot : IDisposable
    {

        private ChromeDriver _driver;
        private bool _isRunning;
        private bool _isPaused;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _currentTask;
        private SessionManager _sessionManager;
        // Events for UI updates
        public event Action<string, LogType> LogMessage;
        public event Action<int, int, int, int> StatisticsUpdated;
        public event Action<int, int> ProgressUpdated;
        public event Action<string, StatusType> StatusUpdated;
        public event Action<bool> RunningStateChanged;
  
// ADD THIS LINE
        // Settings
        public int DelayBetweenUnfollows { get; set; } = 60;
        public int BreakAfterUsers { get; set; } = 10;
        public int BreakDurationMinutes { get; set; } = 60;
        public bool HeadlessMode { get; set; } = false;

        public bool IsRunning => _isRunning;
        public bool IsPaused => _isPaused;

        public InstagramBot()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sessionManager = new SessionManager();
        }
        public class CookieData
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTime? Expiry { get; set; }
            public bool Secure { get; set; }
            public bool IsHttpOnly { get; set; }
        }

        public class InstagramSession
        {
            public string Username { get; set; }
            public string SessionFilePath { get; set; }
            public DateTime LastLogin { get; set; }
            public bool IsValid { get; set; } = true;
        }

        public class SessionManager
        {
            private readonly string _sessionsFolder;
            private List<InstagramSession> _sessions;

            public SessionManager()
            {
                _sessionsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sessions");
                Directory.CreateDirectory(_sessionsFolder);
                LoadSessions();
            }
            public bool SessionFileExists(string username)
            {
                try
                {
                    var sessionFile = Path.Combine(_sessionsFolder, $"{username}.json");
                    return File.Exists(sessionFile);
                }
                catch
                {
                    return false;
                }

            }

            public List<InstagramSession> GetSessions()
            {
                return _sessions.Where(s => s.IsValid).OrderByDescending(s => s.LastLogin).ToList();
            }

            public void SaveSession(string username, List<Cookie> cookies)
            {
                try
                {
                    var sessionFile = Path.Combine(_sessionsFolder, $"{username}.json");
                    var cookieData = cookies.Select(c => new CookieData
                    {
                        Name = c.Name,
                        Value = c.Value,
                        Domain = c.Domain,
                        Path = c.Path,
                        Expiry = c.Expiry,
                        Secure = c.Secure,
                        IsHttpOnly = c.IsHttpOnly
                    }).ToList();

                    var json = System.Text.Json.JsonSerializer.Serialize(cookieData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sessionFile, json);

                    var existingSession = _sessions.FirstOrDefault(s => s.Username == username);
                    if (existingSession != null)
                    {
                        existingSession.LastLogin = DateTime.Now;
                        existingSession.IsValid = true;
                    }
                    else
                    {
                        _sessions.Add(new InstagramSession
                        {
                            Username = username,
                            SessionFilePath = sessionFile,
                            LastLogin = DateTime.Now,
                            IsValid = true
                        });
                    }

                    SaveSessionList();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to save session: {ex.Message}");
                }
            }

            public List<Cookie> LoadSession(string username)
            {
                try
                {
                    var sessionFile = Path.Combine(_sessionsFolder, $"{username}.json");
                    if (!File.Exists(sessionFile))
                        return null;

                    var json = File.ReadAllText(sessionFile);
                    var cookieData = System.Text.Json.JsonSerializer.Deserialize<List<CookieData>>(json);

                    if (cookieData == null) return null;

                    var cookies = new List<Cookie>();
                    foreach (var c in cookieData)
                    {
                        try
                        {
                            var cookie = new Cookie(c.Name, c.Value, c.Domain, c.Path, c.Expiry);
                            cookies.Add(cookie);
                        }
                        catch
                        {
                            // Skip invalid cookies
                        }
                    }
                    return cookies;
                }
                catch
                {
                    return null;
                }
            }

            public void DeleteSession(string username)
            {
                try
                {
                    var sessionFile = Path.Combine(_sessionsFolder, $"{username}.json");
                    if (File.Exists(sessionFile))
                        File.Delete(sessionFile);

                    var session = _sessions.FirstOrDefault(s => s.Username == username);
                    if (session != null)
                    {
                        session.IsValid = false;
                        SaveSessionList();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to delete session: {ex.Message}");
                }
            }

            private void LoadSessions()
            {
                try
                {
                    var sessionListFile = Path.Combine(_sessionsFolder, "sessions.json");
                    if (File.Exists(sessionListFile))
                    {
                        var json = File.ReadAllText(sessionListFile);
                        _sessions = System.Text.Json.JsonSerializer.Deserialize<List<InstagramSession>>(json) ?? new List<InstagramSession>();
                    }
                    else
                    {
                        _sessions = new List<InstagramSession>();
                    }

                    _sessions.RemoveAll(s => !File.Exists(s.SessionFilePath));
                }
                catch
                {
                    _sessions = new List<InstagramSession>();
                }
            }

            private void SaveSessionList()
            {
                try
                {
                    var sessionListFile = Path.Combine(_sessionsFolder, "sessions.json");
                    var json = System.Text.Json.JsonSerializer.Serialize(_sessions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sessionListFile, json);
                }
                catch
                {
                    // Ignore errors
                }
            }
        }
        public async Task<bool> LoginWithCookies(string username)
        {
            if (_isRunning || _driver != null)
                return false;

            try
            {
                StatusUpdated?.Invoke("Loading saved session...", StatusType.Processing);

                // FIRST: Check if session file actually exists
                if (!_sessionManager.SessionFileExists(username))
                {
                    LogMessage?.Invoke($"❌ Session file not found for {username}", LogType.Error);
                    return false;
                }

                // SECOND: Load cookies from the file
                var cookies = _sessionManager.LoadSession(username);
                if (cookies == null || cookies.Count == 0)
                {
                    LogMessage?.Invoke($"❌ No valid cookies found in session file for {username}", LogType.Error);
                    return false;
                }

                LogMessage?.Invoke($"✅ Session file found with {cookies.Count} cookies for {username}", LogType.Info);

                // Rest of your existing LoginWithCookies code continues here...
                var driverService = ChromeDriverService.CreateDefaultService();
                driverService.HideCommandPromptWindow = true;

                var options = new ChromeOptions();
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--disable-extensions");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                if (HeadlessMode)
                {
                    options.AddArgument("--headless");
                }

                _driver = new ChromeDriver(driverService, options);
                _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);

                ((IJavaScriptExecutor)_driver).ExecuteScript("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");

                _driver.Navigate().GoToUrl("https://www.instagram.com/");
                Thread.Sleep(2000);

                _driver.Manage().Cookies.DeleteAllCookies();
                Thread.Sleep(1000);

                int cookiesAdded = 0;
                foreach (var cookie in cookies)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(cookie.Domain) || cookie.Domain == "instagram.com")
                        {
                            var fixedCookie = new Cookie(cookie.Name, cookie.Value, ".instagram.com", cookie.Path, cookie.Expiry);
                            _driver.Manage().Cookies.AddCookie(fixedCookie);
                        }
                        else
                        {
                            _driver.Manage().Cookies.AddCookie(cookie);
                        }
                        cookiesAdded++;
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"Warning: Could not load cookie {cookie.Name}: {ex.Message}", LogType.Warning);
                    }
                }

                LogMessage?.Invoke($"Successfully added {cookiesAdded} cookies", LogType.Info);

                _driver.Navigate().Refresh();
                Thread.Sleep(5000);

                if (!IsLoggedIn())
                {
                    LogMessage?.Invoke("Trying alternative URL...", LogType.Info);
                    _driver.Navigate().GoToUrl("https://www.instagram.com/accounts/edit/");
                    Thread.Sleep(3000);
                }

                if (IsLoggedIn())
                {
                    LogMessage?.Invoke($"✅ Successfully logged in with saved session: {username}", LogType.Success);
                    StatusUpdated?.Invoke("Ready", StatusType.Ready);
                    return true;
                }
                else
                {
                    LogMessage?.Invoke($"❌ Saved session expired for {username}", LogType.Error);
                    _sessionManager.DeleteSession(username);
                    CleanupDriver();
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Cookie login error: {ex.Message}", LogType.Error);
                CleanupDriver();
                return false;
            }
        }

        private void SaveSessionCookies(string username)
        {
            try
            {
                var cookies = _driver.Manage().Cookies.AllCookies;
                _sessionManager.SaveSession(username, cookies.ToList());
                LogMessage?.Invoke($"✅ Session saved for {username}", LogType.Success);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Warning: Could not save session: {ex.Message}", LogType.Warning);
            }
        }

        public List<InstagramSession> GetSavedSessions()
        {
            return _sessionManager.GetSessions();
        }
        public async Task<bool> LoginOnly(string username, string password)
        {
            if (_isRunning || _driver != null)
                return false;

            try
            {
                StatusUpdated?.Invoke("Initializing browser...", StatusType.Processing);

                // Setup ChromeDriver with simple configuration
                var options = new ChromeOptions();

                // Basic options
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--disable-extensions");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");

                // Remove console window options that might cause issues
                if (HeadlessMode)
                {
                    options.AddArgument("--headless");
                }

                // Create driver service to hide console
                var driverService = ChromeDriverService.CreateDefaultService();
                driverService.HideCommandPromptWindow = true; // This should work

                _driver = new ChromeDriver(driverService, options);
                _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
                _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);

                // Remove webdriver detection
                ((IJavaScriptExecutor)_driver).ExecuteScript("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");

                // Login to Instagram
                bool loginSuccess = await LoginToInstagramAsync(username, password);

                if (loginSuccess)
                {
                    StatusUpdated?.Invoke("Please complete login manually...", StatusType.Processing);
                    LogMessage?.Invoke("Please complete any verification in browser", LogType.Info);
                    LogMessage?.Invoke("Click 'Start' when ready to begin unfollowing", LogType.Info);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Login error: {ex.Message}", LogType.Error);
                CleanupDriver();
                return false;
            }
        }

        private async Task<bool> LoginToInstagramAsync(string username, string password)
        {
            try
            {
                StatusUpdated?.Invoke("Logging in to Instagram...", StatusType.Processing);
                LogMessage?.Invoke("Navigating to Instagram...", LogType.Info);

                _driver.Navigate().GoToUrl("https://www.instagram.com/");

                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
                wait.Until(d => d.FindElement(By.Name("username")).Displayed);

                _driver.FindElement(By.Name("username")).SendKeys(username);
                _driver.FindElement(By.Name("password")).SendKeys(password);
                _driver.FindElement(By.CssSelector("button[type='submit']")).Click();

                LogMessage?.Invoke("Credentials submitted...", LogType.Info);

                bool loginSuccess = await WaitForLoginResult();

                // ADD THIS PART - Save cookies if login successful
                if (loginSuccess && IsLoggedIn())
                {
                    SaveSessionCookies(username);
                }

                return loginSuccess;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Login failed: {ex.Message}", LogType.Error);
                return false;
            }
        }

        private async Task<bool> WaitForLoginResult()
        {
            await Task.Yield();
            return await Task.Run(() =>
            {
                try
                {
                    var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));

                    return wait.Until(d =>
                    {
                        try
                        {
                            // Check for main navigation (successful login)
                            if (d.FindElements(By.CssSelector("nav[role='navigation']")).Count > 0)
                            {
                                LogMessage?.Invoke("Login successful! Main page detected.", LogType.Success);
                                return true;
                            }

                            // Check for security challenge
                            if (d.FindElements(By.CssSelector("input[name='security_code']")).Count > 0)
                            {
                                LogMessage?.Invoke("Security verification required.", LogType.Warning);
                                return true;
                            }

                            // Check for save login prompt
                            if (d.FindElements(By.XPath("//button[contains(text(), 'Save')]")).Count > 0 ||
                                d.FindElements(By.XPath("//button[contains(text(), 'Enregistrer')]")).Count > 0)
                            {
                                LogMessage?.Invoke("Save login prompt detected.", LogType.Info);
                                return true;
                            }

                            return false;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                }
                catch (WebDriverTimeoutException)
                {
                    LogMessage?.Invoke("Login timeout. Please complete manually.", LogType.Warning);
                    return true; // Allow manual completion
                }
            });
        }

        public bool IsLoggedIn()
        {
            try
            {
                if (_driver == null)
                {
                    LogMessage?.Invoke("Browser not initialized", LogType.Warning);
                    return false;
                }

                // Check current URL first
                string currentUrl = _driver.Url;
                if (currentUrl.Contains("instagram.com") &&
                    !currentUrl.Contains("/accounts/login") &&
                    !currentUrl.Contains("/auth") &&
                    !currentUrl.Contains("/recover"))
                {
                    LogMessage?.Invoke("URL indicates logged in state", LogType.Info);
                    return true;
                }

                // Check for multiple logged-in indicators
                var loggedInIndicators = new[]
                {
            By.CssSelector("nav[role='navigation']"),
            By.CssSelector("a[href*='/direct/inbox/']"),
            By.CssSelector("a[href*='/create/select/']"),
            By.CssSelector("svg[aria-label='Home']"),
            By.CssSelector("svg[aria-label='Search']"),
            By.CssSelector("main[role='main']"),
            By.XPath("//span[text()='Home']"),
            By.XPath("//span[text()='Search']"),
            By.CssSelector("img[alt*='profile']"), // Profile picture
            By.CssSelector("a[href*='/accounts/edit/']") // Edit profile link
        };

                foreach (var indicator in loggedInIndicators)
                {
                    try
                    {
                        var elements = _driver.FindElements(indicator);
                        if (elements.Count > 0 && elements[0].Displayed)
                        {
                            LogMessage?.Invoke($"✓ Login verified using {indicator}", LogType.Success);
                            return true;
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        continue;
                    }
                    catch (StaleElementReferenceException)
                    {
                        continue;
                    }
                }

                // Additional check: look for login page elements (negative test)
                var loginIndicators = new[]
                {
            By.Name("username"),
            By.Name("password"),
            By.XPath("//button[contains(text(), 'Log In')]"),
            By.XPath("//button[contains(text(), 'Se connecter')]")
        };

                foreach (var indicator in loginIndicators)
                {
                    try
                    {
                        var elements = _driver.FindElements(indicator);
                        if (elements.Count > 0 && elements[0].Displayed)
                        {
                            LogMessage?.Invoke("✗ Login page detected - not logged in", LogType.Warning);
                            return false;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                LogMessage?.Invoke("✗ Unable to determine login status", LogType.Warning);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error checking login status: {ex.Message}", LogType.Error);
                return false;
            }
        }
        public async Task StartUnfollowProcessOnly(string filePath, List<string> usersToUnfollow)
        {
            if (_isRunning || _driver == null || !IsLoggedIn())
            {
                LogMessage?.Invoke("Cannot start: Check login status and browser", LogType.Error);
                return;
            }

            _isRunning = true;
            _isPaused = false;
            RunningStateChanged?.Invoke(true);

            try
            {
                _currentTask = Task.Run(() => ProcessUsersList(filePath, usersToUnfollow));
                await _currentTask;
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("Process cancelled", LogType.Warning);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Process error: {ex.Message}", LogType.Error);
            }
            finally
            {
                _isRunning = false;
                _isPaused = false;
                RunningStateChanged?.Invoke(false);
                _currentTask = null;
            }
        }

        private void ProcessUsersList(string filePath, List<string> usersToUnfollow)
        {
            int initialTotalUsers = usersToUnfollow.Count;
            int successfullyUnfollowed = 0;
            int failed = 0;
            var random = new Random();

            StatusUpdated?.Invoke("Starting unfollow process...", StatusType.Processing);
            LogMessage?.Invoke($"Processing {initialTotalUsers} users...", LogType.Info);

            StatisticsUpdated?.Invoke(initialTotalUsers, 0, 0, usersToUnfollow.Count);
            ProgressUpdated?.Invoke(0, initialTotalUsers);

            for (int i = 0; i < usersToUnfollow.Count && _isRunning; i++)
            {
                while (_isPaused && _isRunning)
                {
                    Thread.Sleep(500);
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;
                }

                if (_cancellationTokenSource.Token.IsCancellationRequested || !_isRunning)
                    break;

                string user = usersToUnfollow[i];
                int currentPosition = successfullyUnfollowed + failed + 1;
                LogMessage?.Invoke($"Processing: {user} ({currentPosition}/{initialTotalUsers})", LogType.Info);

                bool unfollowed = UnfollowUser(user);

                if (unfollowed)
                {
                    successfullyUnfollowed++;
                    usersToUnfollow.RemoveAt(i);
                    i--;

                    SaveUpdatedList(usersToUnfollow, filePath);

                    LogMessage?.Invoke($"✓ Unfollowed: {user}", LogType.Success);

                    StatisticsUpdated?.Invoke(initialTotalUsers, successfullyUnfollowed, failed, usersToUnfollow.Count);
                    ProgressUpdated?.Invoke(successfullyUnfollowed + failed, initialTotalUsers);

                    if (i < usersToUnfollow.Count - 1)
                    {
                        int delaySeconds = random.Next(DelayBetweenUnfollows - 10, DelayBetweenUnfollows + 10);
                        LogMessage?.Invoke($"Waiting {delaySeconds} seconds...", LogType.Info);

                        for (int second = 0; second < delaySeconds && _isRunning && !_isPaused; second++)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                return;
                            Thread.Sleep(1000);
                        }
                    }

                    if (successfullyUnfollowed % BreakAfterUsers == 0 && successfullyUnfollowed > 0)
                    {
                        LogMessage?.Invoke($"Break: {BreakDurationMinutes} minutes after {BreakAfterUsers} users", LogType.Warning);
                        StatusUpdated?.Invoke("Taking break...", StatusType.Paused);

                        for (int minute = 0; minute < BreakDurationMinutes && _isRunning && !_isPaused; minute++)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                return;
                            Thread.Sleep(60000);
                        }

                        StatusUpdated?.Invoke("Resuming...", StatusType.Processing);
                    }
                }
                else
                {
                    failed++;
                    LogMessage?.Invoke($"✗ Failed: {user}", LogType.Error);
                    StatisticsUpdated?.Invoke(initialTotalUsers, successfullyUnfollowed, failed, usersToUnfollow.Count);
                    ProgressUpdated?.Invoke(successfullyUnfollowed + failed, initialTotalUsers);

                    Thread.Sleep(5000);
                }
            }

            if (_isRunning)
            {
                StatusUpdated?.Invoke("Completed", StatusType.Ready);
                LogMessage?.Invoke($"Finished! Success: {successfullyUnfollowed}, Failed: {failed}", LogType.Success);
            }
            else
            {
                StatusUpdated?.Invoke("Stopped", StatusType.Warning);
                LogMessage?.Invoke($"Stopped. Progress: {successfullyUnfollowed}/{initialTotalUsers}", LogType.Warning);
            }
        }

        private bool UnfollowUser(string username)
        {
            try
            {
                _driver.Navigate().GoToUrl($"https://www.instagram.com/{username}/");

                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                wait.Until(d => d.FindElement(By.CssSelector("header section")));

                IWebElement followingButton = FindFollowingButton();
                if (followingButton == null) return false;

                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", followingButton);
                Thread.Sleep(1000);

                try
                {
                    followingButton.Click();
                }
                catch
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", followingButton);
                }

                Thread.Sleep(2000);

                IWebElement unfollowOption = FindUnfollowOption();
                if (unfollowOption == null) return false;

                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView(true);", unfollowOption);
                Thread.Sleep(1000);

                try
                {
                    unfollowOption.Click();
                }
                catch
                {
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", unfollowOption);
                }

                Thread.Sleep(2000);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Unfollow error for {username}: {ex.Message}", LogType.Error);
                return false;
            }
        }

        private IWebElement FindFollowingButton()
        {
            string[] selectors = {
                "//div[text()='Following']",
                "//button[.//div[text()='Following']]",
                "//div[text()='Suivi(e)']",
                "//button[.//div[text()='Suivi(e)']]"
            };

            foreach (string selector in selectors)
            {
                try
                {
                    var element = _driver.FindElement(By.XPath(selector));
                    if (element.Displayed && element.Enabled)
                        return element;
                }
                catch (NoSuchElementException)
                {
                    continue;
                }
            }
            return null;
        }

        private IWebElement FindUnfollowOption()
        {
            string[] selectors = {
                "//span[text()='Unfollow']/ancestor::div[@role='button']",
                "//span[text()='Ne plus suivre']/ancestor::div[@role='button']",
                "//div[@role='dialog']//div[@role='button'][last()]"
            };

            foreach (string selector in selectors)
            {
                try
                {
                    var element = _driver.FindElement(By.XPath(selector));
                    if (element.Displayed && element.Enabled)
                        return element;
                }
                catch (NoSuchElementException)
                {
                    continue;
                }
            }
            return null;
        }

        private void SaveUpdatedList(List<string> usersToUnfollow, string filePath)
        {
            try
            {
                File.WriteAllLines(filePath, usersToUnfollow);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Save error: {ex.Message}", LogType.Error);
            }
        }

        public void Pause()
        {
            if (_isRunning)
            {
                _isPaused = true;
                StatusUpdated?.Invoke("Paused", StatusType.Paused);
            }
        }

        public void Resume()
        {
            if (_isRunning && _isPaused)
            {
                _isPaused = false;
                StatusUpdated?.Invoke("Resuming...", StatusType.Processing);
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _isPaused = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _currentTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException) { }

            StatusUpdated?.Invoke("Stopped", StatusType.Warning);
            CleanupDriver();
        }

        private void CleanupDriver()
        {
            try
            {
                if (_driver != null)
                {
                    _driver.Quit();
                    _driver.Dispose();
                    _driver = null;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Cleanup error: {ex.Message}", LogType.Warning);
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            CleanupDriver();
        }
    }
}