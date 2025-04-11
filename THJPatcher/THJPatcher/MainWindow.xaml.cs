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
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Windows.Data;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace THJPatcher
{
    public class BooleanOrConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.OfType<bool>().Any(b => b);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ChangelogData
    {
        public string Status { get; set; }
        public bool Found { get; set; }
        public ChangelogInfo Changelog { get; set; }
    }

    public class ChangelogResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        public int Total { get; set; }
        public List<ChangelogInfo> Changelogs { get; set; }
    }

    public class ChangelogInfo
    {
        [JsonPropertyName("id")]
        public string Message_Id { get; set; }

        private string _rawContent;
        [JsonPropertyName("content")]
        public string Raw_Content
        {
            get => _rawContent;
            set
            {
                _rawContent = value;
                if (string.IsNullOrEmpty(_formattedContent))
                {
                    _formattedContent = FormatContent();
                }
            }
        }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        private string _formattedContent;
        public string Formatted_Content
        {
            get => _formattedContent ?? FormatContent();
            set => _formattedContent = value;
        }

        private string FormatContent()
        {
            if (string.IsNullOrEmpty(_rawContent)) return "";
            var date = Timestamp.ToString("MMMM dd, yyyy");
            return $"# {date}\n\n## {Author}\n\n{_rawContent.TrimStart('`').TrimEnd('`')}\n\n---";
        }
    }

    public class LoadingMessages
    {
        public List<string> Messages { get; set; }

        public static LoadingMessages CreateDefault()
        {
            return new LoadingMessages
            {

                Messages = new List<string>
                {
                    "Decreasing exp by 1.1%, how would you know?...",
                    "Decreasing enemy health to 0 while keeping yours above 0...",
                    "Cata is messing with the wires again....",
                    "Feeding halflings to ogres...",
                    "Casting lightning bolt...",
                    "I didn't ask how big the room was… I said I cast Fireball...",
                    "Increasing Froglok jumping by 3.8%...",
                    "Banning MQ2 users...",
                    "Nerfing Monk/Monk/Monk...",
                    "Reverting the bug fixes by pulling from upstream...",
                    "Feeding the server hamsters...",
                    "Bribing the RNG gods...",
                    "Aporia is watching you.....",
                    "Standby.....building a gazebo.....",
                    "Gnomes are not snacks, stop asking...",
                    "Buffing in the Plane of Water… just kidding, no one goes there...",
                    "Undercutting your trader by 1 copper...",
                    "Emergency patch: Aporia found an exploit. Cata is 'fixing' it. Drake is taking cover...",
                    "'Balancing' triple-class builds...",
                    "Adding more duct tape to the database—should be fine...",
                    "Server hamster demands a raise, Aporia Refused...",
                    "'Balancing' pet builds...",
                    "I Pity the Fool...",
                    "You will not evade me.....",
                    "You have ruined your own lands..... you will not ruin mine!....",
                    "Im winning at Fashion Quest...",
                    "Welcome to The Heroes Journey—where your class build is only limited by your imagination.",
                    "Three-class builds allow for endless customization—choose wisely, adventure boldly!",
                    "The Tribunal have ruled: Your Rogue/Warrior/Monk build is technically a crime.",
                    "Plane of Fire..... the BRD/WIZ/SK's new home.....",
                    "Zebuxoruk is confused by your triple-class build. Not as confused as we are, though...",
                    "Even the Gods are questioning your choice of PAL/SK/MNK. But hey, it's your journey...",
                    "Bristlebane is rewriting the loot tables… good luck!...",
                    "Entering Vex Thal… see you next year...",
                    "Heading into Plane of Time… don't worry, you will need plenty of it....",
                    "Aporia tried to nerf Bards… but they twisted out of it.",
                    "Xegony's winds are howling… or is that just the lag?...."
                }
            };
        }
    }

    public partial class MainWindow : Window
    {
        private readonly SolidColorBrush _redBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0));
        private readonly SolidColorBrush _defaultButtonBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 233, 164)); // #FBE9A4
        private bool isPatching = false;
        private bool isPatchCancelled = false;
        private bool isPendingPatch = false;
        private bool isNeedingSelfUpdate = false;
        private bool isLoading;
        private bool isAutoPatch = false;
        private bool isAutoPlay = false;
        private bool isDebugMode = false;
        private bool isSilentMode = false;
        private bool isAutoConfirm = false;
        private CancellationTokenSource cts;
        private Process process;
        private string myHash = "";
        private string patcherUrl;
        private string fileName;
        private string version;
        public string Version => version;
        private List<FileEntry> filesToDownload = new List<FileEntry>();

        // Server and file configuration
        private static string serverName;
        private static string filelistUrl;

        // Supported client versions
        public static List<VersionTypes> supportedClients = new List<VersionTypes> {
            VersionTypes.Rain_Of_Fear,
            VersionTypes.Rain_Of_Fear_2
        };

        private Dictionary<VersionTypes, ClientVersion> clientVersions = new Dictionary<VersionTypes, ClientVersion>();

        private List<ChangelogInfo> changelogs = new List<ChangelogInfo>();
        private string changelogContent = "";
        private readonly string changelogEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog/";
        private readonly string allChangelogsEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog?all=true";
        private readonly string patcherToken;
        private bool hasNewChangelogs = false;
        private ChangelogResponse changelogResponse;

        private bool autoScroll = true;

        private LoadingMessages loadingMessages;
        private Random random = new Random();

        private string cachedChangelogContent = null;
        private bool changelogNeedsUpdate = true;

        private LatestChangelogWindow _latestChangelogWindow;

        // Add a field to track initialization state
        private bool hasInitialized = false;

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

            // Parse command line arguments
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                var lowerArg = arg.ToLower();
                switch (lowerArg)
                {
                    case "--silent":
                    case "-silent":
                        isSilentMode = true;
                        break;
                    case "--confirm":
                    case "-confirm":
                        isAutoConfirm = true;
                        break;
                    case "--debug":
                    case "-debug":
                        isDebugMode = true;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] Debug mode enabled");
                        }
                        break;
                }
            }

            // If in silent mode, hide the window and ensure console output is visible
            if (isSilentMode)
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                Console.WriteLine("Starting THJ Patcher in silent mode...");
                Console.WriteLine("----------------------------------------");
            }

            // Initialize changelogs
            InitializeChangelogs();

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

            buildClientVersions();
        }

        private bool IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private void buildClientVersions()
        {
            clientVersions.Clear();
            clientVersions.Add(VersionTypes.Titanium, new ClientVersion("Titanium", "titanium"));
            clientVersions.Add(VersionTypes.Secrets_Of_Feydwer, new ClientVersion("Secrets Of Feydwer", "sof"));
            clientVersions.Add(VersionTypes.Seeds_Of_Destruction, new ClientVersion("Seeds of Destruction", "sod"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear, new ClientVersion("Rain of Fear", "rof"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear_2, new ClientVersion("Rain of Fear 2", "rof2"));
            clientVersions.Add(VersionTypes.Underfoot, new ClientVersion("Underfoot", "underfoot"));
            clientVersions.Add(VersionTypes.Broken_Mirror, new ClientVersion("Broken Mirror", "brokenmirror"));
        }

        private void detectClientVersion()
        {
            // We only support RoF2 now
            // No need to detect version
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
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OptimizationsButton_Click(object sender, RoutedEventArgs e)
        {
            logPanel.Visibility = Visibility.Collapsed;
            optimizationsPanel.Visibility = Visibility.Visible;

            InitializeOptimizationsPanel();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseOptimizations_Click(object sender, RoutedEventArgs e)
        {
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;
        }

        private async void Apply4GBPatch_Click(object sender, RoutedEventArgs e)
        {
            btn4GBPatch.IsEnabled = false;
            try
            {
                string eqPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string eqExePath = Path.Combine(eqPath, "eqgame.exe");

                if (!File.Exists(eqExePath))
                {
                    MessageBox.Show("Could not find eqgame.exe", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // First check if patch is already applied
                bool isPatchApplied = await Task.Run(() => Utilities.PEModifier.Is4GBPatchApplied(eqExePath));
                if (isPatchApplied)
                {
                    MessageBox.Show("4GB patch is already applied to eqgame.exe", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    btn4GBPatch.ToolTip = "4GB patch is already applied to eqgame.exe";
                    return;
                }

                // Show confirmation dialog
                string message = "Are you sure you want to apply the 4GB patch? This will enable EverQuest to use up to 4GB of RAM on 64-bit systems and will create a backup of the original file.";
                if (!CustomMessageBox.Show(message))
                {
                    return;
                }

                // Apply the patch
                bool success = await Task.Run(() => Utilities.PEModifier.Apply4GBPatch(eqExePath));
                if (success)
                {

                    bool verifyPatch = await Task.Run(() => Utilities.PEModifier.Is4GBPatchApplied(eqExePath));
                    if (verifyPatch)
                    {
                        MessageBox.Show("Successfully applied 4GB patch. A backup of the original file has been created as eqgame.exe.bak",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        btn4GBPatch.ToolTip = "4GB patch is already applied to eqgame.exe";
                    }
                    else
                    {
                        MessageBox.Show("Failed to verify 4GB patch application. Please try again.",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Failed to apply 4GB patch. The file may already be patched or is not compatible.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying 4GB patch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn4GBPatch.IsEnabled = true;
            }
        }

        private void FixUIScale_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqcfgPath = Path.Combine(eqPath, "eqclient.ini");

                if (File.Exists(eqcfgPath))
                {
                    var lines = File.ReadAllLines(eqcfgPath);
                    bool found = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("UIScale="))
                        {
                            lines[i] = "UIScale=1.0";
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Array.Resize(ref lines, lines.Length + 1);
                        lines[lines.Length - 1] = "UIScale=1.0";
                    }

                    File.WriteAllLines(eqcfgPath, lines);
                    MessageBox.Show("UI Scale has been set to 1.0", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error fixing UI scale: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OptimizeGraphics_Click(object sender, RoutedEventArgs e)
        {
            btnOptimizeGraphics.IsEnabled = false;
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqcfgPath = Path.Combine(eqPath, "eqclient.ini");

                if (File.Exists(eqcfgPath))
                {
                    await Task.Run(() =>
                    {
                        var lines = File.ReadAllLines(eqcfgPath);
                        var optimizations = new Dictionary<string, string>
                        {
                            { "MaxFPS=", "MaxFPS=60" },
                            { "MaxBackgroundFPS=", "MaxBackgroundFPS=60" },
                            { "WaterReflections=", "WaterReflections=0" },
                            { "ParticleDensity=", "ParticleDensity=1000" },
                            { "SpellParticleDensity=", "SpellParticleDensity=1000" },
                            { "EnableVSync=", "EnableVSync=0" }
                        };

                        bool[] found = new bool[optimizations.Count];
                        for (int i = 0; i < lines.Length; i++)
                        {
                            foreach (var opt in optimizations)
                            {
                                if (lines[i].StartsWith(opt.Key))
                                {
                                    lines[i] = opt.Value;
                                    found[Array.IndexOf(optimizations.Keys.ToArray(), opt.Key)] = true;
                                }
                            }
                        }

                        var notFound = optimizations.Where((kvp, index) => !found[index]);
                        if (notFound.Any())
                        {
                            Array.Resize(ref lines, lines.Length + notFound.Count());
                            int index = lines.Length - notFound.Count();
                            foreach (var opt in notFound)
                            {
                                lines[index++] = opt.Value;
                            }
                        }

                        File.WriteAllLines(eqcfgPath, lines);
                    });

                    MessageBox.Show("Graphics settings have been optimized", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error optimizing graphics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string[] cacheDirs = { "dbstr", "maps" };

                await Task.Run(() =>
                {
                    foreach (string dir in cacheDirs)
                    {
                        string path = Path.Combine(eqPath, dir);
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                            Directory.CreateDirectory(path);
                        }
                    }
                });

                MessageBox.Show("Cache has been cleared", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string[] configFiles = { "eqclient.ini", "eqclient_local.ini" };

                await Task.Run(() =>
                {
                    foreach (string file in configFiles)
                    {
                        string path = Path.Combine(eqPath, file);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                });

                MessageBox.Show("Settings have been reset. They will be recreated when you next launch EverQuest.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            StatusLibrary.SubscribeLogAdd(new StatusLibrary.LogAddHandler((string message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLog.AppendText(message + Environment.NewLine);
                    txtLog.ScrollToEnd();
                    txtLog.CaretIndex = txtLog.Text.Length;
                });
            }));
            StatusLibrary.SubscribePatchState(new StatusLibrary.PatchStateHandler((bool isPatchGoing) =>
            {
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Content = isPatchGoing ? "CANCEL" : "PATCH";
                });
            }));

            // Load configuration first
            IniLibrary.Load();

            // Display a welcome message
            LoadLoadingMessages();
            string randomMessage = GetRandomLoadingMessage();
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
                // First check if we need to update the patcher
                StatusLibrary.Log("Checking for updates...");
                StatusLibrary.Log("Checking patcher version...");

                string url = $"{patcherUrl}{fileName}-hash.txt";
                try
                {
                    var data = await UtilityLibrary.Download(cts, url);
                    string response = System.Text.Encoding.Default.GetString(data).Trim().ToUpperInvariant();

                    if (!string.IsNullOrEmpty(response))
                    {
                        myHash = UtilityLibrary.GetMD5(System.Windows.Forms.Application.ExecutablePath).ToUpperInvariant();
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Remote hash: {response}");
                            StatusLibrary.Log($"[DEBUG] Local hash:  {myHash}");
                        }
                        if (response != myHash)
                        {
                            isNeedingSelfUpdate = true;
                            Dispatcher.Invoke(() =>
                            {
                                StatusLibrary.Log("Patcher update available! Click PATCH to begin.");
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
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"[Error] Failed to check patcher version: {ex.Message}");
                }
            }
            else
            {
                StatusLibrary.Log("[DEBUG] Debug mode enabled - skipping patcher self-update check");
            }

            // Only proceed with remaining initialization if no patcher update is needed
            await CompleteInitialization();
        }

        // Add a new method to complete the initialization process
        private async Task CompleteInitialization()
        {
            // Clear any existing download list to start fresh
            filesToDownload.Clear();

            // Check if changelog needs to be deleted
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
                        File.Delete(changelogYmlPath);
                        StatusLibrary.Log("Outdate Changelog file detected...Changeglog will be updated during this patch.");
                        needsReinitialization = true;
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[ERROR] Failed to delete changelog.yml: {ex.Message}");
                    }
                }

                if (File.Exists(changelogMdPath))
                {
                    try
                    {
                        File.Delete(changelogMdPath);
                        needsReinitialization = true;
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[ERROR] Failed to delete changelog.md: {ex.Message}");
                    }
                }

                // Set DeleteChangelog to false for future runs
                IniLibrary.instance.DeleteChangelog = "false";
                IniLibrary.Save();

                // Clear any cached changelog data
                if (needsReinitialization)
                {
                    changelogs.Clear();
                    changelogContent = "";
                    cachedChangelogContent = null;
                    changelogNeedsUpdate = true;
                    hasNewChangelogs = false;
                }
            }

            // Remove any conflicting files
            await RemoveConflictingFiles();

            // Check for pending dinput8.dll.new file
            await CheckForPendingDinput8();

            // Check server status
            await CheckServerStatus();

            // Initialize changelogs (this will handle redownloading if files were deleted)
            InitializeChangelogs();

            // Check for new changelogs
            await CheckChangelogAsync();

            // Load configuration
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;

            // Create DXVK configuration for Linux/Proton compatibility
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string dxvkPath = Path.Combine(eqPath, "dxvk.conf");
                string dxvkContent = "[heroesjourneyemu.exe]\nd3d9.shaderModel = 1";

                if (!File.Exists(dxvkPath))
                {
                    File.WriteAllText(dxvkPath, dxvkContent);
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[Error] Failed to create DXVK configuration: {ex.Message}");
            }

            // Run a quick file scan every time the patcher starts
            await RunFileIntegrityScanAsync();

            // If we're in auto-patch mode, start patching (but not for self-updates)
            if (isAutoPatch && !isNeedingSelfUpdate)
            {
                isPendingPatch = true;
                await Task.Delay(1000);
                await StartPatch();
            }

            isLoading = false;
        }

        private async Task ForceDinput8Check()
        {
            try
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Silently checking dinput8.dll for updates...");
                }

                // Get the path to dinput8.dll
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string dinput8Path = Path.Combine(eqPath, "dinput8.dll");

                // Download the filelist to get the latest dinput8.dll MD5
                string suffix = "rof";
                string primaryUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download";
                string webUrl = $"{primaryUrl}/filelist_{suffix}.yml";
                string filelistResponse = "";

                // Try to download the filelist with a timeout
                try
                {
                    using (var client = new HttpClient())
                    {
                        // Set a timeout of 5 seconds for the filelist download
                        client.Timeout = TimeSpan.FromSeconds(5);

                        // Download the filelist
                        var response = await client.GetAsync(webUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            await response.Content.ReadAsStringAsync();
                            // If successful, download the file using the regular method
                            filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");

                            if (isDebugMode)
                            {
                                StatusLibrary.Log("[DEBUG] Successfully downloaded filelist from primary URL");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Failed to download filelist from primary URL: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(filelistResponse))
                {
                    // Try fallback URL with timeout
                    string fallbackUrl = "https://patch.heroesjourneyemu.com";
                    webUrl = $"{fallbackUrl}/filelist_{suffix}.yml";

                    try
                    {
                        using (var client = new HttpClient())
                        {
                            // Set a timeout of 5 seconds for the fallback filelist download
                            client.Timeout = TimeSpan.FromSeconds(5);

                            // Download the filelist
                            var response = await client.GetAsync(webUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                await response.Content.ReadAsStringAsync();
                                // If successful, download the file using the regular method
                                filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");

                                if (isDebugMode)
                                {
                                    StatusLibrary.Log("[DEBUG] Successfully downloaded filelist from fallback URL");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Failed to download filelist from fallback URL: {ex.Message}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(filelistResponse))
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Could not download filelist to check dinput8.dll");
                    }
                    return;
                }

                // Parse the filelist
                FileList filelist = null;
                try
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    filelist = deserializer.Deserialize<FileList>(File.ReadAllText("filelist.yml"));

                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Successfully parsed filelist");
                    }
                }
                catch (Exception ex)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Failed to parse filelist: {ex.Message}");
                    }
                    return;
                }

                if (filelist == null || filelist.downloads == null)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Invalid filelist format");
                    }
                    return;
                }

                // Find dinput8.dll in the filelist
                FileEntry dinput8Entry = filelist.downloads.FirstOrDefault(e => e.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase));

                if (dinput8Entry == null)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Could not find dinput8.dll in filelist");
                    }
                    return;
                }

                // Check if dinput8.dll exists locally
                bool needsUpdate = false;
                if (!File.Exists(dinput8Path))
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] dinput8.dll not found, will be downloaded");
                    }
                    needsUpdate = true;
                }
                else
                {
                    // Check MD5 hash
                    string localMd5 = await Task.Run(() => UtilityLibrary.GetMD5(dinput8Path));
                    if (localMd5.ToUpper() != dinput8Entry.md5.ToUpper())
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] dinput8.dll is outdated, will be updated");
                            StatusLibrary.Log($"[DEBUG] Current MD5: {localMd5.ToUpper()}");
                            StatusLibrary.Log($"[DEBUG] Expected MD5: {dinput8Entry.md5.ToUpper()}");
                        }
                        needsUpdate = true;
                    }
                    else if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] dinput8.dll is up to date");
                    }
                }

                if (needsUpdate)
                {
                    // Add dinput8.dll to the list of files that need updating
                    if (!filesToDownload.Any(e => e.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        filesToDownload.Add(dinput8Entry);

                        if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] Added dinput8.dll to update queue");
                        }

                        // Trigger a full integrity scan silently
                        await RunFileIntegrityScanAsync(true);
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Failed to check dinput8.dll for updates: {ex.Message}");
                }
            }
        }

        private async Task RemoveConflictingFiles()
        {
            try
            {
                // Get the game directory path
                string gamePath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);

                // List of files that can cause conflicts
                string[] conflictingFiles = {
                    "MaterialDesignThemes.Wpf.dll",
                    "Microsoft.Xaml.Behaviors.dll"
                };

                foreach (string file in conflictingFiles)
                {
                    string filePath = Path.Combine(gamePath, file);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Removing conflicting file: {file}");
                            }

                            File.Delete(filePath);
                            StatusLibrary.Log($"Removed conflicting file: {file}");
                        }
                        catch (Exception ex)
                        {
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Failed to remove {file}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error removing conflicting files: {ex.Message}");
                }
            }
        }

        private async Task CheckForPendingDinput8()
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string dinput8Path = Path.Combine(eqPath, "dinput8.dll");
                string dinput8NewPath = Path.Combine(eqPath, "dinput8.dll.new");

                // Check if there's a pending dinput8.dll.new file
                if (File.Exists(dinput8NewPath))
                {
                    StatusLibrary.Log("Found pending dinput8.dll.new file from previous update");

                    try
                    {
                        // Try to replace the file directly
                        if (File.Exists(dinput8Path))
                        {
                            // Try to check if the file is in use
                            bool isFileInUse = false;
                            try
                            {
                                using (FileStream fs = File.Open(dinput8Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                {
                                    // If we get here, the file isn't locked
                                    fs.Close();
                                }
                            }
                            catch (IOException)
                            {
                                isFileInUse = true;
                            }

                            if (!isFileInUse)
                            {
                                StatusLibrary.Log("Attempting to replace dinput8.dll with pending update");
                                File.Delete(dinput8Path);
                                File.Move(dinput8NewPath, dinput8Path);
                                StatusLibrary.Log("Successfully updated dinput8.dll");
                                return;
                            }
                        }
                        else
                        {
                            // dinput8.dll doesn't exist, just rename the .new file
                            StatusLibrary.Log("Installing dinput8.dll from pending update");
                            File.Move(dinput8NewPath, dinput8Path);
                            StatusLibrary.Log("Successfully installed dinput8.dll");
                            return;
                        }

                        // If we get here, the file is in use or we couldn't replace it
                        if (IsAdministrator())
                        {
                            StatusLibrary.Log("Attempting to schedule dinput8.dll replacement on next reboot");
                            if (UtilityLibrary.ScheduleFileOperation(dinput8NewPath, dinput8Path,
                                UtilityLibrary.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT | UtilityLibrary.MoveFileFlags.MOVEFILE_REPLACE_EXISTING))
                            {
                                StatusLibrary.Log("dinput8.dll will be updated on next reboot");
                            }
                            else
                            {
                                StatusLibrary.Log("[Warning] Failed to schedule dinput8.dll replacement");
                            }
                        }
                        else if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] Administrator privileges required to update dinput8.dll");
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[Warning] Failed to handle pending dinput8.dll update: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[Warning] Error checking for pending dinput8.dll update: {ex.Message}");
            }
        }

        private async Task CheckServerStatus()
        {
            try
            {
                // Get token from Constants
                string token = Constants.PATCHER_TOKEN;
                if (string.IsNullOrEmpty(token))
                {
                    StatusLibrary.Log("[ERROR] Unable to authenticate with server status API");
                    StatusLibrary.Log("Continuing....");
                    return;
                }

                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Checking server status...");
                }

                // Retry settings
                int maxRetries = 3;
                int currentRetry = 0;
                bool success = false;
                Exception lastException = null;

                while (currentRetry < maxRetries && !success)
                {
                    try
                    {
                        using (var client = new HttpClient(new HttpClientHandler
                        {
                            // Set security protocol explicitly to support all TLS versions
                            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                        }))
                        {
                            // Set a timeout of 3 seconds for server status API requests
                            client.Timeout = TimeSpan.FromSeconds(3);
                            client.DefaultRequestHeaders.Add("x-patcher-token", token);

                            // Server status API endpoints - try primary and fallback if needed
                            string primaryApiUrl = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/serverstatus";
                            string fallbackApiUrl = "http://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/serverstatus";

                            // First try with HTTPS
                            string apiUrl = currentRetry == 0 ? primaryApiUrl : fallbackApiUrl;
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Trying server status API (attempt {currentRetry + 1}): {apiUrl}");
                            }

                            // Get response as string first to examine if there are issues
                            var responseString = await client.GetStringAsync(apiUrl);

                            if (isDebugMode && currentRetry > 0)
                            {
                                StatusLibrary.Log($"[DEBUG] Succeeded with {(apiUrl.StartsWith("https") ? "HTTPS" : "HTTP")} on retry {currentRetry + 1}");
                            }

                            // Check if response starts with unexpected characters (like HTML)
                            if (responseString.StartsWith("<"))
                            {
                                throw new JsonException("Response appears to be HTML instead of JSON");
                            }

                            var status = JsonSerializer.Deserialize<ServerStatus>(responseString);

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Server status response successful");
                            }

                            if (status?.Found == true && status.Server != null)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    txtPlayerCount.Inlines.Clear();
                                    txtPlayerCount.Inlines.Add(new Run("Players Online:"));
                                    txtPlayerCount.Inlines.Add(new LineBreak());
                                    txtPlayerCount.Inlines.Add(new Run(status.Server.PlayersOnline.ToString()) { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(212, 184, 106)) });
                                });
                            }

                            // Now try the EXP bonus API using the same protocol that worked
                            if (isDebugMode)
                            {
                                StatusLibrary.Log("[DEBUG] Checking exp bonus...");
                            }

                            string expApiUrl = apiUrl.Replace("serverstatus", "expbonus");
                            var expResponseString = await client.GetStringAsync(expApiUrl);

                            // Check if response starts with unexpected characters
                            if (expResponseString.StartsWith("<"))
                            {
                                throw new JsonException("EXP response appears to be HTML instead of JSON");
                            }

                            var expStatus = JsonSerializer.Deserialize<ExpBonusStatus>(expResponseString);

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Exp bonus response successful");
                            }

                            if (expStatus?.Status == "success" && expStatus.Found)
                            {
                                // Extract the percentage from the exp_boost string
                                var percentage = expStatus.ExpBoost.Split('%')[0].Split(':')[1].Trim();
                                // If the percentage is "Off", show 0%
                                if (percentage.Equals("Off", StringComparison.OrdinalIgnoreCase))
                                {
                                    percentage = "0";
                                }
                                Dispatcher.Invoke(() =>
                                {
                                    txtExpBonus.Inlines.Clear();
                                    txtExpBonus.Inlines.Add(new Run("Experience:"));
                                    txtExpBonus.Inlines.Add(new LineBreak());
                                    txtExpBonus.Inlines.Add(new Run($"{percentage}% Bonus") { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(212, 184, 106)) });
                                });
                            }

                            // If we got here, everything succeeded
                            success = true;
                        }
                    }
                    catch (JsonException ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Server status JSON parse error: {ex.Message}");
                            // If we have an inner exception, log it as well
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                            }
                        }
                        // This is likely a response format issue, so retrying won't help
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Server status HTTP request error: {ex.Message}");
                            // Log inner exception which often contains SSL details
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");

                                // If it's an SSL error, log more details
                                if (ex.InnerException is System.Security.Authentication.AuthenticationException)
                                {
                                    StatusLibrary.Log($"[DEBUG] SSL error: {ex.InnerException.Message}");
                                }
                            }
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Handle timeout specifically
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Server status API request timed out on attempt {currentRetry + 1}");
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Server status check error: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                            }
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                }

                // Log if all retries failed but don't show errors to users
                if (!success && isDebugMode && lastException != null)
                {
                    StatusLibrary.Log($"[DEBUG] All server status check attempts failed. Last error: {lastException.Message}");

                    // Add a troubleshooting hint if SSL errors are detected
                    if (lastException is HttpRequestException && lastException.InnerException is System.Security.Authentication.AuthenticationException)
                    {
                        StatusLibrary.Log("[DEBUG] SSL connection issues detected. This may be due to outdated TLS settings, firewall, or antivirus software.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Unhandled error in server status check: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                    }
                }
                // Silently fail for end users - we don't want to show errors for this
            }
        }

        private async Task StartPatch()
        {
            if (isPatching || isPatchCancelled) return;

            isPatching = true;
            isPatchCancelled = false;

            // Hide patch button and show play button when patch starts
            Dispatcher.Invoke(() =>
            {
                btnPatch.Visibility = Visibility.Collapsed;
                btnPlay.Visibility = Visibility.Visible;
                btnPatch.IsEnabled = false;
                btnPlay.IsEnabled = false;
                chkAutoPatch.IsEnabled = false;
                chkAutoPlay.IsEnabled = false;
            });

            try
            {
                await AsyncPatch();

                // After successful patch, if in silent mode, start the game
                if (isSilentMode && isAutoConfirm)
                {
                    await Task.Delay(2000); // Give a small delay to show completion
                    BtnPlay_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[Error] Failed to patch: {ex.Message}");
                if (!isSilentMode)
                {
                    MessageBox.Show($"Failed to patch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                isPatching = false;
                Dispatcher.Invoke(() =>
                {
                    btnPatch.IsEnabled = true;
                    btnPlay.IsEnabled = true;
                    chkAutoPatch.IsEnabled = true;
                    chkAutoPlay.IsEnabled = true;
                });
            }
        }

        private async Task AsyncPatch()
        {
            Stopwatch start = Stopwatch.StartNew();
            StatusLibrary.Log($"Patching with patcher version {version}...");
            StatusLibrary.SetProgress(0);

            // Handle self-update first if needed and not in debug mode
            if (!isDebugMode && myHash != "" && isNeedingSelfUpdate)
            {
                StatusLibrary.Log("Downloading patcher update...");
                string url = $"{patcherUrl}/{fileName}.exe";
                try
                {
                    var data = await Task.Run(async () => await UtilityLibrary.Download(cts, url));
                    string localExePath = Process.GetCurrentProcess().MainModule.FileName;
                    string localExeName = Path.GetFileName(localExePath);

                    // Create a temporary file for the new patcher
                    string tempExePath = Path.Combine(Path.GetDirectoryName(localExePath), "temp_" + localExeName);

                    // Write the new patcher to the temp file
                    await Task.Run(async () =>
                    {
                        using (var w = File.Create(tempExePath))
                        {
                            await w.WriteAsync(data, 0, data.Length, cts.Token);
                        }
                    });

                    // Get the expected MD5 from the server
                    string hashUrl = $"{patcherUrl}{fileName}-hash.txt";
                    var hashData = await UtilityLibrary.Download(cts, hashUrl);
                    string expectedHash = System.Text.Encoding.Default.GetString(hashData).Trim().ToUpperInvariant();

                    // Verify the new patcher's MD5 against the expected hash
                    var newHash = await Task.Run(() => UtilityLibrary.GetMD5(tempExePath));
                    if (newHash.ToUpper() != expectedHash)
                    {
                        StatusLibrary.Log($"[Error] Downloaded patcher MD5 mismatch. Expected: {expectedHash}, Got: {newHash.ToUpper()}");
                        await Task.Run(() => File.Delete(tempExePath));
                        return;
                    }

                    // If we get here, the new patcher is valid
                    // Delete the old backup if it exists
                    if (File.Exists(localExePath + ".old"))
                    {
                        await Task.Run(() => File.Delete(localExePath + ".old"));
                    }

                    // Move the current patcher to .old
                    await Task.Run(() => File.Move(localExePath, localExePath + ".old"));

                    // Move the temp file to the final location
                    await Task.Run(() => File.Move(tempExePath, localExePath));

                    // Check for a patcher_changelog.md file in the local directory
                    string localChangelogPath = Path.Combine(Path.GetDirectoryName(localExePath), "patcher_changelog.md");

                    // Try to download the latest patcher changelog
                    try
                    {
                        string changelogUrl = $"{patcherUrl}/patcher_changelog.md";
                        var changelogData = await UtilityLibrary.Download(cts, changelogUrl);
                        if (changelogData != null && changelogData.Length > 0)
                        {
                            string changelogContent = System.Text.Encoding.UTF8.GetString(changelogData);
                            await Task.Run(() => File.WriteAllText(localChangelogPath, changelogContent));

                            if (isDebugMode)
                            {
                                StatusLibrary.Log("[DEBUG] Downloaded updated patcher_changelog.md file");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Failed to download patcher_changelog.md: {ex.Message}");
                        }
                        // This is not critical, so we continue with the update
                    }

                    StatusLibrary.Log("Patcher update complete!");
                    StatusLibrary.Log("Restarting patcher to apply update...");

                    // Hide the play button and show a message
                    Dispatcher.Invoke(() =>
                    {
                        btnPlay.Visibility = Visibility.Collapsed;
                        btnPatch.Visibility = Visibility.Visible;
                        btnPatch.Content = "RESTARTING...";
                        btnPatch.IsEnabled = false;
                    });

                    // Give the user time to read the message
                    await Task.Delay(2000);

                    // Start the new patcher with the same arguments
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = localExePath,
                        UseShellExecute = true,
                        Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
                    });

                    // Close the current patcher
                    Environment.Exit(0);
                }
                catch (Exception e)
                {
                    StatusLibrary.Log($"Patcher update failed {url}: {e.Message}");
                    isNeedingSelfUpdate = false;
                    return;
                }
            }

            if (isPatchCancelled)
            {
                StatusLibrary.Log("Patching cancelled.");
                return;
            }

            // Force check for dinput8.dll updates
            await ForceDinput8Check();

            // Get the client version suffix
            string suffix = "rof"; // Since we're only supporting RoF/RoF2

            // Download the filelist
            string webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
            string filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
            if (filelistResponse != "")
            {
                StatusLibrary.Log($"Failed to fetch filelist from {webUrl}: {filelistResponse}");
                return;
            }

            // Parse the filelist
            FileList filelist;
            using (var input = await Task.Run(() => File.OpenText($"{Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)}\\filelist.yml")))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = await Task.Run(() => deserializerBuilder.Deserialize<FileList>(input));
            }

            // Handle delete.txt if it exists
            string deleteUrl = $"{filelist.downloadprefix}delete.txt";
            try
            {
                var deleteData = await UtilityLibrary.Download(cts, deleteUrl);
                if (deleteData != null && deleteData.Length > 0)
                {
                    StatusLibrary.Log($"Checking for outdated files...");
                    string deleteContent = System.Text.Encoding.UTF8.GetString(deleteData);
                    var filesToDelete = deleteContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    if (filelist.deletes == null)
                        filelist.deletes = new List<FileEntry>();

                    foreach (var file in filesToDelete)
                    {
                        filelist.deletes.Add(new FileEntry { name = file.Trim() });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[Warning] Failed to process delete.txt: {ex.Message}");
            }

            // Only scan for new files if we haven't already identified files to download
            if (filesToDownload.Count == 0)
            {
                // Calculate total files to check
                int totalFilesCount = filelist.downloads.Count;
                int checkedFiles = 0;

                // First scan - check all files
                StatusLibrary.Log("Scanning files...");
                foreach (var entry in filelist.downloads)
                {
                    if (isPatchCancelled)
                    {
                        StatusLibrary.Log("Scanning cancelled.");
                        return;
                    }

                    StatusLibrary.SetProgress((int)((double)checkedFiles / totalFilesCount * 10000));
                    checkedFiles++;

                    // Skip heroesjourneyemu.exe as it's the patcher itself
                    if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        StatusLibrary.Log($"[Warning] Path {entry.name} might be outside of your EverQuest directory.");
                        continue;
                    }

                    bool needsDownload = false;

                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        StatusLibrary.Log($"Missing file detected: {entry.name}");
                        needsDownload = true;
                    }
                    else
                    {
                        var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            StatusLibrary.Log($"Modified file detected: {entry.name}");
                            needsDownload = true;
                        }
                    }

                    if (needsDownload)
                    {
                        filesToDownload.Add(entry);
                    }
                }
            }
            else
            {
                StatusLibrary.Log($"Using {filesToDownload.Count} previously identified files that need updating.");
            }

            // Calculate total patch size for downloads
            double totalBytes = await Task.Run(() =>
            {
                double total = 0;
                foreach (var entry in filesToDownload)
                {
                    total += entry.size;
                }
                return total == 0 ? 1 : total;
            });

            double currentBytes = 0; // Start at 0, not 1
            double patchedBytes = 0;
            bool hasErrors = false;

            // If no files need downloading after all, we're done
            if (filesToDownload.Count == 0)
            {
                StatusLibrary.Log("All files are up to date.");
                StatusLibrary.SetProgress(10000);
                return;
            }

            // Group files by type for improved UI feedback
            var mapFiles = filesToDownload.Where(f =>
                f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

            var dllFiles = filesToDownload.Where(f =>
                f.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

            var otherFiles = filesToDownload.Except(mapFiles).Except(dllFiles).ToList();

            // Log download counts by type
            if (mapFiles.Any())
                StatusLibrary.Log($"Downloading {mapFiles.Count} map files...");
            if (dllFiles.Any())
                StatusLibrary.Log($"Downloading {dllFiles.Count} DLL files...");
            if (otherFiles.Any())
                StatusLibrary.Log($"Downloading {otherFiles.Count} other game files...");

            StatusLibrary.Log($"Found {filesToDownload.Count} files to update.");
            await Task.Delay(500); // Shorter pause to show the message

            // Download and patch files
            if (!filelist.downloadprefix.EndsWith("/")) filelist.downloadprefix += "/";

            // Process files in a specific order: DLLs first, then other files, maps last
            var orderedFiles = new List<FileEntry>();
            orderedFiles.AddRange(dllFiles);
            orderedFiles.AddRange(otherFiles);
            orderedFiles.AddRange(mapFiles);

            int processedFiles = 0;
            int totalFiles = filesToDownload.Count;
            int loggedMapFiles = 0;
            const int maxLoggedMapFiles = 20; // Limit map file logging

            foreach (var entry in orderedFiles)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                processedFiles++;

                // Update progress bar with both file count and byte count for better feedback
                double fileProgress = (double)processedFiles / totalFiles;
                double byteProgress = currentBytes / totalBytes;
                double combinedProgress = (fileProgress + byteProgress) / 2.0 * 10000;

                // Ensure progress is shown throughout the operation
                StatusLibrary.SetProgress((int)combinedProgress);

                // For map files, limit the log output
                bool isMapFile = entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                bool shouldLogFile = !isMapFile || (isMapFile && loggedMapFiles < maxLoggedMapFiles);

                var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");

                // Create directory if it doesn't exist
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Special handling for dinput8.dll and other DLLs
                bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                bool isDll = entry.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                bool isFileInUse = false;

                // Always check if dinput8.dll exists first
                if (isDinput8 && !File.Exists(path))
                {
                    StatusLibrary.Log($"[Info] dinput8.dll not found, will be downloaded");
                }

                // Check if file is in use for DLLs
                if (isDll)
                {
                    try
                    {
                        // Try to open the file for writing to check if it's locked
                        if (File.Exists(path))
                        {
                            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                // If we get here, the file isn't locked
                                fs.Close();
                            }
                        }
                    }
                    catch (IOException)
                    {
                        isFileInUse = true;
                        StatusLibrary.Log($"[Info] {entry.name} is currently in use");
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[Warning] Error checking if {entry.name} is in use: {ex.Message}");
                    }
                }

                // Special handling for dinput8.dll - always try to update it
                if (isDinput8 || (isDll && isFileInUse))
                {
                    // Special handling for dinput8.dll and locked DLLs - force update with a backup and replace approach
                    try
                    {
                        StatusLibrary.Log($"[Info] Using special handling for {entry.name}");

                        // Create a temp backup path
                        string tempPath = path + ".new";

                        // Download to the temp file
                        StatusLibrary.Log($"[Info] Downloading {entry.name} to temporary file");
                        string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                        string response = await UtilityLibrary.DownloadFile(cts, backupUrl, entry.name + ".new");

                        if (response != "")
                        {
                            StatusLibrary.Log($"[Info] Trying alternate download source for {entry.name}");
                            string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                            response = await UtilityLibrary.DownloadFile(cts, url, entry.name + ".new");
                            if (response != "")
                            {
                                StatusLibrary.Log($"[Error] Failed to download {entry.name} to temp file: {response}");
                                continue;
                            }
                        }

                        // Verify the temp file exists
                        if (!File.Exists(tempPath))
                        {
                            StatusLibrary.Log($"[Error] Failed to create temp file for {entry.name}");
                            continue;
                        }

                        // Verify the MD5 of the downloaded file
                        var downloadedMd5 = await Task.Run(() => UtilityLibrary.GetMD5(tempPath));
                        StatusLibrary.Log($"[Info] Downloaded {entry.name} MD5: {downloadedMd5.ToUpper()}");
                        StatusLibrary.Log($"[Info] Expected {entry.name} MD5: {entry.md5.ToUpper()}");

                        if (downloadedMd5.ToUpper() != entry.md5.ToUpper())
                        {
                            StatusLibrary.Log($"[Warning] MD5 mismatch for downloaded {entry.name}");
                        }

                        // If the file is not in use, try to replace it directly
                        if (!isFileInUse)
                        {
                            try
                            {
                                StatusLibrary.Log($"[Info] Attempting direct replacement of {entry.name}");
                                if (File.Exists(path))
                                {
                                    File.Delete(path);
                                }
                                File.Move(tempPath, path);
                                StatusLibrary.Log($"Successfully updated {entry.name}");
                                currentBytes += entry.size;
                                patchedBytes += entry.size;
                                continue;
                            }
                            catch (Exception ex)
                            {
                                StatusLibrary.Log($"[Warning] Direct replacement failed: {ex.Message}");
                                // Fall through to the other methods
                                isFileInUse = true; // Treat as in-use for the next steps
                            }
                        }

                        // Schedule the file to be replaced on next reboot if it's in use
                        if (isFileInUse && IsAdministrator())
                        {
                            try
                            {
                                StatusLibrary.Log($"[Info] Attempting to schedule {entry.name} replacement on reboot");
                                // Try Windows' MoveFileEx API to replace on reboot
                                if (UtilityLibrary.ScheduleFileOperation(tempPath, path, UtilityLibrary.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT | UtilityLibrary.MoveFileFlags.MOVEFILE_REPLACE_EXISTING))
                                {
                                    StatusLibrary.Log($"{entry.name} will be updated on next reboot");
                                    currentBytes += entry.size;
                                    patchedBytes += entry.size;
                                    continue;
                                }
                                else
                                {
                                    StatusLibrary.Log($"[Warning] Failed to schedule {entry.name} replacement");
                                }
                            }
                            catch (Exception ex)
                            {
                                StatusLibrary.Log($"[Warning] Could not schedule {entry.name} replacement: {ex.Message}");
                            }
                        }

                        // If MoveFileEx fails or we're not admin, try a more aggressive approach
                        try
                        {
                            StatusLibrary.Log($"[Info] Attempting aggressive replacement of {entry.name}");
                            File.Delete(path);
                            File.Move(tempPath, path);
                            StatusLibrary.Log($"Successfully updated {entry.name}");
                            currentBytes += entry.size;
                            patchedBytes += entry.size;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            StatusLibrary.Log($"[Warning] Could not force update {entry.name}: {ex.Message}");
                            if (File.Exists(tempPath))
                            {
                                // Keep the temp file for next run
                                StatusLibrary.Log($"Keeping {entry.name}.new for next run");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[Warning] Special handling for {entry.name} failed: {ex.Message}");
                    }

                    if (isFileInUse)
                    {
                        StatusLibrary.Log($"[Warning] Skipping {entry.name} - file is currently in use");
                        continue;
                    }
                }

                bool downloadSuccess = false;
                int retryCount = 0;
                const int maxRetries = 3;

                // For map files, only update UI after every few files to avoid UI freeze
                if (isMapFile)
                {
                    if (loggedMapFiles == 0)
                    {
                        StatusLibrary.Log("Downloading map files...");
                    }

                    loggedMapFiles++;

                    // Only update UI for some map files to reduce UI overhead
                    if (loggedMapFiles > maxLoggedMapFiles && loggedMapFiles % 10 != 0)
                    {
                        shouldLogFile = false;
                    }

                    // Force UI update every 10 map files to keep progress visible
                    if (loggedMapFiles % 10 == 0)
                    {
                        // Force UI update by dispatching to UI thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            progressBar.Value = combinedProgress / 100;
                            // Update TextBox
                            if (autoScroll)
                            {
                                txtLog.ScrollToEnd();
                            }
                        });

                        // Short delay to let UI catch up
                        await Task.Delay(1);
                    }
                }

                while (!downloadSuccess && retryCount < maxRetries)
                {
                    // Try backup URL first since we know files exist there
                    string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                    string response = await UtilityLibrary.DownloadFile(cts, backupUrl, entry.name);

                    // If backup fails, try primary URL
                    if (response != "")
                    {
                        string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                        response = await UtilityLibrary.DownloadFile(cts, url, entry.name);
                        if (response != "")
                        {
                            if (shouldLogFile)
                            {
                                StatusLibrary.Log($"Failed to download {entry.name} ({generateSize(entry.size)}): {response}");
                            }
                            retryCount++;
                            continue;
                        }
                    }

                    // Verify the downloaded file's MD5
                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        if (shouldLogFile)
                        {
                            StatusLibrary.Log($"[Error] Failed to create file {entry.name}");
                        }
                        retryCount++;
                        continue;
                    }

                    var downloadedMd5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                    if (downloadedMd5.ToUpper() != entry.md5.ToUpper())
                    {
                        if (shouldLogFile)
                        {
                            StatusLibrary.Log($"[Warning] MD5 mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {downloadedMd5.ToUpper()}");
                        }
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            if (shouldLogFile)
                            {
                                StatusLibrary.Log($"Retrying download of {entry.name} (attempt {retryCount + 1}/{maxRetries})...");
                            }
                            await Task.Delay(1000); // Wait a bit before retrying
                            continue;
                        }
                        hasErrors = true;
                        break;
                    }

                    downloadSuccess = true;
                    if (shouldLogFile)
                    {
                        StatusLibrary.Log($"{entry.name} ({generateSize(entry.size)})");
                    }

                    // Every 20 files for non-map files, provide a summary
                    if (!isMapFile && processedFiles % 20 == 0)
                    {
                        StatusLibrary.Log($"Progress: {processedFiles}/{totalFiles} files processed ({(int)(processedFiles * 100.0 / totalFiles)}%)");
                    }

                    // For map files, show a summary every 50 files
                    if (isMapFile && loggedMapFiles % 50 == 0)
                    {
                        StatusLibrary.Log($"Downloaded {loggedMapFiles} map files so far");

                        // Make sure UI is updated
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (autoScroll)
                            {
                                txtLog.ScrollToEnd();
                            }
                        });
                    }

                    currentBytes += entry.size;
                    patchedBytes += entry.size;
                }

                if (!downloadSuccess)
                {
                    hasErrors = true;
                    if (shouldLogFile)
                    {
                        StatusLibrary.Log($"[Error] Failed to download and verify {entry.name} after {maxRetries} attempts");
                    }
                }

                // Periodically update UI thread to keep it responsive
                if (processedFiles % 20 == 0)
                {
                    await Task.Delay(1); // Small delay to allow UI to process
                }
            }

            // After downloading map files, show a summary
            if (loggedMapFiles > maxLoggedMapFiles)
            {
                StatusLibrary.Log($"Downloaded a total of {loggedMapFiles} map files");
            }

            // Handle file deletions
            if (filelist.deletes != null && filelist.deletes.Count > 0)
            {
                StatusLibrary.Log($"Processing {filelist.deletes.Count} file deletion(s)...");
                foreach (var entry in filelist.deletes)
                {
                    if (isPatchCancelled)
                    {
                        StatusLibrary.Log("Patching cancelled.");
                        return;
                    }

                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        StatusLibrary.Log($"[Warning] Path {entry.name} might be outside your EverQuest directory. Skipping deletion.");
                        continue;
                    }

                    if (await Task.Run(() => File.Exists(path)))
                    {
                        try
                        {
                            await Task.Run(() => File.Delete(path));
                            StatusLibrary.Log($"Deleted {entry.name}");
                        }
                        catch (Exception ex)
                        {
                            StatusLibrary.Log($"[Warning] Failed to delete {entry.name}: {ex.Message}");
                            hasErrors = true;
                        }
                    }
                }
            }

            // Final progress update
            StatusLibrary.SetProgress(10000);

            // Make sure the UI is updated
            await Dispatcher.InvokeAsync(() =>
            {
                if (autoScroll)
                {
                    txtLog.ScrollToEnd();
                }
            });

            // Update LastPatchedVersion and save configuration
            if (hasErrors)
            {
                StatusLibrary.Log("[Error] Patch completed with errors. Some files may not have been updated correctly.");
                if (!isSilentMode)
                {
                    MessageBox.Show("The patch completed with errors. Some files may not have been updated correctly. Please try running the patcher again.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (patchedBytes == 0)
            {
                string version = filelist.version;
                if (version.Length >= 8)
                {
                    version = version.Substring(0, 8);
                }
                StatusLibrary.Log($"Up to date with patch {version}.");
                IniLibrary.instance.LastPatchedVersion = filelist.version;
                await Task.Run(() => IniLibrary.Save());
                return;
            }

            string elapsed = start.Elapsed.ToString("ss\\.ff");
            StatusLibrary.Log($"Complete! Patched {generateSize(patchedBytes)} in {elapsed} seconds. Press Play to begin.");
            IniLibrary.instance.LastPatchedVersion = filelist.version;
            IniLibrary.instance.Version = version;
            await Task.Run(() => IniLibrary.Save());
        }

        private string generateSize(double size)
        {
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} bytes";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} KB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} MB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} GB";
            }

            return $"{Math.Round(size, 2)} TB";
        }

        private void StatusLibrary_ProgressChanged(int progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Convert from 0-10000 range to 0-100 range
                int displayProgress = progress / 100;
                progressBar.Value = displayProgress;
                // Use the actual progress bar value for the text
                string progressText = $"{(int)progressBar.Value}%";
                txtProgress.Text = progressText;

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

        private void StatusLibrary_LogAdded(string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Append the new message
                txtLog.AppendText(message + Environment.NewLine);

                // If in silent mode, also write to console and flush immediately
                if (isSilentMode)
                {
                    Console.WriteLine(message);
                    Console.Out.Flush();
                }

                // Only auto-scroll if enabled
                if (autoScroll)
                {
                    // Force scroll to end in multiple ways to ensure it works
                    txtLog.ScrollToEnd();
                    txtLog.CaretIndex = txtLog.Text.Length;

                    // Ensure the parent ScrollViewer also scrolls
                    if (txtLog.Parent is ScrollViewer scrollViewer)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
                    }
                    else if (LogicalTreeHelper.GetParent(txtLog.Parent) is ScrollViewer parentScrollViewer)
                    {
                        parentScrollViewer.ScrollToVerticalOffset(parentScrollViewer.ExtentHeight);
                    }

                    // Update layout to ensure scrolling takes effect
                    txtLog.UpdateLayout();
                }
            });
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

        private void InitializeChangelogs()
        {
            try
            {
                string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
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

                changelogs.Clear();

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
                                changelogs.Add(new ChangelogInfo
                                {
                                    Timestamp = timestamp,
                                    Author = author,
                                    Formatted_Content = formattedContent,
                                    Raw_Content = rawContent,
                                    Message_Id = messageId
                                });
                            }
                        }
                    }
                }

                if (changelogs.Count == 0)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] No entries found, creating default changelog");
                    }

                    // Create a new list with the default entry and save it
                    var defaultEntries = new List<Dictionary<string, string>> { defaultEntry };
                    IniLibrary.SaveChangelog(defaultEntries);

                    // Add the default entry to the changelogs list
                    changelogs.Add(new ChangelogInfo
                    {
                        Timestamp = DateTime.Now,
                        Author = "System",
                        Formatted_Content = defaultEntry["formatted_content"],
                        Raw_Content = defaultEntry["raw_content"],
                        Message_Id = "default"
                    });
                }

                // Format all changelogs
                FormatAllChangelogs();
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to initialize changelogs: {ex.Message}");
            }
        }

        private void FormatAllChangelogs()
        {
            try
            {
                // If we don't need to update and we have cached content, return early
                if (!changelogNeedsUpdate && !string.IsNullOrEmpty(cachedChangelogContent))
                {
                    changelogContent = cachedChangelogContent;
                    return;
                }

                var formattedLogs = new StringBuilder();

                // Order by timestamp descending to show newest first
                foreach (var log in changelogs.OrderByDescending(x => x.Timestamp))
                {
                    // Skip entries with invalid timestamps (like year 0001)
                    if (log.Timestamp.Year <= 1)
                        continue;

                    formattedLogs.AppendLine(log.Formatted_Content);
                }

                changelogContent = formattedLogs.Length > 0
                    ? formattedLogs.ToString()
                    : "No changelog entries available.";

                // Cache the result
                cachedChangelogContent = changelogContent;
                changelogNeedsUpdate = false;
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to format changelogs: {ex.Message}");
                changelogContent = "Error loading changelog entries.";
                cachedChangelogContent = null;
                changelogNeedsUpdate = true;
            }
        }

        // Update the flag whenever changelogs are modified
        private void UpdateChangelogs(List<ChangelogInfo> newChangelogs)
        {
            changelogs.Clear();
            changelogs.AddRange(newChangelogs);
            changelogNeedsUpdate = true;
        }

        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            FormatAllChangelogs();
            var dialog = new ChangelogWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private async Task CheckChangelogAsync()
        {
            try
            {
                string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string changelogPath = Path.Combine(appPath, "changelog.yml");

                // Get token from Constants
                string token = Constants.PATCHER_TOKEN;
                if (string.IsNullOrEmpty(token))
                {
                    StatusLibrary.Log("[ERROR] Unable to authenticate with changelog API");
                    StatusLibrary.Log("Continuing....");
                    return;
                }

                // Determine which endpoint to use
                string url;
                if (!File.Exists(changelogPath) || (changelogs.Count == 1 && changelogs[0].Message_Id == "default"))
                {
                    // If no changelog file exists or only has default entry, get all changelogs
                    url = allChangelogsEndpoint;
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] No changelog file exists or only has default entry, getting all changelogs");
                    }
                }
                else
                {
                    // Get the latest message_id and get only new changelogs
                    string currentMessageId = IniLibrary.GetLatestMessageId();
                    url = $"{changelogEndpoint}{currentMessageId}";
                }

                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Changelog path: {changelogPath}");
                    StatusLibrary.Log($"[DEBUG] File exists: {File.Exists(changelogPath)}");
                    StatusLibrary.Log($"[DEBUG] Using URL: {url}");
                    StatusLibrary.Log($"[DEBUG] Calling changelog API: {url}");
                }

                // Retry settings
                int maxRetries = 3;
                int currentRetry = 0;
                bool success = false;
                Exception lastException = null;

                while (currentRetry < maxRetries && !success)
                {
                    try
                    {
                        using (var client = new HttpClient(new HttpClientHandler
                        {
                            // Set security protocol explicitly to support all TLS versions
                            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                        }))
                        {
                            // Set a timeout of 5 seconds for the changelog API request
                            client.Timeout = TimeSpan.FromSeconds(5);
                            client.DefaultRequestHeaders.Add("x-patcher-token", token);

                            // Use different protocol on retries
                            string apiUrl = currentRetry == 0 ? url : url.Replace("https://", "http://");

                            if (isDebugMode && currentRetry > 0)
                            {
                                StatusLibrary.Log($"[DEBUG] Retry {currentRetry + 1} using URL: {apiUrl}");
                            }

                            // First get the raw response to check for HTML vs JSON
                            var responseString = await client.GetStringAsync(apiUrl);

                            // Check if we got HTML instead of JSON (common with SSL errors or proxies)
                            if (responseString.StartsWith("<"))
                            {
                                throw new JsonException("Response appears to be HTML instead of JSON");
                            }

                            // Parse the JSON response
                            changelogResponse = JsonSerializer.Deserialize<ChangelogResponse>(responseString);

                            if (changelogResponse?.Status == "success" && changelogResponse.Changelogs != null)
                            {
                                if (changelogResponse.Total > 0)
                                {
                                    if (isDebugMode)
                                    {
                                        StatusLibrary.Log($"[DEBUG] Found {changelogResponse.Total} new changelog entries");
                                    }

                                    // Add new changelogs to the list and update UI
                                    List<Dictionary<string, string>> entries = new List<Dictionary<string, string>>();
                                    bool hasNewEntries = false;

                                    foreach (var changelog in changelogResponse.Changelogs)
                                    {
                                        // Skip if this changelog is already in our list
                                        if (changelogs.Any(c => c.Message_Id == changelog.Message_Id))
                                        {
                                            continue;
                                        }

                                        // Format the author name
                                        changelog.Author = FormatAuthorName(changelog.Author);

                                        // Add to our list
                                        changelogs.Add(changelog);

                                        // Add to entries for saving
                                        entries.Add(new Dictionary<string, string>
                                        {
                                            ["timestamp"] = changelog.Timestamp.ToString("O"),
                                            ["author"] = changelog.Author,
                                            ["formatted_content"] = changelog.Formatted_Content,
                                            ["raw_content"] = changelog.Raw_Content,
                                            ["message_id"] = changelog.Message_Id
                                        });

                                        hasNewEntries = true;
                                    }

                                    if (hasNewEntries)
                                    {
                                        hasNewChangelogs = true;
                                        changelogNeedsUpdate = true;

                                        // Save the updated changelogs to the file
                                        IniLibrary.SaveChangelog(IniLibrary.LoadChangelog().Concat(entries).ToList());

                                        if (isDebugMode)
                                        {
                                            StatusLibrary.Log($"[DEBUG] Saved {entries.Count} new changelog entries");
                                        }

                                        // Show the latest changelog window if we have new entries
                                        if (hasNewChangelogs && !isSilentMode && !isNeedingSelfUpdate && !isPendingPatch)
                                        {
                                            Dispatcher.Invoke(() =>
                                            {
                                                if (_latestChangelogWindow == null || !_latestChangelogWindow.IsVisible)
                                                {
                                                    // Format the changelog content to pass to the window
                                                    FormatAllChangelogs();
                                                    _latestChangelogWindow = new LatestChangelogWindow(cachedChangelogContent);
                                                    _latestChangelogWindow.Owner = this;
                                                    _latestChangelogWindow.Show();
                                                }
                                            });
                                        }
                                    }
                                    else if (isDebugMode)
                                    {
                                        StatusLibrary.Log("[DEBUG] No new changelog entries found");
                                    }
                                }
                                else if (isDebugMode)
                                {
                                    StatusLibrary.Log("[DEBUG] No new changelog entries available");
                                }

                                // Set success flag
                                success = true;
                            }
                            else if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Invalid response format or empty response");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Changelog JSON parse error: {ex.Message}");
                            // If we have an inner exception, log it as well
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                            }
                        }
                        // This is likely a response format issue, so retrying won't help
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Changelog HTTP request error: {ex.Message}");
                            // Log inner exception which often contains SSL details
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");

                                // If it's an SSL error, log more details
                                if (ex.InnerException is System.Security.Authentication.AuthenticationException)
                                {
                                    StatusLibrary.Log($"[DEBUG] SSL error: {ex.InnerException.Message}");
                                }
                            }
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Handle timeout specifically
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Changelog API request timed out on attempt {currentRetry + 1}");
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Failed to check changelogs: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                            }
                        }
                        currentRetry++;

                        // Add a small delay between retries
                        if (currentRetry < maxRetries)
                        {
                            await Task.Delay(500 * currentRetry); // Increasing backoff
                        }
                    }
                }

                // Log if all retries failed but don't show errors to users
                if (!success && isDebugMode && lastException != null)
                {
                    StatusLibrary.Log($"[DEBUG] All changelog check attempts failed. Last error: {lastException.Message}");

                    // Add a troubleshooting hint if SSL errors are detected
                    if (lastException is HttpRequestException && lastException.InnerException is System.Security.Authentication.AuthenticationException)
                    {
                        StatusLibrary.Log("[DEBUG] SSL connection issues detected. This may be due to outdated TLS settings, firewall, or antivirus software.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Unhandled error in changelog check: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        StatusLibrary.Log($"[DEBUG] Inner exception: {ex.InnerException.Message}");
                    }
                }
                StatusLibrary.Log("[ERROR] Failed to check for changelogs");
                StatusLibrary.Log("Continuing....");
            }
        }

        private async Task CheckChangelogWithFallbackAsync()
        {
            try
            {
                // Attempt to fetch changelog from API
                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Attempting to fetch changelog from API...");
                }

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("x-patcher-token", Constants.PATCHER_TOKEN);

                    var response = await client.GetAsync(changelogEndpoint);

                    if (response.IsSuccessStatusCode)
                    {
                        var changelogData = await response.Content.ReadAsStringAsync();
                        if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] Successfully fetched changelog from API.");
                        }

                        // Process the changelog data
                        ProcessChangelogData(changelogData);
                        return;
                    }
                    else
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] API returned non-success status code: {response.StatusCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error fetching changelog from API: {ex.Message}");
                }
            }

            // Fallback to local changelog if API fails
            try
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Falling back to local changelog...");
                }

                string localChangelogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.yml");
                if (File.Exists(localChangelogPath))
                {
                    var localChangelogData = await File.ReadAllTextAsync(localChangelogPath);
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Successfully loaded local changelog.");
                    }

                    // Process the local changelog data
                    ProcessChangelogData(localChangelogData);
                }
                else
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Local changelog file not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error loading local changelog: {ex.Message}");
                }
            }
        }

        private void ProcessChangelogData(string changelogData)
        {
            try
            {
                // Parse and update changelog content
                changelogContent = changelogData;
                changelogNeedsUpdate = true;

                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Changelog data processed successfully.");
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error processing changelog data: {ex.Message}");
                }
            }
        }

        private void LoadLoadingMessages()
        {
            // Create messages directly
            loadingMessages = LoadingMessages.CreateDefault();
        }

        private string GetRandomLoadingMessage()
        {
            if (loadingMessages?.Messages != null && loadingMessages.Messages.Count > 0)
            {
                return loadingMessages.Messages[random.Next(loadingMessages.Messages.Count)];
            }
            return null;
        }

        private async void BtnPatch_Click(object sender, RoutedEventArgs e)
        {
            if (isLoading && !isPendingPatch)
            {
                isPendingPatch = true;
                StatusLibrary.Log("Checking for updates...");
                btnPatch.Content = "CANCEL";
                return;
            }

            if (isPatching)
            {
                isPatchCancelled = true;
                cts.Cancel();
                return;
            }
            await StartPatch();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                process = UtilityLibrary.StartEverquest();
                if (process != null)
                {
                    if (isSilentMode)
                    {
                        Console.WriteLine("Starting EverQuest...");
                        Console.Out.Flush();

                        // Ensure the process is properly detached
                        process.EnableRaisingEvents = false;
                        process.StartInfo.UseShellExecute = true;
                        process.StartInfo.RedirectStandardOutput = false;
                        process.StartInfo.RedirectStandardError = false;
                        process.StartInfo.CreateNoWindow = false;

                        // Give the process time to fully start
                        Thread.Sleep(2000);
                    }
                    this.Close();
                }
                else
                {
                    if (isSilentMode)
                    {
                        Console.WriteLine("[ERROR] Failed to start EverQuest");
                        Console.Out.Flush();
                        Thread.Sleep(2000); // Longer delay for errors
                    }
                    else
                    {
                        MessageBox.Show("The process failed to start", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception err)
            {
                if (isSilentMode)
                {
                    Console.WriteLine($"[ERROR] Failed to start EverQuest: {err.Message}");
                    Console.Out.Flush();
                    Thread.Sleep(2000); // Longer delay for errors
                }
                else
                {
                    MessageBox.Show($"An error occurred while trying to start Everquest: {err.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
                // Check if Patch button is visible and enabled
                if (btnPatch.Visibility == Visibility.Visible && btnPatch.IsEnabled)
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

        private void FileIntegrityScan_Click(object sender, RoutedEventArgs e)
        {
            // Close the optimizations panel
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;

            // Run the file integrity scan with full scan only
            RunFileIntegrityScanAsync(true);
        }

        private async void MemoryOptimizations_Click(object sender, RoutedEventArgs e)
        {
            // Close the optimizations panel
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;

            btnMemoryOptimizations.IsEnabled = false;
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqcfgPath = Path.Combine(eqPath, "eqclient.ini");

                if (!File.Exists(eqcfgPath))
                {
                    MessageBox.Show("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show confirmation dialog
                string message = "This will modify settings in the EQclient.ini file to help with memory optimization. Continue?";
                if (!CustomMessageBox.Show(message))
                {
                    return;
                }

                StatusLibrary.Log("Applying memory optimizations to eqclient.ini...");

                await Task.Run(() =>
                {
                    // Read the ini file
                    var lines = File.ReadAllLines(eqcfgPath);

                    // Create dictionary of sections and their keys/values
                    var sections = new Dictionary<string, Dictionary<string, string>>();
                    string currentSection = "";

                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                        {
                            // This is a section header
                            currentSection = trimmedLine;
                            if (!sections.ContainsKey(currentSection))
                            {
                                sections[currentSection] = new Dictionary<string, string>();
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmedLine) && trimmedLine.Contains("="))
                        {
                            // This is a key=value pair
                            var parts = trimmedLine.Split(new[] { '=' }, 2);
                            if (parts.Length == 2 && !string.IsNullOrEmpty(currentSection))
                            {
                                sections[currentSection][parts[0].Trim()] = parts[1].Trim();
                            }
                        }
                    }

                    // Update or add the memory optimization settings
                    if (!sections.ContainsKey("[Defaults]"))
                    {
                        sections["[Defaults]"] = new Dictionary<string, string>();
                    }

                    sections["[Defaults]"]["VertexShaders"] = "TRUE";
                    sections["[Defaults]"]["PostEffects"] = "0";

                    if (!sections.ContainsKey("[Options]"))
                    {
                        sections["[Options]"] = new Dictionary<string, string>();
                    }

                    sections["[Options]"]["MaxFPS"] = "60";
                    sections["[Options]"]["MaxBGFPS"] = "20";
                    sections["[Options]"]["ClipPlane"] = "12";

                    // Rebuild the ini file content
                    var newContent = new List<string>();
                    foreach (var section in sections)
                    {
                        newContent.Add(section.Key);
                        foreach (var entry in section.Value)
                        {
                            newContent.Add($"{entry.Key}={entry.Value}");
                        }
                        newContent.Add(""); // Empty line between sections
                    }

                    // Write the updated content back to the file
                    File.WriteAllLines(eqcfgPath, newContent);
                });

                StatusLibrary.Log("Memory optimizations applied successfully!");
            }
            catch (Exception ex)
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
            StatusLibrary.Log("Starting file integrity scan...");
            await Task.Delay(1000);

            // Reset cancellation token source to ensure no previous cancellations affect this scan
            if (cts.IsCancellationRequested)
            {
                cts = new CancellationTokenSource();
            }

            // Download and parse the filelist
            string suffix = "rof";
            string primaryUrl = filelistUrl;
            string fallbackUrl = "https://patch.heroesjourneyemu.com";
            string webUrl = $"{primaryUrl}/filelist_{suffix}.yml";
            string filelistResponse = "";

            // Try primary URL first
            StatusLibrary.Log("Downloading file list...");
            filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");

            // If primary URL fails, try fallback
            if (filelistResponse != "")
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Primary URL failed, trying fallback URL");
                }
                webUrl = $"{fallbackUrl}/filelist_{suffix}.yml";
                filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");

                // If fallback also fails, report error
                if (filelistResponse != "")
                {
                    StatusLibrary.Log($"Failed to fetch filelist from both primary and fallback URLs:");
                    StatusLibrary.Log($"Primary: {primaryUrl}/filelist_{suffix}.yml");
                    StatusLibrary.Log($"Fallback: {fallbackUrl}/filelist_{suffix}.yml");
                    StatusLibrary.Log($"Error: {filelistResponse}");
                    return;
                }
                else
                {
                    // Fallback succeeded, update the filelistUrl for future use in this session
                    filelistUrl = fallbackUrl;
                }
            }

            // Read the filelist
            FileList filelist;
            string filelistPath = $"{Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)}\\filelist.yml";

            using (var input = File.OpenText(filelistPath))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializerBuilder.Deserialize<FileList>(input);
            }

            // First do a quick check (file existence and size only)
            StatusLibrary.Log("Starting Quick File Scan...");

            Dispatcher.Invoke(() =>
            {
                txtProgress.Visibility = Visibility.Visible;
                progressBar.Value = 0;
                txtProgress.Text = "Quick scan: 0%";
            });

            bool quickCheckPassed = true;
            List<FileEntry> missingOrModifiedFiles = new List<FileEntry>();

            int totalFilesCount = filelist.downloads.Count;
            int checkedFiles = 0;
            int loggedFileCount = 0;
            const int maxLoggedFiles = 10; // Only log a limited number of missing files to avoid flooding

            // Process files in batches to prevent UI freezing
            for (int i = 0; i < filelist.downloads.Count; i++)
            {
                var entry = filelist.downloads[i];
                checkedFiles++;

                // Update progress bar every 10 files or for the last file
                if (checkedFiles % 10 == 0 || checkedFiles == totalFilesCount)
                {
                    int progress = (int)((double)checkedFiles / totalFilesCount * 100);
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = progress;
                        txtProgress.Text = $"Quick scan: {progress}%";
                    });

                    // Short delay every 100 files to keep UI responsive
                    if (checkedFiles % 100 == 0)
                    {
                        await Task.Delay(1); // Minimal delay to allow UI thread to process
                    }
                }

                // Skip heroesjourneyemu.exe as it's the patcher itself
                if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                {
                    StatusLibrary.Log($"[Warning] Path {entry.name} might be outside of your EverQuest directory.");
                    continue;
                }

                // Check if this is a special file type that needs additional handling
                bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                bool isUiFile = entry.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase);
                bool isMapFile = entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                bool needsDownload = false;
                if (!await Task.Run(() => File.Exists(path)))
                {
                    // Only log a limited number of missing files to prevent console flooding
                    if (loggedFileCount < maxLoggedFiles)
                    {
                        if (isDinput8)
                        {
                            StatusLibrary.Log($"[Important] Missing dinput8.dll detected - will be downloaded");
                        }
                        else if (isMapFile)
                        {
                            StatusLibrary.Log($"Missing map file detected: {entry.name}");
                        }
                        else
                        {
                            StatusLibrary.Log($"Missing file detected: {entry.name}");
                        }
                        loggedFileCount++;
                    }
                    else if (loggedFileCount == maxLoggedFiles)
                    {
                        StatusLibrary.Log($"... and more files need to be downloaded (limiting log output)");
                        loggedFileCount++;
                    }

                    needsDownload = true;
                    quickCheckPassed = false;
                }
                else
                {
                    // For dinput8.dll, always check the MD5 hash in the quick scan
                    if (isDinput8)
                    {
                        var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Current MD5: {md5.ToUpper()}");
                                StatusLibrary.Log($"[DEBUG] Expected MD5: {entry.md5.ToUpper()}");
                            }

                            needsDownload = true;
                            quickCheckPassed = false;
                        }
                        else if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] dinput8.dll is up to date (MD5: {md5.ToUpper()})");
                        }
                    }
                    else
                    {
                        // For other files, just check the size
                        var fileInfo = await Task.Run(() => new FileInfo(path));
                        if (fileInfo.Length != entry.size)
                        {
                            // Only log a limited number of size mismatches to prevent console flooding
                            if (loggedFileCount < maxLoggedFiles)
                            {
                                StatusLibrary.Log($"Size mismatch detected: {entry.name}");
                                loggedFileCount++;
                            }
                            else if (loggedFileCount == maxLoggedFiles)
                            {
                                StatusLibrary.Log($"... and more files need to be downloaded (limiting log output)");
                                loggedFileCount++;
                            }

                            needsDownload = true;
                            quickCheckPassed = false;
                        }
                    }
                }

                // If file needs download, add it to the right lists
                if (needsDownload)
                {
                    // Add to missing/modified files list if not already there
                    if (!missingOrModifiedFiles.Any(f => f.name == entry.name))
                    {
                        missingOrModifiedFiles.Add(entry);
                    }

                    // Add to download queue if not already there
                    if (!filesToDownload.Any(f => f.name == entry.name))
                    {
                        filesToDownload.Add(entry);
                        if (isDebugMode)
                        {
                            string fileType = isDinput8 ? "DLL" : (isMapFile ? "map" : (isUiFile ? "UI" : "file"));
                            StatusLibrary.Log($"[DEBUG] Added {fileType} to download queue: {entry.name}");
                        }
                    }
                }
            }

            // Final progress update to ensure we show 100%
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = 100;
                txtProgress.Text = "Quick scan: 100%";
            });
            await Task.Delay(250); // Small delay to let the UI update

            // Hide progress bar after quick check
            Dispatcher.Invoke(() =>
            {
                txtProgress.Visibility = Visibility.Collapsed;
                progressBar.Value = 0;
            });

            // If there are any files to download, show the patch button and hide play button
            if (filesToDownload.Count > 0)
            {
                StatusLibrary.Log($"Scan complete - found {filesToDownload.Count} file(s) that need to be patched.");
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Visibility = Visibility.Visible;
                    btnPlay.Visibility = Visibility.Collapsed;
                    btnPatch.IsEnabled = true;
                });

                // Set integrity check status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "failed";
                await Task.Run(() => IniLibrary.Save());

                // Print summary of detected types (for debugging)
                if (isDebugMode)
                {
                    var mapFiles = filesToDownload.Where(f =>
                        f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                        f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                        f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Count();

                    var uiFiles = filesToDownload.Where(f =>
                        f.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) ||
                        f.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase)).Count();

                    var dllFiles = filesToDownload.Where(f =>
                        f.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Count();

                    StatusLibrary.Log($"[DEBUG] File types to download: {mapFiles} map files, {uiFiles} UI files, {dllFiles} DLL files");
                }

                // Immediately start the patch process if auto-patch is enabled
                if (isAutoPatch && !isNeedingSelfUpdate)
                {
                    isPendingPatch = true;
                    await Task.Delay(1000);
                    await StartPatch();
                }
                return;
            }

            // If quick check passed and this is an automatic scan, we're done
            if (quickCheckPassed && !fullScanOnly)
            {
                // Update integrity check timestamp and status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "success";
                await Task.Run(() => IniLibrary.Save());

                StatusLibrary.Log("Quick scan complete - all files are up to date");

                // Make the play button visible
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Visibility = Visibility.Collapsed;
                    btnPlay.Visibility = Visibility.Visible;
                    btnPlay.IsEnabled = true;
                });

                return;
            }

            // If full scan was requested, continue with MD5 checks
            if (fullScanOnly)
            {
                StatusLibrary.Log("Performing full file check...");
                Dispatcher.Invoke(() =>
                {
                    txtProgress.Visibility = Visibility.Visible;
                    progressBar.Value = 0;
                    txtProgress.Text = "Full scan: 0%";
                });

                bool allFilesIntact = true;
                checkedFiles = 0;
                loggedFileCount = 0;

                foreach (var entry in filelist.downloads)
                {
                    // Skip heroesjourneyemu.exe as it's the patcher itself
                    if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        checkedFiles++;
                        continue;
                    }

                    checkedFiles++;

                    // Update progress bar every 10 files or for the last file
                    if (checkedFiles % 10 == 0 || checkedFiles == totalFilesCount)
                    {
                        int progress = (int)((double)checkedFiles / totalFilesCount * 100);
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = progress;
                            txtProgress.Text = $"Full scan: {progress}%";
                        });

                        // Short delay every 100 files to keep UI responsive
                        if (checkedFiles % 100 == 0)
                        {
                            await Task.Delay(1); // Minimal delay to allow UI thread to process
                        }
                    }

                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        continue;
                    }

                    // Check if this is a special file type
                    bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                    bool isMapFile = entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                    entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                    entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                    bool needsDownload = false;
                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        // Limit logging to prevent console flooding
                        if (loggedFileCount < maxLoggedFiles)
                        {
                            string fileType = isDinput8 ? "dinput8.dll" : (isMapFile ? "map file" : "file");
                            StatusLibrary.Log($"Missing {fileType} detected: {entry.name}");
                            loggedFileCount++;
                        }
                        else if (loggedFileCount == maxLoggedFiles)
                        {
                            StatusLibrary.Log($"... and more files need to be downloaded (limiting log output)");
                            loggedFileCount++;
                        }

                        needsDownload = true;
                        allFilesIntact = false;
                    }
                    else
                    {
                        var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] MD5 mismatch for {entry.name}");
                                StatusLibrary.Log($"[DEBUG] Expected: {entry.md5.ToUpper()}");
                                StatusLibrary.Log($"[DEBUG] Got: {md5.ToUpper()}");
                            }

                            // Limit logging to prevent console flooding
                            if (loggedFileCount < maxLoggedFiles)
                            {
                                StatusLibrary.Log($"Content mismatch detected: {entry.name}");
                                loggedFileCount++;
                            }
                            else if (loggedFileCount == maxLoggedFiles)
                            {
                                StatusLibrary.Log($"... and more files need to be downloaded (limiting log output)");
                                loggedFileCount++;
                            }

                            needsDownload = true;
                            allFilesIntact = false;
                        }
                    }

                    // If file needs download, add it to the download queue
                    if (needsDownload && !filesToDownload.Any(f => f.name == entry.name))
                    {
                        filesToDownload.Add(entry);
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Added file to download queue from full scan: {entry.name}");
                        }
                    }
                }

                // Update integrity check timestamp and status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = allFilesIntact ? "success" : "failed";
                await Task.Run(() => IniLibrary.Save());

                // Hide progress bar after full check
                Dispatcher.Invoke(() =>
                {
                    txtProgress.Visibility = Visibility.Collapsed;
                    progressBar.Value = 0;
                });

                // Report results
                if (filesToDownload.Count > 0)
                {
                    StatusLibrary.Log($"Full scan complete - {filesToDownload.Count} file(s) need updating");
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                        btnPatch.IsEnabled = true;
                    });

                    // If in auto-patch mode, start patching
                    if (isAutoPatch)
                    {
                        isPendingPatch = true;
                        await Task.Delay(1000);
                        await StartPatch();
                    }
                }
                else if (allFilesIntact)
                {
                    StatusLibrary.Log("Full scan complete - all files are up to date");

                    // If files are intact but versions differ, update the version
                    IniLibrary.instance.LastPatchedVersion = filelist.version;
                    await Task.Run(() => IniLibrary.Save());

                    // Make the play button visible
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Collapsed;
                        btnPlay.Visibility = Visibility.Visible;
                        btnPlay.IsEnabled = true;
                    });
                }
                else
                {
                    StatusLibrary.Log("Scan complete - some files may need updating");
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                        btnPatch.IsEnabled = true;
                    });
                }
            }
        }

        private void PatcherChangelog_Click(object sender, RoutedEventArgs e)
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

                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Created default patcher changelog file");
                    }
                }

                // Read the content from the changelog file
                string changelogContent = File.ReadAllText(patcherChangelogPath);

                // Display the changelog in the ChangelogWindow
                var dialog = new ChangelogWindow(changelogContent);
                dialog.Title = "Patcher Changelog";
                dialog.Owner = this;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to open patcher changelog: {ex.Message}");
                MessageBox.Show($"Failed to open patcher changelog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} // End of namespace THJPatcher
