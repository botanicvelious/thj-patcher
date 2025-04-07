using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using THJPatcher.Models;
using WpfApplication = System.Windows.Application;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service that handles application initialization tasks
    /// </summary>
    public class InitializationService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        private readonly Func<string, string, MessageBoxButton, MessageBoxImage, bool?> _showMessageAction;
        private readonly Action<int> _progressAction;
        private readonly bool _isAdministrator;
        private readonly ServerStatusService _serverStatusService;
        private readonly ChangelogService _changelogService;
        private readonly FileSystemService _fileSystemService;
        private readonly string _filelistUrl;
        private readonly List<FileEntry> _filesToDownload;
        private Window _mainWindow;
        
        private LoadingMessages _loadingMessages;
        private Random _random = new Random();
        private LatestChangelogWindow _latestChangelogWindow;

        public InitializationService(
            bool isDebugMode,
            Action<string> logAction,
            Action<int> progressAction,
            Func<string, string, MessageBoxButton, MessageBoxImage, bool?> showMessageAction,
            bool isAdministrator,
            ServerStatusService serverStatusService,
            ChangelogService changelogService,
            FileSystemService fileSystemService,
            string filelistUrl,
            List<FileEntry> filesToDownload,
            Window mainWindow)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction;
            _progressAction = progressAction;
            _showMessageAction = showMessageAction;
            _isAdministrator = isAdministrator;
            _serverStatusService = serverStatusService;
            _changelogService = changelogService;
            _fileSystemService = fileSystemService;
            _filelistUrl = filelistUrl;
            _filesToDownload = filesToDownload;
            _mainWindow = mainWindow;
            
            LoadLoadingMessages();
        }
        
        /// <summary>
        /// Initializes the application state
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task<bool> CompleteInitializationAsync(CancellationTokenSource cts)
        {
            // Clear any existing download list to start fresh
            _filesToDownload.Clear();

            // Check if changelog needs to be deleted
            bool needsReinitialization = await CleanupChangelogFilesAsync();
            if (needsReinitialization)
            {
                // Clear any cached changelog data
                _changelogService.ClearCache();
            }

            // Remove any conflicting files
            string gamePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            await FileSystemUtils.RemoveConflictingFilesAsync(gamePath, _isDebugMode, _logAction);

            // Check for pending dinput8.dll.new file
            await FileSystemUtils.HandlePendingDinput8Async(gamePath, _isDebugMode, () => _isAdministrator, _logAction);

            // Check server status
            await _serverStatusService.RefreshStatusAsync();
            
            // Initialize changelogs
            _changelogService.Initialize();

            // Check for new changelogs
            bool hasNewChangelogs = await _changelogService.CheckChangelogAsync();
            
            // Show the latest changelogs window if we have new entries
            await ShowNewChangelogsAsync(hasNewChangelogs);

            // Create DXVK configuration for Linux/Proton compatibility
            await CreateDXVKConfigAsync(gamePath);

            // Check for updates to game files
            await CheckForUpdatesAsync(cts);

            // Run a quick file scan
            await _fileSystemService.RunFileIntegrityScanAsync(cts, null, false);

            return true;
        }
        
        /// <summary>
        /// Shows a random loading message
        /// </summary>
        public string GetRandomLoadingMessage()
        {
            if (_loadingMessages?.Messages != null && _loadingMessages.Messages.Count > 0)
            {
                return _loadingMessages.Messages[_random.Next(_loadingMessages.Messages.Count)];
            }
            return null;
        }
        
        /// <summary>
        /// Loads loading messages
        /// </summary>
        private void LoadLoadingMessages()
        {
            // Create messages directly
            _loadingMessages = Models.LoadingMessages.CreateDefault();
        }
        
        /// <summary>
        /// Checks for and cleans up changelog files based on settings
        /// </summary>
        private async Task<bool> CleanupChangelogFilesAsync()
        {
            string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string changelogYmlPath = Path.Combine(appPath, "changelog.yml");
            string changelogMdPath = Path.Combine(appPath, "changelog.md");
            bool needsReinitialization = false;

            if (IniLibrary.instance.DeleteChangelog == null || IniLibrary.instance.DeleteChangelog.ToLower() == "true")
            {
                if (File.Exists(changelogYmlPath))
                {
                    try
                    {
                        await Task.Run(() => File.Delete(changelogYmlPath));
                        _logAction("Outdated Changelog file detected...Changelog will be updated during this patch.");
                        needsReinitialization = true;
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[ERROR] Failed to delete changelog.yml: {ex.Message}");
                    }
                }

                if (File.Exists(changelogMdPath))
                {
                    try
                    {
                        await Task.Run(() => File.Delete(changelogMdPath));
                        needsReinitialization = true;
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[ERROR] Failed to delete changelog.md: {ex.Message}");
                    }
                }

                // Set DeleteChangelog to false for future runs
                IniLibrary.instance.DeleteChangelog = "false";
                await Task.Run(() => IniLibrary.Save());
            }
            
            return needsReinitialization;
        }
        
        /// <summary>
        /// Shows new changelogs if available
        /// </summary>
        private async Task ShowNewChangelogsAsync(bool hasNewChangelogs)
        {
            if (hasNewChangelogs)
            {
                _changelogService.FormatAllChangelogs();
                
                await Task.Run(() => {
                    WpfApplication.Current.Dispatcher.Invoke(() => {
                        if (_latestChangelogWindow == null || !_latestChangelogWindow.IsVisible)
                        {
                            _latestChangelogWindow = new LatestChangelogWindow(_changelogService.ChangelogContent);
                            _latestChangelogWindow.Owner = _mainWindow;
                            _latestChangelogWindow.Show();
                        }
                    });
                });
            }
        }
        
        /// <summary>
        /// Creates DXVK configuration file for Linux/Proton compatibility
        /// </summary>
        private async Task CreateDXVKConfigAsync(string gamePath)
        {
            try
            {
                string dxvkPath = Path.Combine(gamePath, "dxvk.conf");
                string dxvkContent = "[heroesjourneyemu.exe]\nd3d9.shaderModel = 1";

                if (!File.Exists(dxvkPath))
                {
                    await Task.Run(() => File.WriteAllText(dxvkPath, dxvkContent));
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Error] Failed to create DXVK configuration: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks for updates and handles changelog display
        /// </summary>
        private async Task CheckForUpdatesAsync(CancellationTokenSource cts)
        {
            // Display new changelogs if we have any
            if (_changelogService.HasNewChangelogs)
            {
                // Format the changelogs
                _changelogService.FormatAllChangelogs();

                // Show the latest changelogs window
                await ShowNewChangelogsAsync(true);

                // Clear the new changelogs flag
                _changelogService.ClearNewChangelogsFlag();
            }
        }
    }
} 