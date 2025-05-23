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
using System.IO.Compression;

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

        [JsonPropertyName("raw")]
        public string Raw { get; set; }

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

            // Use the raw content as-is since it's already markdown formatted
            // Add proper spacing between sections to ensure consistent rendering
            return $"# {date}\n\n## {Author}\n\n{_rawContent.Trim()}\n\n---";
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

        // Add a field to store the latest new changelogs for the modal window
        private List<ChangelogInfo> latestNewChangelogs = new List<ChangelogInfo>();

        // Feature flag for chunked patching
        private bool isChunkedPatchEnabled = true; // Default to chunked patching (opposite of UseSingleFilePatch)

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

        private string FormatChangelogs(List<ChangelogInfo> changelogsToFormat)
        {
            if (changelogsToFormat == null || changelogsToFormat.Count == 0)
                return "No new changelogs.";
            var formattedLogs = new StringBuilder();
            foreach (var log in changelogsToFormat.OrderByDescending(x => x.Timestamp))
            {
                if (log.Timestamp.Year <= 1)
                    continue;
                var date = log.Timestamp.ToString("MMMM dd, yyyy");
                var author = FormatAuthorName(log.Author ?? "System");
                formattedLogs.AppendLine($"# {date}");
                formattedLogs.AppendLine($"## {author}");
                formattedLogs.AppendLine();
                if (!string.IsNullOrEmpty(log.Raw_Content))
                {
                    string content = log.Raw_Content.Trim();
                    content = PreProcessMarkdown(content);
                    formattedLogs.AppendLine(content);
                }
                formattedLogs.AppendLine();
                formattedLogs.AppendLine("---");
                formattedLogs.AppendLine();
            }
            return formattedLogs.ToString();
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded; btnPatch.Click += BtnPatch_Click;
            btnPlay.Click += BtnPlay_Click;
            chkAutoPatch.Checked += ChkAutoPatch_CheckedChanged;
            chkAutoPlay.Checked += ChkAutoPlay_CheckedChanged;
            chkEnableCpuAffinity.Checked += ChkEnableCpuAffinity_CheckedChanged;
            chkEnableCpuAffinity.Unchecked += ChkEnableCpuAffinity_CheckedChanged;
            chkEnableChunkedPatch.Checked += ChkEnableChunkedPatch_CheckedChanged;
            chkEnableChunkedPatch.Unchecked += ChkEnableChunkedPatch_CheckedChanged;

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

            // SWAP: Make patch.heroesjourneyemu.com the primary, GitHub the backup
            filelistUrl = "https://patch.heroesjourneyemu.com";
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

            // Check for eqgame.exe in the root folder
            string eqExePath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "eqgame.exe");
            if (!File.Exists(eqExePath))
            {
                CustomWarningBox.Show(
                    "eqgame.exe is missing, did you forget to run the installer? Did you not run this file from your EverQuest directory?",
                    "Missing eqgame.exe"
                );
            }

            // Set CPU affinity checkbox state immediately after loading configuration
            bool enableCpuAffinity = (IniLibrary.instance.EnableCpuAffinity.ToLower() == "true");
            Dispatcher.Invoke(() =>
            {
                chkEnableCpuAffinity.IsChecked = enableCpuAffinity;
            });

            // Changelog wipe logic: bump changelogRefreshValue to '2' to trigger a one-time wipe for all users
            if (IniLibrary.instance.ChangelogRefreshValue != "2")
            {
                IniLibrary.instance.DeleteChangelog = "true";
                IniLibrary.instance.ChangelogRefreshValue = "2";
                IniLibrary.Save();
            }

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

        private async Task CompleteInitialization()
        {
            // Ensure the THJ Log Parser is present and up-to-date before any patching or file checks
            await EnsureLatestLogParserAsync();

            // Clear any existing download list to start fresh
            filesToDownload.Clear();

            // Check if changelog needs to be deleted
            string appPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string changelogYmlPath = Path.Combine(appPath, "changelog.yml");
            string changelogMdPath = Path.Combine(appPath, "changelog.md");
            bool needsReinitialization = false;

            if (IniLibrary.instance.DeleteChangelog == null || IniLibrary.instance.DeleteChangelog.ToLower() == "true")
            {
                bool deletedAnyFile = false;
                if (File.Exists(changelogYmlPath))
                {
                    try
                    {
                        File.Delete(changelogYmlPath);
                        StatusLibrary.Log("Outdated Changelog file detected...Changelog will be updated during this patch.");
                        needsReinitialization = true;
                        deletedAnyFile = true;
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
                        deletedAnyFile = true;
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[ERROR] Failed to delete changelog.md: {ex.Message}");
                    }
                }

                // Set DeleteChangelog to false for future runs
                IniLibrary.instance.DeleteChangelog = "false";
                // Set changelogRefreshValue to '2' after deletion
                IniLibrary.instance.ChangelogRefreshValue = "2";
                IniLibrary.Save();

                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Set DeleteChangelog to 'false' and ChangelogRefreshValue to '2' after {(deletedAnyFile ? "deleting" : "checking")} changelog files");
                }

                // Clear any cached changelog data
                if (needsReinitialization)
                {
                    changelogs.Clear();
                    changelogContent = "";
                    cachedChangelogContent = null;
                    changelogNeedsUpdate = true;
                    hasNewChangelogs = false;

                    // Update the LastChangelogRefresh to record this refresh
                    IniLibrary.instance.LastChangelogRefresh = DateTime.UtcNow.ToString("O");
                    IniLibrary.Save(); // Save again after updating LastChangelogRefresh

                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Reset changelog cache and updated LastChangelogRefresh timestamp");
                    }
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

            // Run a quick file scan every time the patcher starts
            // This must happen BEFORE showing changelogs to ensure filesToDownload is populated
            await RunFileIntegrityScanAsync(false);

            if (hasNewChangelogs)
            {
                // Format only the new changelogs for display
                string latestChangelogContent = FormatChangelogs(latestNewChangelogs);
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Visibility = Visibility.Collapsed;
                    btnPlay.Visibility = Visibility.Collapsed;
                    btnPatch.IsEnabled = false;
                    btnPlay.IsEnabled = false;
                    StatusLibrary.Log("New changelogs detected! Please review the changes before proceeding.");
                });
                await Dispatcher.InvokeAsync(() =>
                {
                    _latestChangelogWindow = new LatestChangelogWindow(latestChangelogContent);
                    _latestChangelogWindow.Owner = this;
                    _latestChangelogWindow.ShowDialog();
                });
                if (_latestChangelogWindow != null && _latestChangelogWindow.IsAcknowledged)
                {
                    ShowAppropriateButtons();
                }
            }
            else
            {
                ShowAppropriateButtons();
            }            // Load configuration
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;

            // Initialize chunked patching checkbox - checked when single file patching is enabled
            bool useSingleFilePatch = (IniLibrary.instance.UseSingleFilePatch.ToLower() == "true");
            chkEnableChunkedPatch.IsChecked = useSingleFilePatch; // Checkbox is for "Use Single File Patching"
            isChunkedPatchEnabled = !useSingleFilePatch; // Flag is the inverse (true = use chunked patching)
            StatusLibrary.Log($"Using {(isChunkedPatchEnabled ? "chunked" : "single file")} patching method. {(isChunkedPatchEnabled ? "(faster)" : "(slower)")}");

            // If we're in auto-patch mode, start patching (but not for self-updates)
            if (isAutoPatch && !isNeedingSelfUpdate && !hasNewChangelogs && filesToDownload.Count > 0)
            {
                isPendingPatch = true;
                await Task.Delay(1000);
                await StartPatch();
            }
            // If we're in auto-play mode and no files need to be patched
            else if (isAutoPlay && !isNeedingSelfUpdate && filesToDownload.Count == 0)
            {
                await Task.Delay(1000);
                BtnPlay_Click(null, null);
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
                string primaryUrl = filelistUrl;
                string fallbackUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download";
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

                using (var client = new HttpClient())
                {
                    // Set a timeout of 3 seconds for server status API requests
                    client.Timeout = TimeSpan.FromSeconds(3);
                    client.DefaultRequestHeaders.Add("x-patcher-token", token);

                    // Check server status
                    var response = await client.GetStringAsync("http://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/serverstatus");
                    var status = JsonSerializer.Deserialize<ServerStatus>(response);

                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Server status response: {response}");
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

                    // Check exp bonus
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Checking exp bonus...");
                    }
                    var expResponse = await client.GetStringAsync("http://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/expbonus");
                    var expStatus = JsonSerializer.Deserialize<ExpBonusStatus>(expResponse);

                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Exp bonus response: {expResponse}");
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
                }
            }
            catch (TaskCanceledException)
            {
                // Handle timeout specifically but silently
                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Server status API request timed out after 3 seconds");
                }
                // Silently fail - we don't want to show errors for this
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Server status check error: {ex.Message}");
                }
                // Silently fail - we don't want to show errors for this
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
                string url = $"{patcherUrl}{fileName}.exe";
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

                    // If we get here, the new patcher is valid                    // Delete the old backup if it exists
                    if (File.Exists(localExePath + ".old"))
                    {
                        try
                        {
                            await Task.Run(() => File.Delete(localExePath + ".old"));
                        }
                        catch (Exception ex)
                        {
                            StatusLibrary.Log($"Warning: Could not delete old backup file: {ex.Message}");
                            // Continue anyway - we'll try to handle this gracefully
                        }
                    }

                    // Move the current patcher to .old
                    try
                    {
                        await Task.Run(() => File.Move(localExePath, localExePath + ".old"));
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"Could not move current patcher to backup: {ex.Message}");
                        // Try to use a different name for the backup
                        string uniqueBackup = localExePath + $".old_{DateTime.Now.Ticks}";
                        try
                        {
                            await Task.Run(() => File.Move(localExePath, uniqueBackup));
                            StatusLibrary.Log("Used alternative backup file name.");
                        }
                        catch
                        {
                            StatusLibrary.Log("Could not create backup. Attempting direct replacement.");
                            // If we can't create a backup, we'll try to overwrite the file directly
                        }
                    }                    // Move the temp file to the final location
                    try
                    {
                        await Task.Run(() => File.Move(tempExePath, localExePath));
                    }
                    catch (Exception ex)
                    {
                        // The file might be in use or write-protected
                        StatusLibrary.Log($"Could not move new patcher to final location: {ex.Message}");

                        try
                        {
                            // Try to force replace the file by using Copy instead of Move
                            await Task.Run(() => File.Copy(tempExePath, localExePath, true));
                            StatusLibrary.Log("Forced file replacement succeeded.");

                            // Clean up the temp file
                            try { File.Delete(tempExePath); } catch { /* Ignore error here */ }
                        }
                        catch (Exception copyEx)
                        {
                            StatusLibrary.Log($"Critical error during patcher update: {copyEx.Message}");
                            StatusLibrary.Log("Please download the latest patcher manually from GitHub.");
                            throw; // Rethrow to trigger the outer catch block
                        }
                    }

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
            }            // --- CHUNKED PATCHING INTEGRATION ---
            if (isChunkedPatchEnabled)
            {
                StatusLibrary.Log($"Fast patching enabled. Attempting to download {filesToDownload.Count} files in chunks...");
                if (filesToDownload.Count > 0)
                {
                    // Show a preview of the first few files for debug/logging
                    var previewFiles = string.Join(", ", filesToDownload.Take(5).Select(f => f.name));
                    StatusLibrary.Log($"Files to patch: {previewFiles}{(filesToDownload.Count > 5 ? ", ..." : "")}");
                }
                // Pass the download prefix to TryChunkedPatch
                bool chunkedSuccess = await TryChunkedPatch(filesToDownload, filelist.downloadprefix);
                if (chunkedSuccess)
                {
                    StatusLibrary.Log("Patching Complete!");
                    StatusLibrary.SetProgress(10000);
                    // Optionally, update LastPatchedVersion and save config here if needed
                    IniLibrary.instance.LastPatchedVersion = filelist.version;
                    IniLibrary.instance.Version = version;
                    await Task.Run(() => IniLibrary.Save());
                    return;
                }
                else
                {
                    StatusLibrary.Log("Fast patch failed or incomplete. Falling back to normal patching...");
                }
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
                    // Try primary URL first
                    string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                    string response = await UtilityLibrary.DownloadFile(cts, url, entry.name);

                    // If primary fails, try backup URL
                    if (response != "")
                    {
                        string backupUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download/rof/" + entry.name.Replace("\\", "/");
                        response = await UtilityLibrary.DownloadFile(cts, backupUrl, entry.name);
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
            }
            else
            {
                string elapsed = start.Elapsed.ToString("ss\\.ff");
                StatusLibrary.Log($"Complete! Patched {generateSize(patchedBytes)} in {elapsed} seconds. Press Play to begin.");
                IniLibrary.instance.LastPatchedVersion = filelist.version;
                IniLibrary.instance.Version = version;
                await Task.Run(() => IniLibrary.Save());
            }
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
                string changelogYmlPath = Path.Combine(appPath, "changelog.yml");
                string patcherChangelogPath = Path.Combine(appPath, "patcher_changelog.md");

                // Clear previous changelogs when reinitializing
                changelogs.Clear();

                // First priority: Load changelogs from YAML if it exists (these are from the API)
                if (File.Exists(changelogYmlPath))
                {
                    var entries = ChangelogUtility.ReadChangelogFromYaml(changelogYmlPath);

                    // If we have entries, add them to our changelogs
                    if (entries != null && entries.Count > 0)
                    {
                        changelogs.AddRange(entries);
                        // Mark that we need to update the formatted content
                        changelogNeedsUpdate = true;

                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Loaded {entries.Count} entries from changelog.yml");
                        }

                        // We successfully loaded the game changelogs, no need to continue
                        return;
                    }
                }

                // If no changelogs were loaded yet, we need to trigger an API fetch
                if (changelogs.Count == 0)
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] No changelogs found in changelog.yml, will trigger API fetch");
                    }

                    // Add a default changelog entry if none found
                    changelogs.Add(new ChangelogInfo
                    {
                        Message_Id = "default",
                        Author = "System",
                        Timestamp = DateTime.Now,
                        Raw_Content = "Welcome to The Heroes Journey!\n\nLoading game changelogs..."
                    });
                    changelogNeedsUpdate = true;
                }
            }
            catch (Exception ex)
            {
                // Add an error changelog if we encounter issues
                changelogs.Clear();
                changelogs.Add(new ChangelogInfo
                {
                    Message_Id = "default",
                    Author = "System",
                    Timestamp = DateTime.Now,
                    Raw_Content = "Error loading changelogs. Please try again later."
                });
                changelogNeedsUpdate = true;

                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error in InitializeChangelogs: {ex.Message}");
                }
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

                // For all users, show a clean changelog without debug info
                formattedLogs.AppendLine("# The Heroes Journey - Changelog");
                formattedLogs.AppendLine();

                // Order by timestamp descending to show newest first
                foreach (var log in changelogs.OrderByDescending(x => x.Timestamp))
                {
                    // Skip entries with invalid timestamps (like year 0001)
                    if (log.Timestamp.Year <= 1)
                        continue;

                    var date = log.Timestamp.ToString("MMMM dd, yyyy");
                    var author = FormatAuthorName(log.Author ?? "System");

                    // Format the content with proper markdown structure
                    formattedLogs.AppendLine($"## {date}");
                    formattedLogs.AppendLine($"### {author}");
                    formattedLogs.AppendLine();

                    // Add the raw content, ensuring proper markdown formatting
                    if (!string.IsNullOrEmpty(log.Raw_Content))
                    {
                        string content = log.Raw_Content.Trim();

                        // Pre-process the content to fix markdown issues
                        content = PreProcessMarkdown(content);

                        formattedLogs.AppendLine(content);
                    }

                    formattedLogs.AppendLine();
                    formattedLogs.AppendLine("---");
                    formattedLogs.AppendLine();
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

        private string PreProcessMarkdown(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            var result = new StringBuilder();
            var lines = content.Replace("\r\n", "\n").Split('\n');
            bool inList = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    result.AppendLine();
                    inList = false;
                    continue;
                }

                // Handle headers - make sure they have no asterisks and proper spacing
                if (line.StartsWith("#") ||
                    (i > 0 && i < lines.Length - 1 &&
                    lines[i - 1].Trim().Length == 0 &&
                    !string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("•") && !line.StartsWith("*") && !line.StartsWith("-")))
                {
                    // Remove any asterisks from headers
                    line = line.Replace("*", "");
                    result.AppendLine(line);
                    result.AppendLine();
                    inList = false;
                    continue;
                }

                // Fix specific header issues
                if (line.Contains("Auto Idle and Auto-AFK Updates"))
                {
                    line = line.Replace("*Auto Idle and Auto-AFK Updates", "Auto Idle and Auto-AFK Updates");
                    result.AppendLine(line);
                    result.AppendLine();
                    inList = false;
                    continue;
                }

                if (line.Contains("Key Changes:"))
                {
                    result.AppendLine("### Key Changes:");
                    result.AppendLine();
                    inList = false;
                    continue;
                }

                // Handle bullet points - normalize to markdown list items
                if (line.Contains("•") || line.StartsWith("* ") || line.StartsWith("- "))
                {
                    // Clean up the line first
                    string cleanLine = line
                        .Replace("•\t", "")
                        .Replace("•", "")
                        .Replace("* ", "")
                        .Replace("- ", "")
                        .Trim();

                    // Remove any trailing asterisks and clean up
                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"([A-Za-z]+)\*", "$1");

                    // Remove any starting asterisks
                    cleanLine = System.Text.RegularExpressions.Regex.Replace(cleanLine, @"\*([A-Za-z]+)", "$1");

                    // Format specific items that should be clean
                    cleanLine = cleanLine
                        .Replace("The Idle", "The Idle")
                        .Replace("and AFK", "and AFK")
                        .Replace("systems", "systems")
                        .Replace("Idle now", "Idle now")
                        .Replace("AFK is", "AFK is")
                        .Replace("Trade (", "Trade (")
                        .Replace("Wearchange packets", "Wearchange packets")
                        .Replace("Buyers and", "Buyers and")
                        .Replace("Traders are", "Traders are");

                    // Format the bullet point properly
                    result.AppendLine($"- {cleanLine}");
                    inList = true;
                    continue;
                }

                // Handle regular text - ensure proper spacing
                if (!inList)
                {
                    // Process any remaining markdown formatting issues
                    line = System.Text.RegularExpressions.Regex.Replace(line, @"([A-Za-z]+)\*", "$1");
                    line = System.Text.RegularExpressions.Regex.Replace(line, @"\*([A-Za-z]+)", "$1");

                    result.AppendLine(line);
                }
                else
                {
                    // If this is text after a list item, indent it for proper formatting
                    result.AppendLine($"  {line}");
                }
            }

            return result.ToString();
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
                }

                using (var client = new HttpClient())
                {
                    // Set a timeout of 5 seconds for the changelog API request
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("x-patcher-token", token);
                    try
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Calling changelog API: {url}");
                        }

                        var httpResponse = await client.GetAsync(url);

                        if (httpResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            StatusLibrary.Log("[ERROR] Authentication failed with changelog API");
                            StatusLibrary.Log("Continuing....");
                            return;
                        }

                        if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            StatusLibrary.Log("[ERROR] Changelog API endpoint not found");
                            StatusLibrary.Log("Continuing....");
                            return;
                        }

                        if (!httpResponse.IsSuccessStatusCode)
                        {
                            StatusLibrary.Log("[ERROR] Failed to connect to changelog API");
                            StatusLibrary.Log("Continuing....");
                            return;
                        }

                        var response = await httpResponse.Content.ReadAsStringAsync();
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] API Response: {response}");
                        }

                        if (!string.IsNullOrEmpty(response))
                        {
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                AllowTrailingCommas = true
                            };

                            changelogResponse = JsonSerializer.Deserialize<ChangelogResponse>(response, options);
                            if (changelogResponse?.Status == "success" && changelogResponse.Changelogs != null)
                            {
                                if (changelogResponse.Total > 0)
                                {
                                    if (isDebugMode)
                                    {
                                        StatusLibrary.Log($"[DEBUG] Found {changelogResponse.Total} new changelog entries");
                                    }

                                    // Store the new changelogs separately
                                    var newChangelogs = changelogResponse.Changelogs.OrderBy(x => x.Timestamp).ToList();
                                    latestNewChangelogs = newChangelogs;

                                    // Load existing entries or create new list if file doesn't exist
                                    var entries = File.Exists(changelogPath) ? IniLibrary.LoadChangelog() : new List<Dictionary<string, string>>();

                                    // Remove the default entry if it exists
                                    entries.RemoveAll(e => e.ContainsKey("message_id") && e["message_id"] == "default");

                                    // Cache timezone info for formatting
                                    var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                                    // Add new entries at the end (append)
                                    foreach (var changelog in newChangelogs)
                                    {
                                        // Format the date in Eastern Time
                                        var estTime = TimeZoneInfo.ConvertTime(changelog.Timestamp, timeZone);
                                        var dateHeader = $"# {estTime:MMMM dd, yyyy}";
                                        var authorHeader = $"## {FormatAuthorName(changelog.Author)}";

                                        // Use content as-is since it's already markdown formatted
                                        string content = changelog.Raw_Content ?? "";

                                        // Store the raw content (which includes markdown) in the formatted_content field
                                        var formattedContent = $"{dateHeader}\n\n{authorHeader}\n\n{content}\n\n---";

                                        // Store the raw field if available for full content access
                                        entries.Add(new Dictionary<string, string>
                                        {
                                            ["raw_content"] = changelog.Raw_Content ?? "",
                                            ["formatted_content"] = formattedContent,
                                            ["author"] = changelog.Author,
                                            ["timestamp"] = changelog.Timestamp.ToString("O"),
                                            ["message_id"] = changelog.Message_Id,
                                            ["raw"] = changelog.Raw ?? ""
                                        });
                                    }

                                    if (isDebugMode)
                                    {
                                        StatusLibrary.Log($"[DEBUG] Saving {entries.Count} total changelog entries");
                                    }

                                    // Save updated changelog
                                    IniLibrary.SaveChangelog(entries);

                                    // Save pre-formatted markdown file
                                    var markdownContent = new StringBuilder();
                                    foreach (var entry in entries.OrderByDescending(e => DateTime.Parse(e["timestamp"])))
                                    {
                                        markdownContent.AppendLine(entry["formatted_content"]);
                                    }
                                    string markdownPath = Path.Combine(appPath, "changelog.md");
                                    File.WriteAllText(markdownPath, markdownContent.ToString());

                                    // Update changelogs list with ALL entries (existing + new)
                                    changelogs.Clear();
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
                                                var changelogInfo = new ChangelogInfo
                                                {
                                                    Timestamp = timestamp,
                                                    Author = author,
                                                    Formatted_Content = formattedContent,
                                                    Raw_Content = rawContent,
                                                    Message_Id = messageId
                                                };

                                                // Add Raw field if available
                                                if (entry.TryGetValue("raw", out var raw))
                                                {
                                                    changelogInfo.Raw = raw;
                                                }

                                                changelogs.Add(changelogInfo);
                                            }
                                        }
                                    }
                                    changelogNeedsUpdate = true;
                                    hasNewChangelogs = true;
                                }
                                else if (isDebugMode)
                                {
                                    StatusLibrary.Log("[DEBUG] No new changelog entries found");
                                }
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Handle timeout specifically
                        StatusLibrary.Log("[INFO] Changelog API request timed out after 5 seconds");
                        StatusLibrary.Log("Continuing without changelog updates...");
                    }
                    catch (Exception ex)
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Failed to check changelogs: {ex.Message}");
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Handle timeout specifically
                StatusLibrary.Log("[INFO] Changelog API request timed out after 5 seconds");
                StatusLibrary.Log("Continuing without changelog updates...");
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Changelog check failed: {ex.Message}");
                }
                StatusLibrary.Log("[ERROR] Failed to check for changelogs");
                StatusLibrary.Log("Continuing....");
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

        private void MemoryOptimizations_Click(object sender, RoutedEventArgs e)
        {
            // Close the optimizations panel
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;

            btnMemoryOptimizations.IsEnabled = false;
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqcfgPath = Path.Combine(eqPath, "eqclient.ini");

                // Rest of the method implementation
                // ...
            }
            catch (Exception ex)
            {
                // Exception handling
                // ...
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
            string fallbackUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download";
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
                progressBar.Visibility = Visibility.Visible;
                txtProgress.Visibility = Visibility.Visible;
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

                // Update progress bar periodically
                if (checkedFiles % 10 == 0 || checkedFiles == totalFilesCount)
                {
                    StatusLibrary.SetProgress((int)((double)checkedFiles / totalFilesCount * 10000));
                    if (checkedFiles % 100 == 0)
                    {
                        await Task.Delay(1); // Small delay to keep the UI responsive
                    }
                }

                // Skip the patcher itself
                if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip Memory.ini files
                if (entry.name.EndsWith("Memory.ini", StringComparison.OrdinalIgnoreCase))
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log($"[DEBUG] Skipping Memory.ini file: {entry.name}");
                    }
                    continue;
                }

                var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");

                // Safety check
                if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                {
                    StatusLibrary.Log($"[WARNING] Path {entry.name} might be outside of your EverQuest directory. Skipping check.");
                    continue;
                }

                // Special handling for specific file types
                bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                bool isMapFile = entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                 entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase);

                bool needsDownload = false;

                // Check if file exists
                if (!await Task.Run(() => File.Exists(path)))
                {
                    quickCheckPassed = false;
                    needsDownload = true;

                    // Limit the number of logged missing files to avoid spam
                    if (loggedFileCount < maxLoggedFiles)
                    {
                        if (isDinput8)
                        {
                            StatusLibrary.Log($"[Missing] dinput8.dll not found, will be downloaded");
                        }
                        else if (isMapFile)
                        {
                            // For map files, don't log each missing file to avoid spam
                            if (loggedFileCount == 0)
                            {
                                StatusLibrary.Log($"[Missing] Map file: {entry.name}");
                            }
                        }
                        else
                        {
                            StatusLibrary.Log($"[Missing] {entry.name}");
                        }
                        loggedFileCount++;
                    }
                    else if (loggedFileCount == maxLoggedFiles)
                    {
                        StatusLibrary.Log($"[Missing] And {filelist.downloads.Count - maxLoggedFiles - i + 1} other files...");
                        loggedFileCount++;
                    }
                }
                else
                {
                    // For quick check, only verify size of regular files and MD5 of critical files (dinput8.dll)
                    if (isDinput8)
                    {
                        // Always check MD5 of dinput8.dll because it's critical
                        string md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            quickCheckPassed = false;
                            needsDownload = true;

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] dinput8.dll MD5 mismatch. Expected: {entry.md5.ToUpper()}, Got: {md5.ToUpper()}");
                            }
                        }
                        else if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] dinput8.dll is up to date");
                        }
                    }
                    else
                    {
                        // For regular files, just check file size in quick mode
                        var fileInfo = new FileInfo(path);
                        if (fileInfo.Length != entry.size)
                        {
                            quickCheckPassed = false;
                            needsDownload = true;

                            if (loggedFileCount < maxLoggedFiles)
                            {
                                StatusLibrary.Log($"[Modified] {entry.name} (size mismatch)");
                                loggedFileCount++;
                            }
                            else if (loggedFileCount == maxLoggedFiles)
                            {
                                StatusLibrary.Log($"[Modified] And more files...");
                                loggedFileCount++;
                            }
                        }
                    }
                }

                // If the file needs downloading, add it to our lists
                if (needsDownload)
                {
                    if (!missingOrModifiedFiles.Any(f => f.name == entry.name))
                    {
                        missingOrModifiedFiles.Add(entry);
                    }

                    // Add to the global list of files to download if not already there
                    if (!filesToDownload.Any(f => f.name == entry.name))
                    {
                        filesToDownload.Add(entry);

                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Added {entry.name} to download queue");
                        }
                    }
                }
            }

            // Final progress update to ensure we show 100%
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = 100;
                txtProgress.Text = "100%";
            });
            await Task.Delay(250); // Small delay to let the UI update

            // Hide progress bar after quick check
            Dispatcher.Invoke(() =>
            {
                progressBar.Visibility = Visibility.Hidden;
                txtProgress.Visibility = Visibility.Hidden;
            });

            // If there are any files to download, show the patch button and hide play button
            if (filesToDownload.Count > 0)
            {
                StatusLibrary.Log($"Scan complete - found {filesToDownload.Count} file(s) that need to be patched.");
                Dispatcher.Invoke(() =>
                {
                    btnPatch.Visibility = Visibility.Visible;
                    btnPlay.Visibility = Visibility.Collapsed;
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
                    btnPlay.Visibility = Visibility.Visible;
                    btnPatch.Visibility = Visibility.Collapsed;
                });

                return;
            }

            // If full scan was requested, continue with MD5 checks
            if (fullScanOnly)
            {
                StatusLibrary.Log("Performing full file check...");
                Dispatcher.Invoke(() =>
                {
                    progressBar.Visibility = Visibility.Visible;
                    txtProgress.Visibility = Visibility.Visible;
                });

                bool allFilesIntact = true;
                checkedFiles = 0;
                loggedFileCount = 0;

                foreach (var entry in filelist.downloads)
                {
                    // Skip the patcher itself
                    if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Skip Memory.ini files
                    if (entry.name.EndsWith("Memory.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Skipping Memory.ini file in full scan: {entry.name}");
                        }
                        continue;
                    }

                    checkedFiles++;

                    // Update progress bar periodically
                    if (checkedFiles % 10 == 0 || checkedFiles == totalFilesCount)
                    {
                        StatusLibrary.SetProgress((int)((double)checkedFiles / totalFilesCount * 10000));
                        if (checkedFiles % 100 == 0)
                        {
                            await Task.Delay(1); // Small delay to keep the UI responsive
                        }
                    }

                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");

                    // Safety check
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        StatusLibrary.Log($"[WARNING] Path {entry.name} might be outside of your EverQuest directory. Skipping check.");
                        continue;
                    }

                    bool needsDownload = false;

                    // Check if file exists
                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        allFilesIntact = false;
                        needsDownload = true;

                        if (loggedFileCount < maxLoggedFiles)
                        {
                            StatusLibrary.Log($"[Missing] {entry.name}");
                            loggedFileCount++;
                        }
                        else if (loggedFileCount == maxLoggedFiles)
                        {
                            StatusLibrary.Log($"[Missing] And more files...");
                            loggedFileCount++;
                        }
                    }
                    else
                    {
                        // Do full MD5 check
                        string md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));

                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            allFilesIntact = false;
                            needsDownload = true;

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] MD5 mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {md5.ToUpper()}");
                            }

                            if (loggedFileCount < maxLoggedFiles)
                            {
                                StatusLibrary.Log($"[Modified] {entry.name} (MD5 mismatch)");
                                loggedFileCount++;
                            }
                            else if (loggedFileCount == maxLoggedFiles)
                            {
                                StatusLibrary.Log($"[Modified] And more files...");
                                loggedFileCount++;
                            }
                        }
                    }

                    // If the file needs downloading, add it to our download list
                    if (needsDownload && !filesToDownload.Any(f => f.name == entry.name))
                    {
                        filesToDownload.Add(entry);

                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Added {entry.name} to download queue (full scan)");
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
                    progressBar.Visibility = Visibility.Hidden;
                    txtProgress.Visibility = Visibility.Hidden;
                });

                // Report results
                if (filesToDownload.Count > 0)
                {
                    StatusLibrary.Log($"Full scan complete - {filesToDownload.Count} file(s) need updating");
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
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
                        btnPlay.Visibility = Visibility.Visible;
                        btnPatch.Visibility = Visibility.Collapsed;
                    });
                }
                else
                {
                    StatusLibrary.Log("Scan complete - some files may need updating");
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
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

        private void ShowAppropriateButtons()
        {
            Dispatcher.Invoke(() =>
            {
                if (filesToDownload.Count > 0)
                {
                    btnPatch.Visibility = Visibility.Visible;
                    btnPlay.Visibility = Visibility.Collapsed;
                    btnPatch.IsEnabled = true;
                    btnPatch.Content = "PATCH";
                    StatusLibrary.Log("Files need to be patched! Click PATCH to begin.");
                }
                else
                {
                    btnPlay.Visibility = Visibility.Visible;
                    btnPatch.Visibility = Visibility.Collapsed;
                    btnPlay.IsEnabled = true;
                    StatusLibrary.Log("All files are up to date. Press Play to begin.");
                }

                // If we're in auto-patch mode and there are files to download, start patching immediately
                if (isAutoPatch && filesToDownload.Count > 0 && !isNeedingSelfUpdate && !hasNewChangelogs)
                {
                    isPendingPatch = true;
                    Task.Delay(1000).ContinueWith(_ => StartPatch());
                }
                // If we're in auto-play mode and no files need to be patched, start the game immediately
                else if (isAutoPlay && filesToDownload.Count == 0 && !isNeedingSelfUpdate && !hasNewChangelogs)
                {
                    Task.Delay(1000).ContinueWith(_ => BtnPlay_Click(null, null));
                }
            });
        }

        // Add a new method for refreshing changelogs
        private void RefreshChangelogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Set DeleteChangelog to true to force refresh on next run
                if (IniLibrary.instance != null)
                {
                    IniLibrary.instance.DeleteChangelog = "true";
                    IniLibrary.Save();
                    StatusLibrary.Log("Changelog refresh has been scheduled for next launch.");

                    // Show confirmation dialog
                    MessageBox.Show("Changelogs will be refreshed the next time you start the patcher.",
                        "Refresh Scheduled", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to schedule changelog refresh: {ex.Message}");
                MessageBox.Show($"Failed to schedule changelog refresh: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ShouldRefreshChangelogs()
        {
            try
            {
                // If DeleteChangelog is already set to true, no need to check further
                if (IniLibrary.instance.DeleteChangelog != null &&
                    IniLibrary.instance.DeleteChangelog.ToLower() == "true")
                {
                    return true;
                }

                // Check if we've never refreshed before
                if (string.IsNullOrEmpty(IniLibrary.instance.LastChangelogRefresh))
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] No previous changelog refresh detected, scheduling refresh");
                    }
                    return true;
                }

                // Try to parse the last refresh timestamp
                if (DateTime.TryParse(IniLibrary.instance.LastChangelogRefresh, out DateTime lastRefresh))
                {
                    // Remove changelogRefreshInterval logic
                    // Always return false (no auto-refresh by interval)
                    return false;
                }
                else
                {
                    // If we can't parse the timestamp, schedule a refresh
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Could not parse previous changelog refresh timestamp, scheduling refresh");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Error in ShouldRefreshChangelogs: {ex.Message}");
                }
            }

            return false;
        }
        private void ChkEnableCpuAffinity_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            bool isEnabled = chkEnableCpuAffinity.IsChecked ?? false;
            IniLibrary.instance.EnableCpuAffinity = isEnabled ? "true" : "false";
            StatusLibrary.Log($"CPU Affinity {(isEnabled ? "enabled" : "disabled")} - EverQuest will{(isEnabled ? "" : " not")} run on 4 CPU cores for better stability");
            IniLibrary.Save();
        }
        private void ChkEnableChunkedPatch_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            // Invert logic: checkbox checked means "Use Single File Patching" (disable chunked patching)
            isChunkedPatchEnabled = !(chkEnableChunkedPatch.IsChecked ?? false);

            // Update the INI setting
            IniLibrary.instance.UseSingleFilePatch = !isChunkedPatchEnabled ? "true" : "false";
            IniLibrary.Save();

            if (chkEnableChunkedPatch.IsChecked ?? false)
            {
                // Show warning when selecting single file patching (slower method)
                CustomWarningBox.Show(
                    "Single file patching is slower and not recommended for most users. Only use this option if you experience issues with the default chunked patching method.",
                    "Performance Warning"
                );
            }

            StatusLibrary.Log($"Using {(!isChunkedPatchEnabled ? "single file" : "chunked")} patching method. {(!isChunkedPatchEnabled ? "(slower)" : "(faster)")}");
        }
        private async Task<bool> TryChunkedPatch(List<FileEntry> filesToPatch, string prefix)
        {
            try
            {
                StatusLibrary.Log("Requesting file list from server...");
                // Normalize file names for chunked patching - create clean "rof/file.txt" format paths
                var fileNames = new List<string>();
                var clientPrefix = "rof/"; // Use hardcoded prefix since that's all we support

                // Store the FileList object to process delete.txt later
                FileList filelist = null;

                // Get the filelist to process delete.txt
                string suffix = "rof"; // Since we're only supporting RoF/RoF2
                string webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
                string filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
                if (filelistResponse != "")
                {
                    StatusLibrary.Log($"Failed to fetch filelist from {webUrl}: {filelistResponse}");
                    return false;
                }

                // Parse the filelist
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

                foreach (var file in filesToPatch)
                {
                    string relativePath;
                    string name = file.name.Replace("\\", "/");

                    // Handle full GitHub URLs
                    if (name.Contains("github.com") && name.Contains("/master/"))
                    {
                        int masterIndex = name.IndexOf("/master/");
                        if (masterIndex > 0)
                        {
                            // Extract everything after "/master/"
                            relativePath = name.Substring(masterIndex + "/master/".Length);
                        }
                        else
                        {
                            // Fallback to just the filename if we can't find the pattern
                            relativePath = Path.GetFileName(name);
                        }
                    }
                    // Handle other URLs
                    else if (name.StartsWith("http://") || name.StartsWith("https://"))
                    {
                        var uri = new Uri(name);
                        relativePath = uri.AbsolutePath.TrimStart('/');
                        // If the path already contains the prefix, extract just the relevant part
                        if (relativePath.Contains("rof/"))
                        {
                            int rofIndex = relativePath.IndexOf("rof/");
                            relativePath = relativePath.Substring(rofIndex);
                        }
                        else
                        {
                            // Just the filename as last resort
                            relativePath = Path.GetFileName(relativePath);
                        }
                    }
                    else
                    {
                        // Strip any leading slashes
                        relativePath = name.TrimStart('/');
                    }

                    // Don't duplicate prefixes - remove "rof/" if it exists at the start
                    if (relativePath.StartsWith("rof/", StringComparison.OrdinalIgnoreCase))
                    {
                        // Keep it as is
                    }
                    else
                    {
                        // Add the prefix
                        relativePath = clientPrefix + relativePath;
                    }

                    // Ensure we have a clean path
                    fileNames.Add(relativePath);
                }

                // DEBUG: Output the chunked patch file list to disk for Postman testing - only in debug mode
                if (isDebugMode)
                {
                    try
                    {
                        var debugFileList = fileNames.Take(1000).ToList(); // Limit to 1000 for sanity
                        var debugJson = System.Text.Json.JsonSerializer.Serialize(new { files = debugFileList }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var debugPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "chunked_patch_filelist.json");
                        System.IO.File.WriteAllText(debugPath, debugJson);
                        StatusLibrary.Log($"[DEBUG] Wrote debug file list for Postman: {debugPath}");
                    }
                    catch (Exception ex)
                    {
                        StatusLibrary.Log($"[DEBUG] Failed to write debug file list: {ex.Message}");
                    }
                }

                var requestBody = new { files = fileNames };
                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                using (var client = new HttpClient())
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await client.PostAsync("https://patch.heroesjourneyemu.com/zip-chunks/init", content);
                    if (!response.IsSuccessStatusCode)
                    {
                        StatusLibrary.Log($"Server returned error: {response.StatusCode}");
                        return false;
                    }
                    var respString = await response.Content.ReadAsStringAsync();
                    // Parse the response as a JSON object with a "chunks" array
                    using var doc = System.Text.Json.JsonDocument.Parse(respString);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("chunks", out var chunksElem) || chunksElem.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        StatusLibrary.Log("No chunks array in server response.");
                        if (isDebugMode)
                        {
                            StatusLibrary.Log($"[DEBUG] Full server response: {respString}");
                        }
                        return false;
                    }
                    var zipUrls = new List<string>();
                    foreach (var chunk in chunksElem.EnumerateArray())
                    {
                        if (chunk.TryGetProperty("url", out var urlElem))
                        {
                            var url = urlElem.GetString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                // Prepend base URL if needed
                                if (url.StartsWith("/"))
                                    url = "https://patch.heroesjourneyemu.com" + url;
                                zipUrls.Add(url);
                            }
                        }
                    }
                    if (zipUrls.Count == 0)
                    {
                        StatusLibrary.Log("No file chunks returned from server.");
                        return false;
                    }

                    StatusLibrary.Log($"Downloading {zipUrls.Count} file chunk(s)...");
                    int chunkNum = 1;
                    int totalFilesExtracted = 0;

                    // Show progress bar for chunked patching
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Visibility = Visibility.Visible;
                        txtProgress.Visibility = Visibility.Visible;
                        progressBar.Value = 0;
                        txtProgress.Text = "0%";
                    });

                    // Variables to limit the number of map files logged (similar to regular patching)
                    int loggedMapFiles = 0;
                    const int maxLoggedMapFiles = 20;

                    foreach (var zipUrl in zipUrls)
                    {
                        if (isPatchCancelled) return false;

                        // User-friendly progress message for all users
                        StatusLibrary.Log($"Downloading chunk {chunkNum} of {zipUrls.Count}...");

                        // Update progress bar
                        int progressPercent = (int)((double)chunkNum / zipUrls.Count * 100);
                        StatusLibrary.SetProgress(progressPercent * 100); // Convert to 0-10000 range

                        string tempZip = Path.GetTempFileName();
                        try
                        {
                            // Debug-only detailed logging
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[ChunkedPatch][DEBUG] Downloading {zipUrl}");
                            }

                            // Show download started message with size info
                            using (var chunkResponse = await client.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
                            {
                                chunkResponse.EnsureSuccessStatusCode();

                                // Get file size if available
                                long? totalBytes = chunkResponse.Content.Headers.ContentLength;
                                string sizeInfo = totalBytes.HasValue ? $" ({generateSize(totalBytes.Value)})" : "";

                                if (!isDebugMode)
                                {
                                    StatusLibrary.Log($"Downloading chunk {chunkNum}/{zipUrls.Count}{sizeInfo}...");
                                }
                                using (var zipData = await chunkResponse.Content.ReadAsStreamAsync())
                                using (var fs = File.Create(tempZip))
                                {
                                    await zipData.CopyToAsync(fs);
                                }
                            }

                            // User-friendly extraction message
                            StatusLibrary.Log($"Extracting files from chunk {chunkNum} of {zipUrls.Count}...");

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[ChunkedPatch][DEBUG] Extracting chunk {chunkNum}...");
                            }

                            using (var archive = ZipFile.OpenRead(tempZip))
                            {
                                int filesExtracted = 0;
                                int processedFiles = 0;
                                int totalFiles = archive.Entries.Count;
                                foreach (var entry in archive.Entries)
                                {
                                    if (string.IsNullOrEmpty(entry.Name)) continue; // skip folders

                                    processedFiles++;

                                    // Strip the "rof/" prefix from the entry path for extraction
                                    string destinationPath = entry.FullName;
                                    if (destinationPath.StartsWith("rof/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        destinationPath = destinationPath.Substring(4); // Remove "rof/" prefix
                                    }

                                    string outPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), destinationPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                                    entry.ExtractToFile(outPath, true);

                                    // Check if file is a map file or other special type that should have limited logging
                                    bool isMapFile = entry.FullName.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                                 entry.FullName.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                                 entry.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                                    bool shouldLogFile = !isMapFile || (isMapFile && loggedMapFiles < maxLoggedMapFiles);

                                    // Verify the file was properly extracted by checking the file exists
                                    if (!File.Exists(outPath))
                                    {
                                        if (isDebugMode)
                                        {
                                            StatusLibrary.Log($"[DEBUG] Failed to extract: {entry.FullName}");
                                        }
                                        continue;
                                    }

                                    // Debug-only detailed path logging
                                    if (isDebugMode && filesExtracted <= 3) // Only log first few files in debug mode
                                    {
                                        StatusLibrary.Log($"[DEBUG] Extracted: {entry.FullName} -> {outPath}");
                                    }
                                    // Standard file logging similar to regular patching
                                    else if (shouldLogFile && !isDebugMode)
                                    {
                                        // Get the size of the extracted file
                                        long fileSize = new FileInfo(outPath).Length;
                                        StatusLibrary.Log($"{entry.FullName} ({generateSize(fileSize)})");
                                    }

                                    if (isMapFile)
                                    {
                                        loggedMapFiles++;

                                        // For map files, only log messages periodically
                                        if (loggedMapFiles == maxLoggedMapFiles)
                                        {
                                            StatusLibrary.Log("Additional map files are being installed...");
                                        }
                                        else if (loggedMapFiles % 50 == 0 && loggedMapFiles > maxLoggedMapFiles)
                                        {
                                            StatusLibrary.Log($"Installed {loggedMapFiles} map files so far");

                                            // Make sure UI is updated
                                            await Dispatcher.InvokeAsync(() =>
                                            {
                                                if (autoScroll)
                                                {
                                                    txtLog.ScrollToEnd();
                                                }
                                            });
                                        }
                                    }

                                    // Every 20 files for non-map files, provide a summary
                                    if (!isMapFile && processedFiles % 20 == 0)
                                    {
                                        StatusLibrary.Log($"Progress: {processedFiles}/{totalFiles} files processed in chunk {chunkNum} ({(int)(processedFiles * 100.0 / totalFiles)}%)");
                                    }

                                    filesExtracted++;
                                }

                                totalFilesExtracted += filesExtracted;
                                // Update UI for this chunk completion
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (autoScroll)
                                    {
                                        txtLog.ScrollToEnd();
                                    }
                                });
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempZip); } catch { }
                        }
                        chunkNum++;
                    }

                    // After downloading map files, show a summary
                    if (loggedMapFiles > maxLoggedMapFiles)
                    {
                        StatusLibrary.Log($"Installed a total of {loggedMapFiles} map files");
                    }

                    StatusLibrary.Log("All chunks downloaded and extracted.");
                    StatusLibrary.Log($"Total files installed: {totalFilesExtracted}");

                    // Add debug validation - verify that a sample of files actually exists on disk
                    if (isDebugMode && fileNames.Count > 0)
                    {
                        StatusLibrary.Log("Fast patch verification - checking files...");
                        int verifiedCount = 0;
                        int failedCount = 0;

                        // Check first 5 files at most for validation
                        foreach (var sampleFile in fileNames.Take(5))
                        {
                            // Always strip the rof/ prefix since files get installed to root
                            string localPath = sampleFile;
                            if (localPath.StartsWith("rof/", StringComparison.OrdinalIgnoreCase))
                                localPath = localPath.Substring(4); // Remove "rof/" prefix

                            string outPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), localPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                            if (File.Exists(outPath))
                            {
                                verifiedCount++;
                                StatusLibrary.Log($"Verified file: {Path.GetFileName(outPath)}");
                            }
                            else
                            {
                                failedCount++;
                                StatusLibrary.Log($"File not found: {Path.GetFileName(outPath)}");
                            }
                        }

                        StatusLibrary.Log($"[ChunkedPatch][DEBUG] File verification: {verifiedCount} found, {failedCount} not found");
                    }

                    // Process file deletions if any exist in filelist.deletes
                    if (filelist.deletes != null && filelist.deletes.Count > 0)
                    {
                        StatusLibrary.Log($"Checking for any file deletions...");
                        foreach (var entry in filelist.deletes)
                        {
                            if (isPatchCancelled)
                            {
                                StatusLibrary.Log("Patching cancelled.");
                                return false;
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
                                }
                            }
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ChunkedPatch] Error: {ex.Message}");
                return false;
            }
        }

        private static string GetMD5(string filePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private async Task EnsureLatestLogParserAsync()
        {
            string parserDir = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "THJ Log Parser");
            string parserExe = Path.Combine(parserDir, "THJLogParser.exe");
            string readmeFile = Path.Combine(parserDir, "readme.txt");

            // Ensure directory exists
            if (!Directory.Exists(parserDir))
                Directory.CreateDirectory(parserDir);

            // Get latest release info from GitHub API
            string apiUrl = "https://api.github.com/repos/BND10706/THJLogParser/releases/latest";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("THJPatcher/1.0");
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    StatusLibrary.Log("[LogParser] Could not check for latest log parser release.");
                    return;
                }
                var json = await response.Content.ReadAsStringAsync();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var assets = doc.RootElement.GetProperty("assets");
                string downloadUrl = null;
                string md5Url = null;
                string readmeUrl = null;

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name.Equals("THJLogParser.exe", StringComparison.OrdinalIgnoreCase))
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    else if (name.Equals("THJLogParser.exe.md5", StringComparison.OrdinalIgnoreCase))
                        md5Url = asset.GetProperty("browser_download_url").GetString();
                    else if (name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase))
                        readmeUrl = asset.GetProperty("browser_download_url").GetString();
                }

                if (downloadUrl == null || md5Url == null)
                {
                    StatusLibrary.Log("[LogParser] Could not find THJLogParser.exe or its .md5 in latest release.");
                    return;
                }

                // Download the .md5 file
                string remoteMd5 = null;
                try
                {
                    remoteMd5 = (await client.GetStringAsync(md5Url)).Trim().ToLowerInvariant();
                }
                catch
                {
                    StatusLibrary.Log("[LogParser] Failed to download .md5 file.");
                    return;
                }

                // Check if local file exists and compare MD5
                bool needsDownload = true;
                if (File.Exists(parserExe))
                {
                    try
                    {
                        string localMd5 = GetMD5(parserExe);
                        if (localMd5 == remoteMd5)
                        {
                            needsDownload = false;
                        }
                    }
                    catch
                    {
                        needsDownload = true;
                    }
                }

                if (needsDownload)
                {
                    StatusLibrary.Log("Downloading latest THJLogParser.exe...");
                    var exeBytes = await client.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(parserExe, exeBytes);
                    StatusLibrary.Log("THJLogParser.exe updated.");
                }
                else
                {
                    StatusLibrary.Log("THJLogParser.exe is up to date.");
                }                // Download readme.txt only if it doesn't exist locally
                if (readmeUrl != null && !File.Exists(readmeFile))
                {
                    try
                    {
                        var readmeContent = await client.GetStringAsync(readmeUrl);
                        await File.WriteAllTextAsync(readmeFile, readmeContent);
                    }
                    catch (Exception ex)
                    {
                        // Continue execution even if readme download fails
                    }
                }
                else if (readmeUrl == null)
                {
                }
                else
                {
                    // File already exists, no need to download
                    if (isDebugMode)
                    {
                    }
                }
            }
        }
    }
}
