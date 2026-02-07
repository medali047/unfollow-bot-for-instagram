using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenQA.Selenium;

namespace InstagramUnfollowBot
{
    public class InstagramSession
    {
        public string Username { get; set; }
        public string SessionFilePath { get; set; }
        public DateTime LastLogin { get; set; }
        public bool IsValid { get; set; } = true;
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

                var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sessionFile, json);

                // Update sessions list
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
        public List<Cookie> LoadSession(string username)
        {
            try
            {
                var sessionFile = Path.Combine(_sessionsFolder, $"{username}.json");
                if (!File.Exists(sessionFile))
                    return null;

                var json = File.ReadAllText(sessionFile);
                var cookieData = JsonSerializer.Deserialize<List<CookieData>>(json);

                if (cookieData == null || cookieData.Count == 0)
                    return null;

                var cookies = new List<Cookie>();
                foreach (var c in cookieData)
                {
                    try
                    {
                        // Ensure domain is correct for Instagram
                        string domain = string.IsNullOrEmpty(c.Domain) ? ".instagram.com" : c.Domain;

                        var cookie = new Cookie(c.Name, c.Value, domain, c.Path, c.Expiry);
                        cookies.Add(cookie);
                    }
                    catch (Exception ex)
                    {
                        // Skip invalid cookies but log the issue
                        System.Diagnostics.Debug.WriteLine($"Skipping invalid cookie {c.Name}: {ex.Message}");
                    }
                }

                return cookies;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading session: {ex.Message}");
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
        

        public List<string> GetAvailableSessions()
        {
            try
            {
                return _sessions
                    .Where(s => SessionFileExists(s.Username))
                    .Select(s => s.Username)
                    .ToList();
            }
            catch
            {
                return new List<string>();
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
                    _sessions = JsonSerializer.Deserialize<List<InstagramSession>>(json) ?? new List<InstagramSession>();
                }
                else
                {
                    _sessions = new List<InstagramSession>();
                }

                // Remove invalid sessions (files that don't exist)
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
                var json = JsonSerializer.Serialize(_sessions, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sessionListFile, json);
            }
            catch
            {
                // Ignore errors saving session list
            }
        }
    }
}