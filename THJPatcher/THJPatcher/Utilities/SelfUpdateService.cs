using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service responsible for handling patcher self-updates
    /// </summary>
    public class SelfUpdateService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        private readonly Action<int> _progressAction;
        private readonly string _patcherUrl;
        private readonly string _fileName;
        
        /// <summary>
        /// Initializes a new instance of the SelfUpdateService
        /// </summary>
        /// <param name="isDebugMode">Whether debug mode is enabled</param>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="progressAction">Action to update progress</param>
        /// <param name="patcherUrl">URL for patcher downloads</param>
        /// <param name="fileName">Filename of the patcher executable</param>
        public SelfUpdateService(
            bool isDebugMode,
            Action<string> logAction,
            Action<int> progressAction,
            string patcherUrl,
            string fileName)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _progressAction = progressAction ?? (progress => { /* No progress if null */ });
            _patcherUrl = patcherUrl;
            _fileName = fileName;
        }
        
        /// <summary>
        /// Checks if a patcher update is available
        /// </summary>
        /// <param name="cts">Cancellation token source</param>
        /// <returns>A tuple containing a boolean indicating if an update is available and the current hash</returns>
        public async Task<(bool isUpdateAvailable, string currentHash)> CheckForUpdateAsync(CancellationTokenSource cts)
        {
            string myHash = "";
            
            _logAction("Checking patcher version...");

            string url = $"{_patcherUrl}{_fileName}-hash.txt";
            try
            {
                var data = await UtilityLibrary.Download(cts, url, null);
                string response = Encoding.Default.GetString(data).Trim().ToUpperInvariant();

                if (!string.IsNullOrEmpty(response))
                {
                    myHash = UtilityLibrary.GetMD5(System.Windows.Forms.Application.ExecutablePath).ToUpperInvariant();
                    
                    if (_isDebugMode)
                    {
                        _logAction($"[DEBUG] Remote hash: {response}");
                        _logAction($"[DEBUG] Local hash:  {myHash}");
                    }
                    
                    if (response != myHash)
                    {
                        _logAction("Patcher update available! Click PATCH to begin.");
                        return (true, myHash);
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Error] Failed to check patcher version: {ex.Message}");
            }
            
            return (false, myHash);
        }
        
        /// <summary>
        /// Performs a self-update of the patcher
        /// </summary>
        /// <param name="cts">Cancellation token source</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task PerformUpdateAsync(CancellationTokenSource cts)
        {
            try
            {
                DateTime start = DateTime.Now;
                _logAction($"Starting patcher update at {start.ToString("HH:mm:ss")}");

                string url = $"{_patcherUrl}{_fileName}";
                string hashUrl = $"{_patcherUrl}{_fileName}-hash.txt";
                string changelogUrl = $"{_patcherUrl}patcher_changelog.md";

                // Download the new patcher executable
                _logAction("Downloading new patcher version...");
                byte[] data = await UtilityLibrary.Download(cts, url, 
                    (bytesRead, totalBytes) => {
                        int progressPercentage = (int)((double)bytesRead / totalBytes * 8000);
                        _progressAction(progressPercentage); // 0-80% for main download
                    });
                if (data == null || data.Length == 0 || cts.IsCancellationRequested)
                {
                    _logAction("Failed to download patcher update.");
                    return;
                }

                // Download the hash file
                _logAction("Verifying file integrity...");
                byte[] hashData = await UtilityLibrary.Download(cts, hashUrl, 
                    (bytesRead, totalBytes) => {
                        int progressPercentage = 8000 + (int)((double)bytesRead / totalBytes * 1000);
                        _progressAction(progressPercentage); // 80-90% for hash
                    });
                if (hashData == null || hashData.Length == 0 || cts.IsCancellationRequested)
                {
                    _logAction("Failed to download hash file.");
                    return;
                }

                // Parse the hash
                string serverHash = Encoding.Default.GetString(hashData).Trim().ToUpperInvariant();
                
                // Create a unique temp filename
                string tempFile = $"temp_{DateTime.Now.Ticks}.exe";
                string tempPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), tempFile);
                
                // Save the downloaded executable
                _logAction("Saving new patcher version...");
                File.WriteAllBytes(tempPath, data);
                
                // Verify the downloaded file's hash
                string downloadedHash = UtilityLibrary.GetMD5(tempPath).ToUpperInvariant();
                if (downloadedHash != serverHash)
                {
                    _logAction($"[ERROR] Hash verification failed. Expected: {serverHash}, Got: {downloadedHash}");
                    File.Delete(tempPath);
                    return;
                }
                
                // Delete any existing .old backup
                string oldFile = System.Windows.Forms.Application.ExecutablePath + ".old";
                if (File.Exists(oldFile))
                {
                    try
                    {
                        File.Delete(oldFile);
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[Warning] Could not delete old backup: {ex.Message}");
                    }
                }
                
                // Rename current executable to .old
                _logAction("Creating backup of current patcher...");
                try
                {
                    File.Move(System.Windows.Forms.Application.ExecutablePath, oldFile);
                }
                catch (Exception ex)
                {
                    _logAction($"[ERROR] Could not create backup: {ex.Message}");
                    File.Delete(tempPath);
                    return;
                }
                
                // Rename downloaded file to original name
                _logAction("Installing new patcher version...");
                try
                {
                    File.Move(tempPath, System.Windows.Forms.Application.ExecutablePath);
                }
                catch (Exception ex)
                {
                    _logAction($"[ERROR] Could not install new version: {ex.Message}");
                    // Try to restore old version
                    try
                    {
                        File.Move(oldFile, System.Windows.Forms.Application.ExecutablePath);
                        _logAction("Restored previous version.");
                    }
                    catch
                    {
                        _logAction("[CRITICAL] Failed to restore previous version. Patcher may be unusable.");
                    }
                    return;
                }
                
                // Download patcher changelog
                try
                {
                    byte[] changelogData = await UtilityLibrary.Download(cts, changelogUrl, 
                        (bytesRead, totalBytes) => {
                            int progressPercentage = 9000 + (int)((double)bytesRead / totalBytes * 1000);
                            _progressAction(progressPercentage); // 90-100% for changelog
                        });
                    if (changelogData != null && changelogData.Length > 0)
                    {
                        string changelogPath = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "patcher_changelog.md");
                        File.WriteAllBytes(changelogPath, changelogData);
                    }
                }
                catch (Exception ex)
                {
                    _logAction($"[Warning] Could not download patcher changelog: {ex.Message}");
                }
                
                // Final progress at 100%
                _progressAction(10000);
                
                // Calculate stats
                TimeSpan elapsed = DateTime.Now - start;
                _logAction($"Patcher update completed in {elapsed.ToString(@"mm\:ss")}");
                _logAction("Restarting patcher...");
                
                // Wait a moment for logs to be displayed
                await Task.Delay(1500);
                
                // Restart the patcher with the same arguments
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = System.Windows.Forms.Application.ExecutablePath;
                startInfo.Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                Process.Start(startInfo);
                
                // Exit this instance
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logAction($"[ERROR] Exception during self-update: {ex.Message}");
            }
        }
    }
} 