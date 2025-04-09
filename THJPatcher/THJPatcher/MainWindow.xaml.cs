using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Documents;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using THJPatcher.Models;
using THJPatcher.Utilities; // Added using for Utilities
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Windows.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using THJPatcher.Converters;
using System.Windows.Threading; // Added for DispatcherTimer

namespace THJPatcher
{
    public partial class MainWindow : Window
    {
        private readonly SolidColorBrush _redBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0));
        private readonly SolidColorBrush _defaultButtonBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 233, 164)); // #FBE9A4
        private bool isPatching = false;
        private bool isPendingPatch = false;
        private bool isNeedingSelfUpdate = false;
        private bool isLoading;
        private bool isAutoPatch = false;
        private bool isAutoPlay = false;
        private bool isDebugMode = false;
        private bool isSilentMode = false;
        private bool isAutoConfirm = false;
        private bool isCheckingForUpdates = false; // New flag to prevent duplicate checks
        private CancellationTokenSource cts;
        private string myHash = "";
        private string patcherUrl;
        private string fileName;
        private string version;
        public string Version => version;
        private List<FileEntry> filesToDownload = new List<FileEntry>();

        // Server and file configuration
        private static string serverName;
        private static string filelistUrl;

        // Add a field to track initialization state
        private bool hasInitialized = false;

        // Supported client versions
        public static List<VersionTypes> supportedClients = new List<VersionTypes> {
            VersionTypes.Rain_Of_Fear,
            VersionTypes.Rain_Of_Fear_2
        };

        private Dictionary<VersionTypes, ClientVersion> clientVersions = new Dictionary<VersionTypes, ClientVersion>();

        private readonly string changelogEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog/";
        private readonly string allChangelogsEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog?all=true";
        private readonly string patcherToken;

        private bool autoScroll = true;

        private Random random = new Random();

        // Add the ServerStatusService as a member
        private ServerStatusService serverStatusService;

        // Add the ChangelogService as a member and remove old changelog-related fields
        private ChangelogService changelogService;
        
        // Add the PatcherService as a member
        private PatcherService patcherService;
        
        // Add the FileSystemService as a member
        private FileSystemService fileSystemService;

        // Add members for new services
        private OptimizationService optimizationService;
        private GameLaunchService gameLaunchService;
        private SelfUpdateService selfUpdateService;
        private NavigationService navigationService;
        private InitializationService initializationService;
        private ClientVersionService clientVersionService;
        private CommandLineService commandLineService;

        // Add static Status class to hold application state
        public static class Status
        {
            public static bool IsPatching { get; set; }
            public static bool HasUpdates { get; set; }
            public static bool IsAutoPatch { get; set; }
            public static bool IsAutoPlay { get; set; }
        }

        private string FormatAuthorName(string author)
        {
            if (string.IsNullOrEmpty(author)) return "System";

            // Convert to lowercase for comparison
            var lowerAuthor = author.ToLower();

            // Handle specific author names
            if (lowerAuthor == "catapultam_habeo" || lowerAuthor == "catapultam")
                return "Catapultam";
            if (lowerAuthor == "aporia")
                return "Aporia";

            // For other names, just return as is
            return author;
        }

        private DispatcherTimer logUpdateTimer;
        private readonly object logBufferLock = new object(); // Lock for buffer access
        private StringBuilder logBuffer = new StringBuilder(); // Buffer for batching

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            btnPatch.Click += BtnPatch_Click;
            btnPlay.Click += BtnPlay_Click;
            chkAutoPatch.Checked += ChkAutoPatch_CheckedChanged;
            chkAutoPlay.Checked += ChkAutoPlay_CheckedChanged;

            // Add KeyDown event handler for Enter key
            this.KeyDown += MainWindow_KeyDown;

            // Initialize command line service
            commandLineService = new CommandLineService(Environment.GetCommandLineArgs(), StatusLibrary.Log);
            isDebugMode = commandLineService.IsDebugMode;
            isSilentMode = commandLineService.IsSilentMode;
            isAutoConfirm = commandLineService.IsAutoConfirm;

            // If in silent mode, hide the window and ensure console output is visible
            if (isSilentMode)
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                Console.WriteLine("Starting THJ Patcher in silent mode...");
                Console.WriteLine("----------------------------------------");
            }

            // Get the patcher token from Constants
            patcherToken = Constants.PATCHER_TOKEN;
            if (string.IsNullOrEmpty(patcherToken) || patcherToken == "__PATCHER_TOKEN__")
            {
                StatusLibrary.Log("[ERROR] Patcher token not properly initialized");
            }

            // Initialize server configuration
            serverName = "The Heroes Journey";
            if (string.IsNullOrEmpty(serverName))
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the server name is not provided or screenshot this message.");
                Close();
                return;
            }

            fileName = "heroesjourneyemu";

            filelistUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download/";
            if (string.IsNullOrEmpty(filelistUrl))
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the file list url is not provided or screenshot this message.", serverName);
                Close();
                return;
            }
            if (!filelistUrl.EndsWith("/")) filelistUrl += "/";

            patcherUrl = "https://github.com/The-Heroes-Journey-EQEMU/thj-patcher/releases/latest/download/";
            if (string.IsNullOrEmpty(patcherUrl))
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the patcher url is not provided or screenshot this message.", serverName);
                Close();
                return;
            }
            if (!patcherUrl.EndsWith("/")) patcherUrl += "/";

            // Initialize client version service
            clientVersionService = new ClientVersionService(StatusLibrary.Log, isDebugMode);

            // Initialize the ServerStatusService
            serverStatusService = new ServerStatusService(isDebugMode, StatusLibrary.Log);
            
            // Initialize the ChangelogService
            changelogService = new ChangelogService(
                isDebugMode, 
                StatusLibrary.Log,
                changelogEndpoint,
                allChangelogsEndpoint);

            // Initialize the PatcherService
            patcherService = new PatcherService(
                isDebugMode,
                StatusLibrary.Log,
                StatusLibrary.SetProgress,
                SystemInfoUtils.IsAdministrator());
                
            // Initialize the FileSystemService
            fileSystemService = new FileSystemService(
                isDebugMode,
                StatusLibrary.Log,
                StatusLibrary.SetProgress,
                () => SystemInfoUtils.IsAdministrator(),
                filelistUrl,
                filesToDownload);

            // Initialize services
            string eqPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            
            // Message box action for optimization service to display messages
            Func<string, string, MessageBoxButton, MessageBoxImage, bool?> showMessageAction = 
                (message, title, button, icon) => {
                    MessageBoxResult result = MessageBox.Show(message, title, button, icon);
                    return result switch
                    {
                        MessageBoxResult.OK => true,
                        MessageBoxResult.Yes => true,
                        MessageBoxResult.No => false,
                        _ => null // Handles Cancel, None, or closed dialog
                    };
                };
            
            optimizationService = new OptimizationService(
                StatusLibrary.Log,
                showMessageAction,
                eqPath);
                
            gameLaunchService = new GameLaunchService(
                StatusLibrary.Log,
                isSilentMode);
                
            selfUpdateService = new SelfUpdateService(
                isDebugMode,
                StatusLibrary.Log,
                StatusLibrary.SetProgress,
                patcherUrl,
                fileName);

            navigationService = new NavigationService(
                StatusLibrary.Log,
                showMessageAction,
                logPanel,
                optimizationsPanel,
                InitializeOptimizationsPanel);

            // Initialize the initialization service
            initializationService = new InitializationService(
                isDebugMode,
                StatusLibrary.Log,
                StatusLibrary.SetProgress,
                showMessageAction,
                SystemInfoUtils.IsAdministrator(),
                serverStatusService,
                changelogService,
                fileSystemService,
                filelistUrl,
                filesToDownload,
                this);

            // Initialize the patch button spinner to be invisible
            btnPatch.Tag = false;

            // Subscribe to the patch state changes
            StatusLibrary.SubscribePatchState(new StatusLibrary.PatchStateHandler((bool isPatchGoing) =>
            {
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Content = isPatchGoing ? "PATCHING..." : "PATCH";
                    btnPatch.Tag = isPatchGoing;
                });
            }));

            // Initialize and start the log update timer
            logUpdateTimer = new DispatcherTimer(DispatcherPriority.Background); // Use Background priority for timer itself
            logUpdateTimer.Interval = TimeSpan.FromMilliseconds(200); // Update log every 200ms
            logUpdateTimer.Tick += LogUpdateTimer_Tick;
            logUpdateTimer.Start();

            // Subscribe to the log update signal
            StatusLibrary.SubscribeLogUpdateAvailable(StatusLibrary_LogUpdateAvailable);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string url)
            {
                navigationService.OpenUrl(url);
            }
        }

        private void OptimizationsButton_Click(object sender, RoutedEventArgs e)
        {
            navigationService.ShowOptimizationsPanel();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            navigationService.OpenGameFolder();
        }

        private void CloseOptimizations_Click(object sender, RoutedEventArgs e)
        {
            navigationService.ShowLogPanel();
        }

        private async void Apply4GBPatch_Click(object sender, RoutedEventArgs e)
        {
            btn4GBPatch.IsEnabled = false;
            try
            {
                await optimizationService.Apply4GBPatchAsync();
                CheckPatchStatus(); // Update button state after patching
            }
            finally
            {
                CheckPatchStatus(); // Ensure button state is updated
            }
        }

        private void FixUIScale_Click(object sender, RoutedEventArgs e)
        {
            optimizationService.FixUIScale();
        }

        private async void OptimizeGraphics_Click(object sender, RoutedEventArgs e)
        {
            btnOptimizeGraphics.IsEnabled = false;
            try
            {
                await optimizationService.OptimizeGraphicsAsync();
            }
            finally
            {
                btnOptimizeGraphics.IsEnabled = true;
            }
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            btnClearCache.IsEnabled = false;
            try
            {
                await optimizationService.ClearCacheAsync();
            }
            finally
            {
                btnClearCache.IsEnabled = true;
            }
        }

        private async void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            btnResetSettings.IsEnabled = false;
            try
            {
                bool success = await optimizationService.ResetSettingsAsync();
                
                if(success)
                {
                    MessageBox.Show("Settings have been reset. They will be recreated when you next launch EverQuest.", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("An error occurred while trying to reset settings.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting settings: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnResetSettings.IsEnabled = true;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Prevent duplicate initialization
            if (hasInitialized) return;
            hasInitialized = true;
            
            isLoading = true;
            cts = new CancellationTokenSource();

            // Initialize logging first
            StatusLibrary.SubscribeProgress(new StatusLibrary.ProgressHandler(StatusLibrary_ProgressChanged));
            StatusLibrary.SubscribePatchState(new StatusLibrary.PatchStateHandler((bool isPatchGoing) =>
            {
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Content = isPatchGoing ? "PATCHING..." : "PATCH";
                    btnPatch.Tag = isPatchGoing;
                });
            }));

            // Load configuration first
            IniLibrary.Load();

            // Display a welcome message
            string randomMessage = initializationService.GetRandomLoadingMessage();
            if (!string.IsNullOrEmpty(randomMessage))
            {
                StatusLibrary.Log(randomMessage);
                await Task.Delay(1000); // Pause after showing message
            }

            // Get the full version including build number
            var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            version = $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";

            // Set the window as the data context for version binding
            this.DataContext = this;

            // Check for patcher updates first, before any other operations
            if (!isDebugMode)
            {
                var (isUpdateAvailable, hash) = await selfUpdateService.CheckForUpdateAsync(cts);
                if (isUpdateAvailable)
                {
                    isNeedingSelfUpdate = true;
                    myHash = hash;
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                        btnPatch.IsEnabled = true;
                        btnPatch.Content = "PATCH";
                    });
                    isLoading = false;
                    // Exit early if patcher needs updating - don't continue with the rest of initialization
                    return;
                }
            }

            // Only proceed with remaining initialization if no patcher update is needed
            bool initResult = await initializationService.CompleteInitializationAsync(cts);
            
            // Setup UI elements after initialization
            serverStatusService.UpdateUI(txtPlayerCount, txtExpBonus);
            
            // Load configuration
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;

            // If we're in auto-patch mode, start patching (but not for self-updates)
            if (isAutoPatch && !isNeedingSelfUpdate)
            {
                isPendingPatch = true;
                await Task.Delay(1000);
                await StartPatch();
            }

            isLoading = false;
        }

        private void StatusLibrary_ProgressChanged(int progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Convert from 0-10000 range to 0-100 range
                double percentage = progress / 100.0;
                progressBar.Value = percentage;
                
                // Show progress text
                string progressText = $"{percentage:F2}%";
                txtProgress.Text = progressText;
                
                // Show progress elements when patching
                if (Status.IsPatching)
                {
                    txtProgress.Visibility = Visibility.Visible;
                    progressBar.Visibility = Visibility.Visible;
                }
                
                // If in silent mode, also write progress to console and flush immediately
                if (isSilentMode)
                {
                    Console.WriteLine($"Progress: {progressText}");
                    Console.Out.Flush();
                }
            });
        }

        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // User scroll
            if (e.ExtentHeightChange == 0)
            {
                // User scroll detected
                if (logScrollViewer.VerticalOffset == logScrollViewer.ScrollableHeight)
                {
                    // Scrolled to bottom - enable auto-scroll
                    autoScroll = true;
                }
                else
                {
                    // Scrolled up - disable auto-scroll
                    autoScroll = false;
                }
            }

            // Content changed
            if (autoScroll && e.ExtentHeightChange != 0)
            {
                logScrollViewer.ScrollToVerticalOffset(logScrollViewer.ExtentHeight);
            }
        }

        // Signal handler - This can potentially trigger an immediate flush if needed,
        // but for now, we rely on the timer.
        private void StatusLibrary_LogUpdateAvailable()
        {
            // No action needed here currently, timer will pick up the queue.
        }

        // Timer tick handler - processes the log queue
        private void LogUpdateTimer_Tick(object sender, EventArgs e)
        {
            const int MaxMessagesPerTick = 100; // Process up to 100 messages per tick
            var messages = StatusLibrary.DequeueLogMessages(); // Still dequeues all available

            if (messages.Count > 0)
            {
                lock(logBufferLock) // Lock the buffer while appending
                {
                    foreach (var message in messages)
                    {
                        logBuffer.AppendLine(message);
                    }
                }
            }

            // Append buffered messages to the UI text box, but limit the amount
            string textToAppend;
            bool moreMessagesInBuffer = false;
            lock(logBufferLock) // Lock the buffer while reading and potentially modifying
            {
                if (logBuffer.Length == 0) return; // Nothing to append

                // Find the end of the Nth line (or end of buffer)
                int linesProcessed = 0;
                int endIndex = -1;
                for (int i = 0; i < logBuffer.Length && linesProcessed < MaxMessagesPerTick; ++i)
                {
                    if (logBuffer[i] == '\n')
                    {
                        linesProcessed++;
                        if (linesProcessed == MaxMessagesPerTick)
                        {
                            endIndex = i + 1; // Include the newline
                            break;
                        }
                    }
                }

                if (endIndex != -1 && endIndex < logBuffer.Length) // Found limit within buffer
                {
                    textToAppend = logBuffer.ToString(0, endIndex);
                    logBuffer.Remove(0, endIndex); // Remove the processed part
                    moreMessagesInBuffer = true; // Indicate more data remains
                }
                else // Less than MaxMessagesPerTick in buffer, or exactly MaxMessagesPerTick
                {
                    textToAppend = logBuffer.ToString();
                    logBuffer.Clear();
                    moreMessagesInBuffer = false;
                }
            }

            if (!string.IsNullOrEmpty(textToAppend))
            {
                txtLog.AppendText(textToAppend);
                if (autoScroll)
                {
                    logScrollViewer.ScrollToEnd();
                }
            }

            // If we didn't process everything, ensure the timer keeps running
            // (DispatcherTimer runs continuously anyway unless stopped, so this might be redundant)
            // if (moreMessagesInBuffer)
            // {
            //     logUpdateTimer.Start(); // Ensure it runs again soon
            // }
        }

        private async void BtnPatch_Click(object sender, RoutedEventArgs e)
        {
            // Use atomic check and set to prevent duplicate update checks
            if (isLoading && !isPendingPatch)
            {
                // Only proceed if we're not already checking for updates
                lock(logBufferLock) // Reuse existing lock object for thread safety
                {
                    if (isCheckingForUpdates)
                        return;
                    
                    isCheckingForUpdates = true;
                    isPendingPatch = true;
                }
                
                StatusLibrary.Log("Checking for updates...");
                btnPatch.Content = "PATCHING...";
                btnPatch.Tag = true; // Show spinner and disabled appearance
                
                // Reset the flag after a delay to ensure it doesn't get stuck
                await Task.Delay(500);
                isCheckingForUpdates = false;
                return;
            }

            if (isPatching)
            {
                cts.Cancel();
                return;
            }
            await StartPatch();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            bool success = gameLaunchService.LaunchGame();
            if (success)
            {
                this.Close();
            }
        }

        private void ChkAutoPatch_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            isAutoPatch = chkAutoPatch.IsChecked ?? false;
            IniLibrary.instance.AutoPatch = isAutoPatch ? "true" : "false";
            IniLibrary.Save();
        }

        private void ChkAutoPlay_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            isAutoPlay = chkAutoPlay.IsChecked ?? false;
            IniLibrary.instance.AutoPlay = isAutoPlay ? "true" : "false";
            if (isAutoPlay)
                StatusLibrary.Log("To disable autoplay: edit thjpatcher.yml or wait until next patch.");
            IniLibrary.Save();
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true; // Mark as handled to prevent further processing
                
                // Check if Patch button is visible and enabled, and we're not already checking
                if (btnPatch.Visibility == Visibility.Visible && btnPatch.IsEnabled && !isCheckingForUpdates && !isPendingPatch)
                {
                    BtnPatch_Click(null, null);
                }
                // Check if Play button is visible and enabled
                else if (btnPlay.Visibility == Visibility.Visible && btnPlay.IsEnabled)
                {
                    BtnPlay_Click(null, null);
                }
            }
        }

        private async void FileIntegrityScan_Click(object sender, RoutedEventArgs e)
        {
            // Close the optimizations panel
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;

            // Run the file integrity scan with full scan only
            await RunFileIntegrityScanAsync(true);
        }

        private async void MemoryOptimizations_Click(object sender, RoutedEventArgs e)
        {
            // Close the optimizations panel first
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;

            btnMemoryOptimizations.IsEnabled = false;
            string eqcfgPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "eqclient.ini");
            try
            {
                if (!File.Exists(eqcfgPath))
                {
                    MessageBox.Show("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return; // Exit early
                }

                // Show confirmation dialog
                string message = "This will modify settings in the EQclient.ini file to help with memory optimization. Continue?";
                if (!CustomMessageBox.Show(message))
                {
                    return; // User cancelled
                }

                StatusLibrary.Log("Applying memory optimizations to eqclient.ini...");

                bool success = await OptimizationUtils.ApplyMemoryOptimizationsAsync(eqcfgPath);

                if(success)
                {
                    StatusLibrary.Log("Memory optimizations applied successfully!");
                    // Optionally show a MessageBox confirmation too, although log message might suffice
                    // MessageBox.Show("Memory optimizations applied successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusLibrary.Log("Failed to apply memory optimizations.");
                    MessageBox.Show("An error occurred while applying memory optimizations.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) // Catch unexpected errors
            {
                StatusLibrary.Log($"Error applying memory optimizations: {ex.Message}");
                MessageBox.Show($"Error applying memory optimizations: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnMemoryOptimizations.IsEnabled = true;
            }
        }

        private async Task RunFileIntegrityScanAsync(bool fullScanOnly = false)
        {
            var cts = new CancellationTokenSource();
            cts.Token.Register(() => StatusLibrary.Log("File scan cancelled."));

            try
            {
                // Reset cancellation token source if needed
                if (cts.IsCancellationRequested)
                {
                    StatusLibrary.Log("Resetting cancellation token for file scan");
                    cts.Dispose(); // Dispose the old one first
                    cts = new CancellationTokenSource();
                }
                
                // Use the FileSystemService to run the file integrity scan
                bool filesAreUpToDate = await fileSystemService.RunFileIntegrityScanAsync(cts, null, fullScanOnly);
                
                // Update UI based on scan results
                Dispatcher.Invoke(() =>
                {
                    txtProgress.Visibility = Visibility.Collapsed;
                    progressBar.Value = 0;
                    
                    if (filesAreUpToDate)
                    {
                        // All files are up to date, show play button
                        btnPatch.Visibility = Visibility.Collapsed;
                        btnPlay.Visibility = Visibility.Visible;
                        btnPlay.IsEnabled = true;
                    }
                    else if (filesToDownload.Count > 0)
                    {
                        // Files need updating, show patch button
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                        btnPatch.IsEnabled = true;
                        
                        // If auto-patch is enabled, start patching automatically
                        if (isAutoPatch && !isNeedingSelfUpdate)
                        {
                            isPendingPatch = true;
                            // Use Task.Run to avoid blocking the UI thread with await
                            Task.Run(async () => {
                                await Task.Delay(1000);
                                await Dispatcher.InvokeAsync(async () => {
                                    await StartPatch();
                                });
                            });
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                StatusLibrary.Log("Operation was cancelled.");
                UpdatePatchStatus(false);
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"Error during file integrity scan: {ex.Message}");
                UpdatePatchStatus(false);
            }
        }

        private void PatcherChangelog_Click(object sender, RoutedEventArgs e)
        {
            changelogService.ShowPatcherChangelog(this);
        }

        /// <summary>
        /// Begins the patching process
        /// </summary>
        private async Task StartPatch()
        {
            Status.IsPatching = true;
            isPatching = true;
            
            // Update UI immediately
            btnPatch.Content = "PATCHING...";
            btnPatch.Tag = true;
            StatusLibrary.SetPatchState(true);
            
            // Self-update if needed
            if (Status.HasUpdates)
            {
                await selfUpdateService.PerformUpdateAsync(cts);
                return;
            }

            StatusLibrary.Log($"Starting patch at {DateTime.Now:G}");
            await fileSystemService.ForceDinput8CheckAsync(cts);

            try
            {
                // Download file list
                StatusLibrary.Log("Downloading file list...");
                var fileList = await fileSystemService.DownloadAndParseFileListAsync(cts);
                if (fileList == null)
                {
                    StatusLibrary.Log("Cannot download file list!");
                    UpdatePatchStatus(false);
                    return;
                }

                // Build file list for patching
                await fileSystemService.BuildFileList(fileList, cts);

                // Check if we have files to patch
                if (filesToDownload.Count > 0)
                {
                    // Run the patch operation
                    (double patchedBytes, bool downloadHasErrors) = await patcherService.DownloadAndPatchFilesAsync(filesToDownload, fileList, cts);
                    bool success = !downloadHasErrors;
                    
                    // Delete files marked for deletion
                    if (success && fileList.deletes.Count > 0)
                    {
                        bool deleteHasErrors = await patcherService.DeleteFilesAsync(fileList, cts);
                        success = success && !deleteHasErrors;
                    }
                    
                    // Update patch status
                    UpdatePatchStatus(success);
                }
                else
                {
                    // No files to update
                    StatusLibrary.Log("All files are up to date!");
                    UpdatePatchStatus(true);
                }
            }
            catch (OperationCanceledException)
            {
                StatusLibrary.Log("Operation was cancelled.");
                UpdatePatchStatus(false);
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"Error during patching: {ex.Message}");
                UpdatePatchStatus(false);
            }
        }

        /// <summary>
        /// Updates the UI elements based on patch success or failure
        /// </summary>
        private void UpdatePatchStatus(bool success)
        {
            Status.IsPatching = false;
            isPatching = false;
            
            // Update UI buttons
            if (Status.HasUpdates)
            {
                // Show patch button for self-updates
                btnPatch.Visibility = Visibility.Visible;
                btnPlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Show play button when no updates needed
                btnPatch.Visibility = Visibility.Collapsed;
                btnPlay.Visibility = Visibility.Visible;
            }

            // Update button text and appearance
            btnPatch.Content = "PATCH";
            btnPatch.Tag = false;
            StatusLibrary.SetPatchState(false);
            
            // Initialize the optimizations panel
            InitializeOptimizationsPanel();
            
            // Auto-play if enabled and patch was successful
            if (Status.IsAutoPatch && Status.IsAutoPlay && success)
            {
                Task.Run(async () => {
                    await Task.Delay(1500);
                    Dispatcher.Invoke(() => {
                        BtnPlay_Click(null, null);
                    });
                });
            }
        }

        private void CheckPatchStatus()
        {
            try
            {
                string eqPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string eqExePath = Path.Combine(eqPath, "eqgame.exe");

                if (File.Exists(eqExePath))
                {
                    btn4GBPatch.IsEnabled = !Utilities.PEModifier.Is4GBPatchApplied(eqExePath);
                    if (!btn4GBPatch.IsEnabled)
                    {
                        btn4GBPatch.ToolTip = "4GB patch is already applied to eqgame.exe";
                    }
                }
                else
                {
                    btn4GBPatch.IsEnabled = true;
                    btn4GBPatch.ToolTip = "eqgame.exe not found - button will be enabled when game files are patched";
                }
            }
            catch (Exception ex)
            {
                btn4GBPatch.IsEnabled = false;
                btn4GBPatch.ToolTip = "Error checking patch status: " + ex.Message;
            }
        }

        private void InitializeOptimizationsPanel()
        {
            try
            {
                string eqPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string eqExePath = Path.Combine(eqPath, "eqgame.exe");

                if (File.Exists(eqExePath))
                {
                    btn4GBPatch.IsEnabled = !Utilities.PEModifier.Is4GBPatchApplied(eqExePath);
                    if (!btn4GBPatch.IsEnabled)
                    {
                        btn4GBPatch.ToolTip = "4GB patch is already applied to eqgame.exe";
                    }
                }
                else
                {
                    btn4GBPatch.IsEnabled = true;
                    btn4GBPatch.ToolTip = "eqgame.exe not found - button will be enabled when game files are patched";
                }
            }
            catch (Exception ex)
            {
                btn4GBPatch.IsEnabled = false;
                btn4GBPatch.ToolTip = "Error checking patch status: " + ex.Message;
            }
        }

        // Add the missing event handler for the main Changelog button
        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            // Assuming this button should also show the Patcher Changelog
            // If it's meant for a different changelog, adjust the call accordingly
            changelogService.ShowPatcherChangelog(this);
        }
    }
} // End of namespace THJPatcher

