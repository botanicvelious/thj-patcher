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
        public int Total { get; set; }
        public List<ChangelogInfo> Changelogs { get; set; }
    }

    public class ChangelogInfo
    {
        [JsonPropertyName("raw_content")]
        public string Raw_Content { get; set; }
        
        [JsonPropertyName("formatted_content")]
        public string Formatted_Content { get; set; }
        
        [JsonPropertyName("author")]
        public string Author { get; set; }
        
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonPropertyName("message_id")]
        public string Message_Id { get; set; }
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
        private readonly string changelogEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog/{0}";
        private readonly string allChangelogsEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog?all=true";
        private readonly string patcherToken;
        private bool hasNewChangelogs = false;

        private bool autoScroll = true;

        private LoadingMessages loadingMessages;
        private Random random = new Random();

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

            filelistUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download";
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

            // Check server status
            await CheckServerStatus();

            // Load and display random message
            LoadLoadingMessages();
            string randomMessage = GetRandomLoadingMessage();
            if (!string.IsNullOrEmpty(randomMessage))
            {
                StatusLibrary.Log(randomMessage);
                await Task.Delay(1000); // Pause after showing message
            }

            // Load configuration
            IniLibrary.Load();
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;
            
            // Get the full version including build number
            var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            version = $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
            
            // Set the window as the data context for version binding
            this.DataContext = this;

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

            // Check for updates
            await CheckForUpdates();
            
            // If we're in auto-patch mode, start patching (but not for self-updates)
            if (isAutoPatch && !isNeedingSelfUpdate)
            {
                isPendingPatch = true;
                await Task.Delay(1000);
                await StartPatch();
            }
            
            isLoading = false;
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
            catch (Exception ex)
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Server status check error: {ex.Message}");
                }
                // Silently fail - we don't want to show errors for this
            }
        }

        private async Task CheckForUpdates()
        {
            if (isSilentMode)
            {
                Console.WriteLine("Starting silent update check...");
            }
            StatusLibrary.Log("Checking for updates...");
            await Task.Delay(2000);

            // Check for and show changelog if needed
            await CheckChangelogAsync();
            if (changelogs.Any() && hasNewChangelogs && !isSilentMode)
            {
                StatusLibrary.Log("Showing changelog window");
                bool acknowledged = ShowChangelogs();

                // Only proceed if the user acknowledged the changelog or we're in auto-confirm mode
                if (!acknowledged && !isAutoConfirm)
                {
                    return;
                }
            }

            // Show latest changelog window if we have changelogs AND they're new AND not in silent mode
            if (changelogs.Any() && hasNewChangelogs && !isSilentMode)
            {
                StatusLibrary.Log("Showing changelog window");
                bool acknowledged = ShowChangelogs();

                // Only proceed if the user acknowledged the changelog or we're in auto-confirm mode
                if (!acknowledged && !isAutoConfirm)
                {
                    return;
                }
            }

            // Skip self-update check in debug mode
            if (!isDebugMode)
            {
                // First check if we need to update the patcher
                string url = $"{patcherUrl}{fileName}-hash.txt";
                try
                {
                    StatusLibrary.Log("Checking patcher version...");
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

            // Now check if game files need updating by comparing filelist version
            string suffix = "rof";
            string webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
            if (isDebugMode)
            {
                StatusLibrary.Log($"[DEBUG] Checking filelist from URL: {webUrl}");
            }
            string filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
            if (filelistResponse != "")
            {
                StatusLibrary.Log($"Failed to fetch filelist from {webUrl}: {filelistResponse}");
                return;
            }

            // Read and check filelist version
            FileList filelist;
            string filelistPath = $"{Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)}\\filelist.yml";
            
            using (var input = File.OpenText(filelistPath))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializerBuilder.Deserialize<FileList>(input);
            }

            if (isDebugMode)
            {
                StatusLibrary.Log($"[DEBUG] Current filelist version: {filelist.version}");
                StatusLibrary.Log($"[DEBUG] Last patched version: {IniLibrary.instance.LastPatchedVersion}");
                StatusLibrary.Log($"[DEBUG] Version comparison: {filelist.version} == {IniLibrary.instance.LastPatchedVersion}");
                StatusLibrary.Log($"[DEBUG] Config file path: {Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "thjpatcher.yml")}");
            }

            // First do a quick check (file existence and size only)
            StatusLibrary.Log("Performing quick file check...");
            txtProgress.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            bool quickCheckPassed = true;
            List<FileEntry> missingOrModifiedFiles = new List<FileEntry>();

            int totalFiles = filelist.downloads.Count;
            int checkedFiles = 0;

            foreach (var entry in filelist.downloads)
            {
                checkedFiles++;
                // Update progress bar (0-100 range)
                int progress = (int)((double)checkedFiles / totalFiles * 100);
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = progress;
                    txtProgress.Text = $"Quick scan: {progress}%";
                });

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

                if (!await Task.Run(() => File.Exists(path)))
                {
                    StatusLibrary.Log($"Missing file detected: {entry.name}");
                    missingOrModifiedFiles.Add(entry);
                    quickCheckPassed = false;
                }
                else
                {
                    var fileInfo = await Task.Run(() => new FileInfo(path));
                    if (fileInfo.Length != entry.size)
                    {
                        StatusLibrary.Log($"File size mismatch detected: {entry.name}");
                        missingOrModifiedFiles.Add(entry);
                        quickCheckPassed = false;
                    }
                }
            }

            // Hide progress bar after quick check
            Dispatcher.Invoke(() =>
            {
                txtProgress.Visibility = Visibility.Collapsed;
                progressBar.Value = 0;
            });

            // Determine if we need to do a full MD5 check
            bool needsFullCheck = false;
            DateTime lastCheck = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(IniLibrary.instance.LastIntegrityCheck))
            {
                lastCheck = DateTime.Parse(IniLibrary.instance.LastIntegrityCheck);
            }

            // Check if we need a full integrity check
            if (!quickCheckPassed || 
                filelist.version != IniLibrary.instance.LastPatchedVersion ||
                (DateTime.UtcNow - lastCheck).TotalHours >= 24)
            {
                needsFullCheck = true;
                if (isDebugMode)
                {
                    StatusLibrary.Log($"[DEBUG] Full check needed: QuickCheck={quickCheckPassed}, VersionMatch={filelist.version == IniLibrary.instance.LastPatchedVersion}, HoursSinceLastCheck={(DateTime.UtcNow - lastCheck).TotalHours}");
                }
            }

            // If we need a full check, do it now
            if (needsFullCheck)
            {
                StatusLibrary.Log("Performing full file integrity check...");
                txtProgress.Visibility = Visibility.Visible;
                progressBar.Value = 0;
                bool allFilesIntact = true;
                checkedFiles = 0;

                foreach (var entry in filelist.downloads)
                {
                    checkedFiles++;
                    int progress = (int)((double)checkedFiles / totalFiles * 100);
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = progress;
                        txtProgress.Text = $"Checking files: {progress}%";
                    });

                    if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        continue;
                    }

                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        StatusLibrary.Log($"Missing file detected: {entry.name}");
                        missingOrModifiedFiles.Add(entry);
                        allFilesIntact = false;
                    }
                    else
                    {
                        var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            StatusLibrary.Log($"Modified file detected: {entry.name}");
                            missingOrModifiedFiles.Add(entry);
                            allFilesIntact = false;
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

                // If all files are intact and LastPatchedVersion is empty, set it to current version
                if (allFilesIntact && string.IsNullOrEmpty(IniLibrary.instance.LastPatchedVersion))
                {
                    if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] All files intact but LastPatchedVersion is empty - setting to current version");
                    }
                    IniLibrary.instance.LastPatchedVersion = filelist.version;
                    await Task.Run(() => IniLibrary.Save());
                    StatusLibrary.Log("Up to date - no update needed");
                    
                    // Only show the play button if there's no self-update pending
                    if (!isNeedingSelfUpdate)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            btnPatch.Visibility = Visibility.Collapsed;
                            btnPlay.Visibility = Visibility.Visible;
                        });
                    }
                    else if (isDebugMode)
                    {
                        StatusLibrary.Log("[DEBUG] Quick check passed but self-update is pending - keeping patch button visible");
                    }
                    return;
                }

                // Only show update button if versions don't match or files are not intact
                if (filelist.version != IniLibrary.instance.LastPatchedVersion || !allFilesIntact)
                {
                    if (!isPendingPatch)
                    {
                        if (isDebugMode)
                        {
                            StatusLibrary.Log("[DEBUG] Version mismatch or files not intact - showing update button");
                        }
                        Dispatcher.Invoke(() =>
                        {
                            StatusLibrary.Log("Update available! Click PATCH to begin.");
                            btnPatch.Visibility = Visibility.Visible;
                            btnPlay.Visibility = Visibility.Collapsed;
                        });
                        // In silent mode, automatically start patching
                        if (isSilentMode && isAutoConfirm)
                        {
                            await StartPatch();
                        }
                        return;
                    }
                }
            }
            else
            {
                if (isDebugMode)
                {
                    StatusLibrary.Log("[DEBUG] Quick check passed - skipping full integrity check");
                }
                StatusLibrary.Log("Quick check complete.");
                
                // Only show the play button if there's no self-update pending
                if (!isNeedingSelfUpdate)
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnPatch.Visibility = Visibility.Collapsed;
                        btnPlay.Visibility = Visibility.Visible;
                    });
                }
                // Skip second changelog check for quick checks
                return;
            }

            // Check for and show changelog if needed
            await CheckChangelogAsync();
            if (changelogs.Any() && hasNewChangelogs && !isSilentMode)
            {
                StatusLibrary.Log("Showing changelog window");
                bool acknowledged = ShowChangelogs();

                // Only proceed if the user acknowledged the changelog or we're in auto-confirm mode
                if (!acknowledged && !isAutoConfirm)
                {
                    return;
                }
            }

            // In silent mode, automatically start the game
            if (isSilentMode && isAutoConfirm)
            {
                await Task.Delay(2000); // Give a small delay to show completion
                BtnPlay_Click(null, null);
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

            // Calculate total files to check
            int totalFiles = filelist.downloads.Count;
            int checkedFiles = 0;
            List<FileEntry> filesToDownload = new List<FileEntry>();

            // First scan - check all files
            StatusLibrary.Log("Scanning files...");
            foreach (var entry in filelist.downloads)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Scanning cancelled.");
                    return;
                }

                StatusLibrary.SetProgress((int)((double)checkedFiles / totalFiles * 10000));
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

            double currentBytes = 1;
            double patchedBytes = 0;
            bool hasErrors = false;

            // If no files need downloading, we're done
            if (filesToDownload.Count == 0)
            {
                StatusLibrary.Log("All files are up to date.");
                StatusLibrary.SetProgress(10000);
                return;
            }

            StatusLibrary.Log($"Found {filesToDownload.Count} files to update.");
            await Task.Delay(1000); // Pause to show the message

            // Download and patch files
            if (!filelist.downloadprefix.EndsWith("/")) filelist.downloadprefix += "/";
            foreach (var entry in filesToDownload)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                StatusLibrary.SetProgress((int)(currentBytes / totalBytes * 10000));

                var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                
                // Create directory if it doesn't exist
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Skip DLL files that are currently in use
                if (entry.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Try to open the file for writing to check if it's locked
                        using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we get here, the file isn't locked
                            fs.Close();
                        }
                    }
                    catch (IOException)
                    {
                        StatusLibrary.Log($"[Warning] Skipping {entry.name} - file is currently in use");
                        continue;
                    }
                }

                bool downloadSuccess = false;
                int retryCount = 0;
                const int maxRetries = 3;

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
                            StatusLibrary.Log($"Failed to download {entry.name} ({generateSize(entry.size)}): {response}");
                            retryCount++;
                            continue;
                        }
                    }

                    // Verify the downloaded file's MD5
                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        StatusLibrary.Log($"[Error] Failed to create file {entry.name}");
                        retryCount++;
                        continue;
                    }

                    var downloadedMd5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                    if (downloadedMd5.ToUpper() != entry.md5.ToUpper())
                    {
                        StatusLibrary.Log($"[Warning] MD5 mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {downloadedMd5.ToUpper()}");
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            StatusLibrary.Log($"Retrying download of {entry.name} (attempt {retryCount + 1}/{maxRetries})...");
                            await Task.Delay(1000); // Wait a bit before retrying
                            continue;
                        }
                        hasErrors = true;
                        break;
                    }

                    downloadSuccess = true;
                    StatusLibrary.Log($"{entry.name} ({generateSize(entry.size)})");
                    currentBytes += entry.size;
                    patchedBytes += entry.size;
                }

                if (!downloadSuccess)
                {
                    hasErrors = true;
                    StatusLibrary.Log($"[Error] Failed to download and verify {entry.name} after {maxRetries} attempts");
                }
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

            StatusLibrary.SetProgress(10000);
            
            // Update LastPatchedVersion and save configuration
            IniLibrary.instance.LastPatchedVersion = filelist.version;
            await Task.Run(() => IniLibrary.Save());

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
                else
                {
                    StatusLibrary.Log("[Error] No entries found in changelog file, using default entry");
                    // Fallback to default changelog if no yml file exists
                    changelogs.Add(new ChangelogInfo
                    {
                        Timestamp = DateTime.Now,
                        Author = "System",
                        Formatted_Content = "Welcome to The Heroes' Journey!\n\nNo changelog entries have been loaded yet. Please check back later.",
                        Raw_Content = "Welcome to The Heroes' Journey!\n\nNo changelog entries have been loaded yet. Please check back later.",
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
                changelogContent = "";
                foreach (var log in changelogs.OrderByDescending(x => x.Timestamp))
                {
                    changelogContent += FormatChangelog(log) + "\n---\n\n";
                }

                if (string.IsNullOrWhiteSpace(changelogContent))
                {
                    changelogContent = "No changelog entries available.";
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"[ERROR] Failed to format changelogs: {ex.Message}");
                changelogContent = "Error loading changelog entries.";
            }
        }

        private string FormatChangelog(ChangelogInfo changelog)
        {
            // Just use the formatted content directly since it already contains the headers
            return changelog.Formatted_Content.Trim();
        }

        private void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            // When clicking the changelog button, show all changelogs
            var combinedContent = new StringBuilder();
            foreach (var changelog in changelogs)
            {
                combinedContent.AppendLine(changelog.Formatted_Content);
            }

            var dialog = new ChangelogWindow(combinedContent.ToString());
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

                if (File.Exists(changelogPath))
                {
                    // Get the latest message_id from yml
                    string currentMessageId = IniLibrary.GetLatestMessageId();

                    // Check for new changelog
                    using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }))
                    {
                        client.DefaultRequestHeaders.Add("x-patcher-token", token);
                        try
                        {
                            // If we have a changelog.yml, always use the incremental endpoint
                            var endpoint = string.Format(changelogEndpoint, currentMessageId);

                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Requesting changelog from: {endpoint}");
                            }

                            var httpResponse = await client.GetAsync(endpoint);
                            
                            if (isDebugMode)
                            {
                                StatusLibrary.Log($"[DEBUG] Changelog API Status Code: {httpResponse.StatusCode}");
                                StatusLibrary.Log($"[DEBUG] Changelog API Headers: {string.Join(", ", httpResponse.Headers.Select(h => $"{h.Key}: {string.Join("; ", h.Value)}"))}");
                                var debugResponse = await httpResponse.Content.ReadAsStringAsync();
                                StatusLibrary.Log($"[DEBUG] Changelog API Response: {debugResponse}");
                            }
                            
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
                            if (!string.IsNullOrEmpty(response))
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    AllowTrailingCommas = true
                                };

                                var changelogResponse = JsonSerializer.Deserialize<ChangelogResponse>(response, options);
                                if (changelogResponse?.Changelogs != null && changelogResponse.Changelogs.Count > 0)
                                {
                                    // Load existing entries
                                    var entries = IniLibrary.LoadChangelog();
                                    
                                    // Add new entries at the beginning in reverse order (to maintain chronological order)
                                    foreach (var changelog in changelogResponse.Changelogs.OrderByDescending(c => c.Timestamp))
                                    {
                                        entries.Insert(0, new Dictionary<string, string>
                                        {
                                            ["raw_content"] = changelog.Raw_Content,
                                            ["formatted_content"] = changelog.Formatted_Content,
                                            ["author"] = changelog.Author,
                                            ["timestamp"] = changelog.Timestamp.ToString("O"),
                                            ["message_id"] = changelog.Message_Id
                                        });
                                    }

                                    // Save updated changelog
                                    IniLibrary.SaveChangelog(entries);
                                    
                                    // Update changelogs list and set flag
                                    changelogs.InsertRange(0, changelogResponse.Changelogs);
                                    hasNewChangelogs = true;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            StatusLibrary.Log("[ERROR] Failed to connect to changelog API");
                            StatusLibrary.Log("Continuing....");
                        }
                    }
                    return;
                }

                // If we get here, no yml exists - fetch all changelogs
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-patcher-token", token);
                    try
                    {
                        var httpResponse = await client.GetAsync(allChangelogsEndpoint);
                        
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

                        var allResponse = await httpResponse.Content.ReadAsStringAsync();
                        if (!string.IsNullOrEmpty(allResponse))
                        {
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                AllowTrailingCommas = true
                            };
                            
                            var changelogResponse = JsonSerializer.Deserialize<ChangelogResponse>(allResponse, options);
                            if (changelogResponse?.Changelogs != null && changelogResponse.Changelogs.Count > 0)
                            {
                                // Convert changelogs to the format expected by IniLibrary.SaveChangelog
                                var entries = changelogResponse.Changelogs.Select(c => new Dictionary<string, string>
                                {
                                    ["raw_content"] = c.Raw_Content,
                                    ["formatted_content"] = c.Formatted_Content,
                                    ["author"] = c.Author,
                                    ["timestamp"] = c.Timestamp.ToString("O"),
                                    ["message_id"] = c.Message_Id
                                }).ToList();

                                // Save the API response to changelog.yml
                                IniLibrary.SaveChangelog(entries);

                                // Update the changelogs list immediately and set flag
                                changelogs.Clear();
                                changelogs.AddRange(changelogResponse.Changelogs);
                                hasNewChangelogs = true;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        StatusLibrary.Log("[ERROR] Failed to connect to changelog API");
                        StatusLibrary.Log("Continuing....");
                    }
                }
            }
            catch (Exception)
            {
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

        private bool ShowChangelogs()
        {
            if (hasNewChangelogs && !isSilentMode)
            {
                // Get only the new changelogs (they are at the start of the list since we insert at index 0)
                var newChangelogCount = changelogs.TakeWhile(c => !IniLibrary.GetLatestMessageId().Equals(c.Message_Id)).Count();
                if (newChangelogCount == 0) newChangelogCount = 1; // Show at least the latest if no previous message ID

                // Build combined changelog content from new changelogs only
                var combinedContent = new StringBuilder();
                foreach (var changelog in changelogs.Take(newChangelogCount))
                {
                    combinedContent.AppendLine(changelog.Formatted_Content);
                }

                var dialog = new ChangelogWindow(combinedContent.ToString());
                dialog.ShowDialog();
                hasNewChangelogs = false;
                return dialog.IsAcknowledged;
            }
            return true;
        }
    }
}
