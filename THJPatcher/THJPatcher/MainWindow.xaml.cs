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

namespace THJPatcher
{
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
        private bool isDebugMode = true;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            btnPatch.Click += BtnPatch_Click;
            btnPlay.Click += BtnPlay_Click;
            chkAutoPatch.Checked += ChkAutoPatch_CheckedChanged;
            chkAutoPlay.Checked += ChkAutoPlay_CheckedChanged;

            // Initialize server configuration
            serverName = "The Heroes Journey";
            if (string.IsNullOrEmpty(serverName))
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the server name is not provided or screenshot this message.");
                Close();
                return;
            }

            
            fileName = "heroesjourneyeq";

            filelistUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download/";
            if (string.IsNullOrEmpty(filelistUrl))
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the file list url is not provided or screenshot this message.", serverName);
                Close();
                return;
            }
            if (!filelistUrl.EndsWith("/")) filelistUrl += "/";

            patcherUrl = "https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download/";
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

        private void CloseOptimizations_Click(object sender, RoutedEventArgs e)
        {
            optimizationsPanel.Visibility = Visibility.Collapsed;
            logPanel.Visibility = Visibility.Visible;
        }

        private void Apply4GBPatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqExePath = Path.Combine(eqPath, "eqgame.exe");

                if (!File.Exists(eqExePath))
                {
                    MessageBox.Show("Could not find eqgame.exe", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // First check if patch is already applied
                if (Utilities.PEModifier.Is4GBPatchApplied(eqExePath))
                {
                    MessageBox.Show("4GB patch is already applied to eqgame.exe", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    btn4GBPatch.IsEnabled = false;
                    btn4GBPatch.ToolTip = "4GB patch is already applied to eqgame.exe";
                    return;
                }

                // Apply the patch
                if (Utilities.PEModifier.Apply4GBPatch(eqExePath))
                {
                    // Double-check if the patch was actually applied
                    if (Utilities.PEModifier.Is4GBPatchApplied(eqExePath))
                    {
                        MessageBox.Show("Successfully applied 4GB patch. A backup of the original file has been created as eqgame.exe.bak", 
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        btn4GBPatch.IsEnabled = false;
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

        private void OptimizeGraphics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string eqcfgPath = Path.Combine(eqPath, "eqclient.ini");

                if (File.Exists(eqcfgPath))
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
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string[] cacheDirs = { "dbstr", "maps" };

                foreach (string dir in cacheDirs)
                {
                    string path = Path.Combine(eqPath, dir);
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        Directory.CreateDirectory(path);
                    }
                }

                MessageBox.Show("Cache has been cleared", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string eqPath = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string[] configFiles = { "eqclient.ini", "eqclient_local.ini" };

                foreach (string file in configFiles)
                {
                    string path = Path.Combine(eqPath, file);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                MessageBox.Show("Settings have been reset. They will be recreated when you next launch EverQuest.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            isLoading = true;
            cts = new CancellationTokenSource();

            IniLibrary.Load();
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.IsChecked = isAutoPlay;
            chkAutoPatch.IsChecked = isAutoPatch;

            StatusLibrary.SubscribeProgress(new StatusLibrary.ProgressHandler((int value) =>
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = value / 100.0;
                });
            }));

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

            version = IniLibrary.instance.Version;

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

            // Skip self-update check in debug mode
            if (!isDebugMode)
            {
                // First check if we need to update the patcher
                string url = $"{patcherUrl}{fileName}-hash.txt";
                try
                {
                    StatusLibrary.Log("[DEBUG] Checking patcher version...");
                    var data = await UtilityLibrary.Download(cts, url);
                    string response = System.Text.Encoding.Default.GetString(data).ToUpper();
                    
                    if (response != "")
                    {
                        myHash = UtilityLibrary.GetMD5(System.Windows.Forms.Application.ExecutablePath);
                        StatusLibrary.Log($"[DEBUG] Comparing patcher hashes - Remote: {response}, Local: {myHash}");
                        if (response != myHash)
                        {
                            isNeedingSelfUpdate = true;
                            if (!isPendingPatch)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StatusLibrary.Log("[DEBUG] Patcher update needed");
                                    StatusLibrary.Log("Update available! Click PATCH to begin.");
                                    btnPatch.Visibility = Visibility.Visible;
                                    btnPlay.Visibility = Visibility.Collapsed;
                                });
                                return;
                            }
                        }
                        else
                        {
                            StatusLibrary.Log("[DEBUG] Patcher is up to date");
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"[DEBUG] Exception during patcher update check: {ex.Message}");
                }
            }
            else
            {
                StatusLibrary.Log("[DEBUG] Debug mode enabled - skipping patcher self-update check");
            }

            // Now check if game files need updating by comparing filelist version
            string suffix = "rof";
            string webUrl = $"{filelistUrl}{suffix}/filelist_{suffix}.yml";
            StatusLibrary.Log($"[DEBUG] Attempting to download filelist from: {webUrl}");
            string filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
            if (filelistResponse != "")
            {
                webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
                StatusLibrary.Log($"[DEBUG] First URL failed, trying alternate URL: {webUrl}");
                filelistResponse = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
                if (filelistResponse != "")
                {
                    StatusLibrary.Log($"Failed to fetch filelist from {webUrl}: {filelistResponse}");
                    return;
                }
            }

            // Read and check filelist version
            FileList filelist;
            string filelistPath = $"{Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)}\\filelist.yml";
            StatusLibrary.Log($"[DEBUG] Reading local filelist from: {filelistPath}");
            
            using (var input = File.OpenText(filelistPath))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializerBuilder.Deserialize<FileList>(input);
            }

            StatusLibrary.Log($"[DEBUG] Comparing versions - Filelist version: {filelist.version}, Last patched version: {IniLibrary.instance.LastPatchedVersion}");
            if (filelist.version != IniLibrary.instance.LastPatchedVersion)
            {
                if (!isPendingPatch)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusLibrary.Log("[DEBUG] Version mismatch detected - update needed");
                        StatusLibrary.Log("Update available! Click PATCH to begin.");
                        btnPatch.Visibility = Visibility.Visible;
                        btnPlay.Visibility = Visibility.Collapsed;
                    });
                    return;
                }
            }
            else
            {
                StatusLibrary.Log("[DEBUG] Versions match - no update needed");
            }

            // If we get here, no updates are needed
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
            /*
            if (!isDebugMode && myHash != "" && isNeedingSelfUpdate)
            {
                StatusLibrary.Log("Downloading update...");
                string url = $"{patcherUrl}/{fileName}.exe";
                try
                {
                    var data = await UtilityLibrary.Download(cts, url);
                    string localExePath = System.Windows.Forms.Application.ExecutablePath;
                    string localExeName = Path.GetFileName(localExePath);
                    StatusLibrary.Log($"[DEBUG] Saving update as: {localExeName}");
                    
                    if (File.Exists(localExePath + ".old"))
                    {
                        File.Delete(localExePath + ".old");
                    }
                    File.Move(localExePath, localExePath + ".old");
                    using (var w = File.Create(localExePath))
                    {
                        await w.WriteAsync(data, 0, data.Length, cts.Token);
                    }
                    StatusLibrary.Log($"Self update complete. New version will be used next run.");
                }
                catch (Exception e)
                {
                    StatusLibrary.Log($"Self update failed {url}: {e.Message}");
                }
                isNeedingSelfUpdate = false;
            }
            else if (isDebugMode)
            {
                StatusLibrary.Log("[DEBUG] Debug mode enabled - skipping patcher self-update");
            }
            */

            if (isPatchCancelled)
            {
                StatusLibrary.Log("Patching cancelled.");
                return;
            }

            // Get the client version suffix
            string suffix = "rof"; // Since we're only supporting RoF/RoF2

            // Download the filelist
            string webUrl = $"{filelistUrl}{suffix}/filelist_{suffix}.yml";
            string response = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
            if (response != "")
            {
                webUrl = $"{filelistUrl}/filelist_{suffix}.yml";
                response = await UtilityLibrary.DownloadFile(cts, webUrl, "filelist.yml");
                if (response != "")
                {
                    StatusLibrary.Log($"Failed to fetch filelist from {webUrl}: {response}");
                    return;
                }
            }

            // Parse the filelist
            FileList filelist;
            using (var input = File.OpenText($"{Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)}\\filelist.yml"))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializerBuilder.Deserialize<FileList>(input);
            }

            // Calculate total patch size
            double totalBytes = 0;
            double currentBytes = 1;
            double patchedBytes = 0;

            foreach (var entry in filelist.downloads)
            {
                totalBytes += entry.size;
            }
            if (totalBytes == 0) totalBytes = 1;

            // Download and patch files
            if (!filelist.downloadprefix.EndsWith("/")) filelist.downloadprefix += "/";
            foreach (var entry in filelist.downloads)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                StatusLibrary.SetProgress((int)(currentBytes / totalBytes * 10000));

                var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                if (!UtilityLibrary.IsPathChild(path))
                {
                    StatusLibrary.Log("Path " + path + " might be outside of your Everquest directory. Skipping download to this location.");
                    continue;
                }

                // Check if file exists and is already patched
                if (File.Exists(path))
                {
                    var md5 = UtilityLibrary.GetMD5(path);
                    if (md5.ToUpper() == entry.md5.ToUpper())
                    {
                        currentBytes += entry.size;
                        continue;
                    }
                }

                string url = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                string backupUrl = filelist.downloadprefix + entry.name.Replace("\\", "/");

                response = await UtilityLibrary.DownloadFile(cts, url, entry.name);
                if (response != "")
                {
                    response = await UtilityLibrary.DownloadFile(cts, backupUrl, entry.name);
                    if (response == "404")
                    {
                        StatusLibrary.Log($"Failed to download {entry.name} ({generateSize(entry.size)}) from {url} and {filelist.downloadprefix}, 404 error (website may be down?)");
                        return;
                    }
                }
                StatusLibrary.Log($"{entry.name} ({generateSize(entry.size)})");

                currentBytes += entry.size;
                patchedBytes += entry.size;
            }

            // Handle file deletions
            if (filelist.deletes != null && filelist.deletes.Count > 0)
            {
                foreach (var entry in filelist.deletes)
                {
                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (isPatchCancelled)
                    {
                        StatusLibrary.Log("Patching cancelled.");
                        return;
                    }
                    if (!UtilityLibrary.IsPathChild(path))
                    {
                        StatusLibrary.Log("Path " + entry.name + " might be outside your Everquest directory. Skipping deletion of this file.");
                        continue;
                    }
                    if (File.Exists(path))
                    {
                        StatusLibrary.Log("Deleting " + entry.name + "...");
                        File.Delete(path);
                    }
                }
            }

            StatusLibrary.SetProgress(10000);
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
            IniLibrary.Save();
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
                progressBar.Value = progress / 100.0;
                txtProgress.Text = $"{progress / 100}%";
            });
        }

        private void StatusLibrary_LogAdded(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.ScrollToEnd();
                txtLog.CaretIndex = txtLog.Text.Length;
                txtLog.Focus(); // Temporarily focus the text box
                txtLog.ScrollToEnd(); // Scroll to end again after focusing
                txtLog.CaretIndex = txtLog.Text.Length; // Set caret to end
                txtLog.Focusable = false; // Make it unfocusable to prevent focus issues
            });
        }

        private void InitializeOptimizationsPanel()
        {
            try
            {
                string eqExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "eqgame.exe");
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
                    btn4GBPatch.IsEnabled = false;
                    btn4GBPatch.ToolTip = "eqgame.exe not found";
                }
            }
            catch (Exception ex)
            {
                btn4GBPatch.IsEnabled = false;
                btn4GBPatch.ToolTip = "Error checking patch status: " + ex.Message;
            }
        }
    }
} 