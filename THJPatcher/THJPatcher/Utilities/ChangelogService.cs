using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using THJPatcher.Models;
using System.Windows;
using System.Diagnostics;
using WpfMessageBox = System.Windows.MessageBox;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service that manages changelog operations
    /// </summary>
    public class ChangelogService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        private readonly string _changelogEndpoint;
        private readonly string _allChangelogsEndpoint;
        
        // State tracking
        private List<ChangelogInfo> _changelogs = new List<ChangelogInfo>();
        private string _changelogContent = "";
        private string _cachedChangelogContent = null;
        private bool _changelogNeedsUpdate = true;
        private bool _hasNewChangelogs = false;
        
        /// <summary>
        /// Gets all changelogs
        /// </summary>
        public List<ChangelogInfo> Changelogs => _changelogs;
        
        /// <summary>
        /// Gets formatted changelog content for display
        /// </summary>
        public string ChangelogContent => _changelogContent;
        
        /// <summary>
        /// Gets whether there are new changelogs available
        /// </summary>
        public bool HasNewChangelogs => _hasNewChangelogs;
        
        public ChangelogService(
            bool isDebugMode, 
            Action<string> logAction,
            string changelogEndpoint,
            string allChangelogsEndpoint)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _changelogEndpoint = changelogEndpoint;
            _allChangelogsEndpoint = allChangelogsEndpoint;
        }
        
        /// <summary>
        /// Initializes changelogs from local storage or creates default if none exists
        /// </summary>
        public void Initialize()
        {
            try
            {
                string appPath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                string changelogPath = Path.Combine(appPath, "changelog.yml");

                // Create default entry
                var defaultEntry = new Dictionary<string, string>
                {
                    ["timestamp"] = DateTime.Now.ToString("O"),
                    ["author"] = "System",
                    ["formatted_content"] = "Welcome to The Heroes' Journey!\n\nNo changelog entries have been loaded yet. Please check back later.",
                    ["raw_content"] = "Welcome to The Heroes' Journey!\n\nNo changelog entries have been loaded yet. Please check back later.",
                    ["message_id"] = "default"
                };

                // Load changelogs from yml file if it exists
                var entries = IniLibrary.LoadChangelog();

                _changelogs.Clear();

                if (entries.Count > 0)
                {
                    foreach (var entry in entries)
                    {
                        if (entry.TryGetValue("timestamp", out var timestampStr) &&
                            entry.TryGetValue("author", out var author) &&
                            entry.TryGetValue("formatted_content", out var formattedContent) &&
                            entry.TryGetValue("raw_content", out var rawContent) &&
                            entry.TryGetValue("message_id", out var messageId))
                        {
                            if (DateTime.TryParse(timestampStr, out var timestamp))
                            {
                                _changelogs.Add(new ChangelogInfo
                                {
                                    Id = messageId,
                                    Date = timestampStr,
                                    Author = author,
                                    Content = formattedContent
                                });
                            }
                        }
                    }
                }

                if (_changelogs.Count == 0)
                {
                    if (_isDebugMode)
                    {
                        _logAction("[DEBUG] No entries found, creating default changelog");
                    }

                    // Create a new list with the default entry and save it
                    var defaultEntries = new List<Dictionary<string, string>> { defaultEntry };
                    IniLibrary.SaveChangelog(defaultEntries);

                    // Add the default entry to the changelogs list
                    _changelogs.Add(new ChangelogInfo
                    {
                        Id = "default",
                        Date = DateTime.Now.ToString("O"),
                        Author = "System",
                        Content = defaultEntry["formatted_content"]
                    });
                }

                // Format all changelogs
                FormatAllChangelogs();
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Failed to initialize changelogs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Formats all changelogs for display
        /// </summary>
        public void FormatAllChangelogs()
        {
            try
            {
                // If we don't need to update and we have cached content, return early
                if (!_changelogNeedsUpdate && !string.IsNullOrEmpty(_cachedChangelogContent))
                {
                    _changelogContent = _cachedChangelogContent;
                    return;
                }

                var formattedLogs = new StringBuilder();

                // Order by date string descending to show newest first
                foreach (var log in _changelogs.OrderByDescending(x => DateTime.Parse(x.Date)))
                {
                    formattedLogs.AppendLine(log.Content);
                }

                _changelogContent = formattedLogs.Length > 0
                    ? formattedLogs.ToString()
                    : "No changelog entries available.";

                // Cache the result
                _cachedChangelogContent = _changelogContent;
                _changelogNeedsUpdate = false;
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Failed to format changelogs: {ex.Message}");
                _changelogContent = "Error loading changelog entries.";
                _cachedChangelogContent = null;
                _changelogNeedsUpdate = true;
            }
        }
        
        /// <summary>
        /// Updates the changelogs list with new entries
        /// </summary>
        public void UpdateChangelogs(List<ChangelogInfo> newChangelogs)
        {
            _changelogs.Clear();
            _changelogs.AddRange(newChangelogs);
            _changelogNeedsUpdate = true;
        }
        
        /// <summary>
        /// Checks for new changelogs from the server
        /// </summary>
        public async Task<bool> CheckChangelogAsync()
        {
            try
            {
                string appPath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                string changelogPath = Path.Combine(appPath, "changelog.yml");

                // Get token from Constants
                string token = Constants.PATCHER_TOKEN;
                
                // Determine if we need all changelogs
                bool fetchAll = !File.Exists(changelogPath) || (_changelogs.Count == 1 && _changelogs[0].Id == "default");
                string currentMessageId = fetchAll ? "" : IniLibrary.GetLatestMessageId();

                // Call the utility method to fetch data
                var fetchedResponse = await ApiUtils.FetchChangelogDataAsync(
                    token,
                    _changelogEndpoint,
                    _allChangelogsEndpoint,
                    fetchAll,
                    currentMessageId,
                    _isDebugMode,
                    _logAction);

                if (fetchedResponse == null)
                {
                    return false;
                }

                bool hasUpdates = false;

                // Process the fetched data
                if (fetchedResponse.Status == "success")
                {
                    // If this is our first fetch or we have a newer version
                    if (fetchAll || fetchedResponse.Changelogs.Count > 0)
                    {
                        List<ChangelogInfo> allLogs = new List<ChangelogInfo>();
                        
                        // If we have existing changelogs and this isn't a full fetch, combine them
                        if (!fetchAll && _changelogs.Count > 0)
                        {
                            allLogs.AddRange(_changelogs);
                        }
                        
                        // Add new changelogs
                        if (fetchedResponse.Changelogs.Count > 0)
                        {
                            allLogs.AddRange(fetchedResponse.Changelogs);
                            
                            // Remove any duplicates by Id
                            allLogs = allLogs
                                .GroupBy(log => log.Id)
                                .Select(group => group.First())
                                .ToList();
                                
                            _hasNewChangelogs = true;
                            hasUpdates = true;
                        }
                        
                        // Update the changelogs with the combined list
                        UpdateChangelogs(allLogs);
                        
                        // Save to local storage
                        SaveChangelogsToLocalStorage();
                    }
                }

                return hasUpdates;
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Failed to check for changelogs: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Saves changelogs to local storage
        /// </summary>
        private void SaveChangelogsToLocalStorage()
        {
            try
            {
                // Convert changelogs to Dictionary<string, string> format for IniLibrary
                var entries = new List<Dictionary<string, string>>();
                
                foreach (var log in _changelogs)
                {
                    entries.Add(new Dictionary<string, string>
                    {
                        ["timestamp"] = log.Date,
                        ["author"] = log.Author,
                        ["formatted_content"] = log.Content,
                        ["raw_content"] = log.Content,
                        ["message_id"] = log.Id
                    });
                }
                
                IniLibrary.SaveChangelog(entries);
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Failed to save changelogs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clears all cached changelog data
        /// </summary>
        public void ClearCache()
        {
            _changelogs.Clear();
            _changelogContent = "";
            _cachedChangelogContent = null;
            _changelogNeedsUpdate = true;
            _hasNewChangelogs = false;
        }
        
        /// <summary>
        /// Clears the new changelogs flag
        /// </summary>
        public void ClearNewChangelogsFlag()
        {
            _hasNewChangelogs = false;
        }
        
        /// <summary>
        /// Shows the patcher changelog window
        /// </summary>
        /// <param name="owner">The window that owns this dialog</param>
        public void ShowPatcherChangelog(Window owner)
        {
            try
            {
                string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string patcherChangelogPath = Path.Combine(appPath, "patcher_changelog.md");

                // Check if the patcher changelog file exists
                if (!File.Exists(patcherChangelogPath))
                {
                    // Create a default changelog file if it doesn't exist
                    string defaultContent = "# April 4, 2025\n\n## System\n\n- Initial patcher changelog file created\n- This file tracks changes made to the patcher application\n\n---";
                    File.WriteAllText(patcherChangelogPath, defaultContent);

                    if (_isDebugMode)
                    {
                        _logAction("[DEBUG] Created default patcher changelog file");
                    }
                }

                // Read the content from the changelog file
                string changelogContent = File.ReadAllText(patcherChangelogPath);

                // Display the changelog in the ChangelogWindow
                var dialog = new ChangelogWindow(changelogContent);
                dialog.Title = "Patcher Changelog";
                dialog.Owner = owner;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Failed to open patcher changelog: {ex.Message}");
                WpfMessageBox.Show($"Failed to open patcher changelog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 