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
                    "Gnomes are not snacks, stop asking...",
                    "Buffing in the Plane of Water… just kidding, no one goes there...",
                    "Undercutting your trader by 1 copper...",
                    "Emergency patch: Aporia found an exploit. Cata is 'fixing' it. Drake is taking cover...",
                    "'Balancing' triple-class builds...",
                    "Adding more duct tape to the database—should be fine...",
                    "Server hamster demands a raise, Aporia Refused...",
                    "'Balancing' pet builds...",
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
                    "Xegony's winds are howling… or is that just the lag?"
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
        private CancellationTokenSource cts;
        private Process process;
        private string myHash = "";
        private string patcherUrl;
        private string fileName;
        private string version;

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
        private readonly string changelogEndpoint = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/patcher/latest";
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

            // Check and initialize changelog first
            StatusLibrary.Log("Checking for changes...");
            await Task.Delay(1000); // Pause for effect
            
            // Load and display random message
            loadingMessages = LoadingMessages.CreateDefault();
            string randomMessage = GetRandomLoadingMessage();
            if (!string.IsNullOrEmpty(randomMessage))
            {
                StatusLibrary.Log(randomMessage);
                await Task.Delay(1000); // Pause after showing message
            }

            await CheckChangelogAsync();
            InitializeChangelogs();

            // Load configuration
            IniLibrary.Load();
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;
            version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

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
            
            if (isAutoPatch)
            {
                isPendingPatch = true;
                await Task.Delay(1000);
                StartPatch();
            }
            
            isLoading = false;
        }

        private async Task CheckForUpdates()
        {
            StatusLibrary.Log("Checking for updates...");
            await Task.Delay(2000);

            // Show latest changelog window if we have changelogs AND they're new
            if (changelogs.Any() && hasNewChangelogs)
            {
                StatusLibrary.Log("Showing latest changelog window");
                var latestChangelog = changelogs.OrderByDescending(x => x.Timestamp).First();
                var latestChangelogWindow = new LatestChangelogWindow(latestChangelog);
                latestChangelogWindow.ShowDialog();

                // Only proceed if the user acknowledged the changelog
                if (!latestChangelogWindow.IsAcknowledged)
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
                            if (!isPendingPatch)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StatusLibrary.Log("Patcher update needed");
                                    StatusLibrary.Log("Update available! Click PATCH to begin.");
                                    btnPatch.Visibility = Visibility.Visible;
                                    btnPlay.Visibility = Visibility.Collapsed;
                                });
                                return;
                            }
                        }
                        else
                        {
                            StatusLibrary.Log("Patcher is up to date");
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"[Error] Exception during patcher update check: {ex.Message}");
                }
            }
            else
            {
                StatusLibrary.Log("[DEBUG] Debug mode enabled - skipping patcher self-update check");
            }

            // Now check if game files need updating by comparing filelist version
            string suffix = "rof";
            string webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
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

            if (filelist.version != IniLibrary.instance.LastPatchedVersion)
            {
                if (!isPendingPatch)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusLibrary.Log("Update available! Click PATCH to begin.");
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                    });
                    return;
                }
            }
            else
            {
                StatusLibrary.Log("Up to date - no update needed");
            }

            // Perform file integrity check
            StatusLibrary.Log("Performing file integrity check...");
            List<FileEntry> missingOrModifiedFiles = new List<FileEntry>();

            foreach (var entry in filelist.downloads)
            {
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
                }
                else
                {
                    var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                    if (md5.ToUpper() != entry.md5.ToUpper())
                    {
                        StatusLibrary.Log($"Modified file detected: {entry.name}");
                        missingOrModifiedFiles.Add(entry);
                    }
                }
            }

            if (missingOrModifiedFiles.Count > 0)
            {
                StatusLibrary.Log($"Found {missingOrModifiedFiles.Count} files that need to be updated.");
                Dispatcher.Invoke(() =>
                {
                    StatusLibrary.Log("Update needed! Click PATCH to begin.");
                    btnPatch.Visibility = Visibility.Visible;
                    btnPlay.Visibility = Visibility.Collapsed;
                });
                return;
            }

            // If we get here, no updates are needed and all files are intact
            await Task.Delay(1000); 

            Dispatcher.Invoke(() =>
            {
                StatusLibrary.Log("Ready to play!");
                btnPatch.Visibility = Visibility.Collapsed;
                btnPlay.Visibility = Visibility.Visible;
            });
        }

        private void BtnPatch_Click(object sender, RoutedEventArgs e)
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
            StartPatch();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                process = UtilityLibrary.StartEverquest();
                if (process != null)
                    this.Close();
                else
                    MessageBox.Show("The process failed to start", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception err)
            {
                MessageBox.Show($"An error occurred while trying to start Everquest: {err.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void StartPatch()
        {
            if (isPatching) return;

            cts = new CancellationTokenSource();
            isPatchCancelled = false;
            txtLog.Clear();
            StatusLibrary.SetPatchState(true);
            isPatching = true;
            btnPatch.Background = _defaultButtonBrush;
            txtProgress.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            txtProgress.Text = "0%";
            StatusLibrary.Log("Patching in progress...");
            await Task.Delay(1000); // 1 second pause
            btnPatch.Visibility = Visibility.Collapsed;

            try
            {
                await AsyncPatch();
                
                if (!isPatchCancelled)
                {
                    await Task.Delay(1000);
                    StatusLibrary.Log("Patch complete! Ready to play!");
                    btnPlay.Visibility = Visibility.Visible;
                    txtProgress.Visibility = Visibility.Collapsed;
                    
                    if (isAutoPlay)
                    {
                        await Task.Delay(2000);
                        BtnPlay_Click(null, null);
                    }
                }
            }
            catch (Exception e)
            {
                await Task.Delay(1000);
                StatusLibrary.Log($"Exception during patch: {e.Message}");
                btnPatch.Visibility = Visibility.Visible;
                txtProgress.Visibility = Visibility.Collapsed;
            }

            StatusLibrary.SetPatchState(false);
            isPatching = false;
            isPatchCancelled = false;
            cts.Cancel();
        }

        private async Task AsyncPatch()
        {
            Stopwatch start = Stopwatch.StartNew();
            StatusLibrary.Log($"Patching with patcher version {version}...");
            StatusLibrary.SetProgress(0);

            // Handle self-update first if needed and not in debug mode
            if (!isDebugMode && myHash != "" && isNeedingSelfUpdate)
            {
                StatusLibrary.Log("Downloading update...");
                string url = $"{patcherUrl}/{fileName}.exe";
                try
                {
                    var data = await Task.Run(async () => await UtilityLibrary.Download(cts, url));
                    string localExePath = Process.GetCurrentProcess().MainModule.FileName;
                    string localExeName = Path.GetFileName(localExePath);
                    
                    if (File.Exists(localExePath + ".old"))
                    {
                        await Task.Run(() => File.Delete(localExePath + ".old"));
                    }
                    await Task.Run(() => File.Move(localExePath, localExePath + ".old"));
                    await Task.Run(async () =>
                    {
                        using (var w = File.Create(localExePath))
                        {
                            await w.WriteAsync(data, 0, data.Length, cts.Token);
                        }
                    });
                    StatusLibrary.Log($"Self update complete. New version will be used next run.");
                }
                catch (Exception e)
                {
                    StatusLibrary.Log($"Self update failed {url}: {e.Message}");
                }
                isNeedingSelfUpdate = false;
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
                    StatusLibrary.Log($"delete.txt ({generateSize(deleteData.Length)})");
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

                // Try primary download URL first
                string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                string response = await UtilityLibrary.DownloadFile(cts, url, entry.name);
                
                // If primary fails, try backup URL
                if (response != "")
                {
                    string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                    response = await UtilityLibrary.DownloadFile(cts, backupUrl, entry.name);
                    if (response != "")
                    {
                        StatusLibrary.Log($"Failed to download {entry.name} ({generateSize(entry.size)}): {response}");
                        continue;  // Skip this file but continue with others
                    }
                }

                // Verify the downloaded file's MD5
                if (!await Task.Run(() => File.Exists(path)))
                {
                    StatusLibrary.Log($"[Error] Failed to create file {entry.name}");
                    continue;
                }

                var downloadedMd5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
                if (downloadedMd5.ToUpper() != entry.md5.ToUpper())
                {
                    StatusLibrary.Log($"[Warning] MD5 mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {downloadedMd5.ToUpper()}");
                    continue;
                }

                StatusLibrary.Log($"{entry.name} ({generateSize(entry.size)})");
                currentBytes += entry.size;
                patchedBytes += entry.size;
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
                        }
                    }
                }
            }

            StatusLibrary.SetProgress(10000);
            
            // Update LastPatchedVersion and save configuration
            IniLibrary.instance.LastPatchedVersion = filelist.version;
            await Task.Run(() => IniLibrary.Save());

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
                txtProgress.Text = $"{(int)progressBar.Value}%";
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
            var dialog = new ChangelogWindow(changelogContent);
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
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("x-patcher-token", token);
                        try
                        {
                            var url = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/patcher/latest";
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

                            var latestResponse = await httpResponse.Content.ReadAsStringAsync();
                            if (!string.IsNullOrEmpty(latestResponse))
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    AllowTrailingCommas = true
                                };

                                var changelogData = JsonSerializer.Deserialize<ChangelogData>(latestResponse, options);
                                if (changelogData?.Found == true && changelogData.Changelog != null)
                                {
                                    if (changelogData.Changelog.Message_Id != currentMessageId)
                                    {
                                        // Load existing entries
                                        var entries = IniLibrary.LoadChangelog();
                                        
                                        // Add new entry at the beginning
                                        entries.Insert(0, new Dictionary<string, string>
                                        {
                                            ["raw_content"] = changelogData.Changelog.Raw_Content,
                                            ["formatted_content"] = changelogData.Changelog.Formatted_Content,
                                            ["author"] = changelogData.Changelog.Author,
                                            ["timestamp"] = changelogData.Changelog.Timestamp.ToString("O"),
                                            ["message_id"] = changelogData.Changelog.Message_Id
                                        });

                                        // Save updated changelog
                                        IniLibrary.SaveChangelog(entries);
                                        
                                        // Update changelogs list and set flag
                                        changelogs.Insert(0, changelogData.Changelog);
                                        hasNewChangelogs = true;
                                    }
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
                        var url = "https://thj-patcher-gsgvaxf0ehcegjdu.eastus2-01.azurewebsites.net/changelog?all=true";
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

        private async Task LoadLoadingMessages()
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
    }
}
