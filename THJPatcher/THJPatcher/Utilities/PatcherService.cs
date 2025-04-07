using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using THJPatcher.Models;
using System.Net.Http;
using System.Diagnostics;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service responsible for downloading and patching game files
    /// </summary>
    public class PatcherService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        private readonly Action<int> _progressAction;
        private readonly bool _isAdministrator;

        // Constants
        private const int MAX_CONCURRENT_DOWNLOADS = 20; // Increased from 8 to 20 for higher throughput
        private DateTime _lastProgressUpdateTime = DateTime.MinValue;
        private int _lastProgressValue = -1;
        private const int MIN_PROGRESS_UPDATE_INTERVAL_MS = 50; // Minimum time between progress updates

        /// <summary>
        /// Initializes a new instance of the PatcherService
        /// </summary>
        /// <param name="isDebugMode">Whether debug mode is enabled</param>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="progressAction">Action to update progress</param>
        /// <param name="isAdministrator">Whether the application is running with admin privileges</param>
        public PatcherService(
            bool isDebugMode, 
            Action<string> logAction, 
            Action<int> progressAction,
            bool isAdministrator)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _progressAction = progressAction ?? (progress => { /* No progress updates if null */ });
            _isAdministrator = isAdministrator;
        }

        /// <summary>
        /// Checks if a file is currently locked for writing.
        /// </summary>
        /// <param name="filePath">The full path to the file.</param>
        /// <returns>True if the file is locked, false otherwise.</returns>
        private bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false; // File doesn't exist, so it's not locked
            }
            try
            {
                using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // If we get here, the file isn't locked
                    fs.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true; // IOException indicates the file is likely locked
            }
            catch (Exception ex)
            {
                _logAction($"[Warning] Error checking if {Path.GetFileName(filePath)} is locked: {ex.Message}");
                return false; // Assume not locked if an unexpected error occurs
            }
        }

        /// <summary>
        /// Downloads and patches files from the file list, using streaming hash verification
        /// </summary>
        /// <param name="filesToProcess">List of files to download and patch</param>
        /// <param name="filelist">The file list containing download information</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <returns>Tuple containing the total patched bytes and whether there were any errors</returns>
        public async Task<(double patchedBytes, bool hasErrors)> DownloadAndPatchFilesAsync(
            List<FileEntry> filesToProcess, 
            FileList filelist,
            CancellationTokenSource cancellationToken)
        {
            double patchedBytes = 0;
            bool hasErrors = false;
            
            // Maximum number of retries for downloads
            const int maxRetries = 3;

            // If no files need downloading, we're done
            if (filesToProcess == null || filesToProcess.Count == 0)
            {
                _logAction("All files are up to date.");
                UpdateProgressWithThrottling(10000);
                return (0, false);
            }

            _logAction($"Found {filesToProcess.Count} files to update.");
            await Task.Delay(1000); // Pause to show the message

            // Ensure download prefix ends with a slash
            if (!filelist.downloadprefix.EndsWith("/")) filelist.downloadprefix += "/";
            
            // --- Pre-check for Locked Files ---
            var lockedFiles = new ConcurrentBag<FileEntry>();
            var unlockedFiles = new ConcurrentBag<FileEntry>();
            var baseDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            int totalBytes = filesToProcess.Sum(f => f.size);
            
            _logAction("Checking for locked files...");
            await Task.Run(() =>
            {
                Parallel.ForEach(filesToProcess, entry =>
                {
                    var path = Path.Combine(baseDir, entry.name.Replace("/", "\\"));
                    if (IsFileLocked(path))
                    {
                        _logAction($"[Info] File is currently in use: {entry.name}");
                        lockedFiles.Add(entry);
                    }
                    else
                    {
                        unlockedFiles.Add(entry);
                    }
                });
            });
            _logAction($"Check complete. Found {lockedFiles.Count} locked file(s).");

            // Calculate total download size for progress reporting
            if (totalBytes == 0) totalBytes = 1; // Avoid division by zero

            // Use class-level variables for tracking download progress
            int currentBytes = 0;
            object progressLock = new object(); // Lock for progress updates
            
            // Create a semaphore to limit concurrent downloads
            using var downloadSemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);
            
            // Use faster throttling for progress updates
            _lastProgressUpdateTime = DateTime.MinValue;

            // Helper method to update progress
            void UpdateCombinedProgress()
            {
                int progressPercentage = (int)((double)currentBytes / totalBytes * 10000);
                UpdateProgressWithThrottling(Math.Min(progressPercentage, 10000));
            }
            
            // --- Optimize download order ---
            // Prioritize small, important files first for better user experience
            var prioritizedFiles = unlockedFiles.ToList();
            
            // Step 1: Sort by priority category then by size within each category
            var priorityGroups = new List<List<FileEntry>>();
            
            // Group 1: Critical system files (dinput8.dll, etc) - highest priority
            var criticalFiles = prioritizedFiles
                .Where(f => f.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase) ||
                           f.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && f.size < 1024 * 1024)
                .OrderBy(f => f.size)
                .ToList();
            
            // Group 2: UI files (medium size) - second priority
            var uiFiles = prioritizedFiles
                .Where(f => (f.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase) ||
                           f.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase)) &&
                           !criticalFiles.Contains(f))
                .OrderBy(f => f.size)
                .ToList();
            
            // Group 3: Small regular files (< 10MB) - third priority
            var smallFiles = prioritizedFiles
                .Where(f => f.size < 10 * 1024 * 1024 && 
                           !criticalFiles.Contains(f) && 
                           !uiFiles.Contains(f))
                .OrderBy(f => f.size)
                .ToList();
            
            // Group 4: Medium files (10-100MB) - fourth priority
            var mediumFiles = prioritizedFiles
                .Where(f => f.size >= 10 * 1024 * 1024 && 
                           f.size < 100 * 1024 * 1024 && 
                           !criticalFiles.Contains(f) && 
                           !uiFiles.Contains(f) && 
                           !smallFiles.Contains(f))
                .OrderBy(f => f.size)
                .ToList();
            
            // Group 5: Large files (>= 100MB) - lowest priority
            var largeFiles = prioritizedFiles
                .Where(f => f.size >= 100 * 1024 * 1024 && 
                           !criticalFiles.Contains(f) && 
                           !uiFiles.Contains(f) && 
                           !smallFiles.Contains(f) && 
                           !mediumFiles.Contains(f))
                .OrderBy(f => f.size)
                .ToList();
            
            // Create a new ordered list based on priority
            prioritizedFiles.Clear();
            prioritizedFiles.AddRange(criticalFiles);
            prioritizedFiles.AddRange(uiFiles);
            prioritizedFiles.AddRange(smallFiles);
            prioritizedFiles.AddRange(mediumFiles);
            prioritizedFiles.AddRange(largeFiles);
            
            if (_isDebugMode)
            {
                _logAction($"[DEBUG] Download order optimized: {criticalFiles.Count} critical, {uiFiles.Count} UI, " +
                          $"{smallFiles.Count} small, {mediumFiles.Count} medium, {largeFiles.Count} large files");
            }

            // --- Parallel Download Phase (Unlocked Files) ---
            _logAction($"Starting download of {prioritizedFiles.Count} file(s)...");
            var downloadTasks = new List<Task>();
            
            foreach (var entry in prioritizedFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logAction("Patching cancelled.");
                    return (patchedBytes, true);
                }
                
                await downloadSemaphore.WaitAsync();
                
                downloadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        bool success = false;
                        string actualHash = null;
                        var path = Path.Combine(baseDir, entry.name.Replace("/", "\\"));

                        // Create directory if it doesn't exist
                        string directory = Path.GetDirectoryName(path);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        // Set up progress tracking for this file
                        double fileBytesDownloaded = 0;
                        Action<long, long> progressCallback = (bytesRead, totalFileSize) =>
                        {
                            // Calculate the delta since last update
                            double bytesDelta = bytesRead - fileBytesDownloaded;
                            fileBytesDownloaded = bytesRead;

                            // Update shared progress counter
                            lock (progressLock)
                            {
                                currentBytes += (int)bytesDelta;
                                UpdateCombinedProgress();
                            }
                        };

                        // Try primary URL with retries
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                            if (_isDebugMode) _logAction($"[DEBUG] Attempt {attempt}: Downloading {entry.name} from {url}");

                            var (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                                cancellationToken, url, entry.name, entry.md5, progressCallback);
                            
                            actualHash = hashResult;

                            if (downloadSuccess)
                            {
                                success = true;
                                _logAction($"Completed: {entry.name} ({FormattingUtils.GenerateSize(entry.size)})");
                                break; // Exit retry loop on success
                            }
                            else
                            {
                                // Try backup URL on primary failure
                                string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                                
                                if (_isDebugMode) _logAction($"[DEBUG] Primary URL failed, trying backup URL: {backupUrl}");
                                
                                (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                                    cancellationToken, backupUrl, entry.name, entry.md5, progressCallback);
                                
                                actualHash = hashResult;
                                
                                if (downloadSuccess)
                                {
                                    success = true;
                                    _logAction($"Completed: {entry.name} ({FormattingUtils.GenerateSize(entry.size)})");
                                    break; // Exit retry loop on success
                                }
                                else if (hashResult != null)
                                {
                                    _logAction($"[Warning] Hash mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {hashResult}");
                                }
                                else
                                {
                                    _logAction($"[Warning] Failed to download {entry.name}");
                                }

                                if (attempt < maxRetries)
                                {
                                    _logAction($"Retrying download of {entry.name} (attempt {attempt+1}/{maxRetries})...");
                                    await Task.Delay(1000 * attempt); // Exponential backoff
                                }
                            }
                        }

                        // Update counters based on download result
                        lock (progressLock)
                        {
                            if (success)
                            {
                                patchedBytes += entry.size;
                            }
                            else
                            {
                                _logAction($"[Error] Failed to download and verify {entry.name} after {maxRetries} attempts.");
                                if (actualHash != null)
                                {
                                    _logAction($"[Error] Last hash: {actualHash}, Expected: {entry.md5.ToUpper()}");
                                }
                                hasErrors = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[Error] Unexpected error downloading {entry.name}: {ex.Message}");
                        lock (progressLock)
                        {
                            hasErrors = true;
                        }
                    }
                    finally
                    {
                        downloadSemaphore.Release();
                    }
                }, cancellationToken.Token));
            }

            try
            {
                // Wait for all parallel downloads to complete
                await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                _logAction("Download operations cancelled.");
                return (patchedBytes, true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _logAction("Patching cancelled before handling locked files.");
                return (patchedBytes, true);
            }

            // --- Sequential Download Phase (Locked Files) ---
            if (lockedFiles.Count > 0)
            {
                _logAction($"Processing {lockedFiles.Count} locked file(s)...");
                
                foreach (var entry in lockedFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logAction("Patching cancelled during locked file handling.");
                        return (patchedBytes, true);
                    }

                    var path = Path.Combine(baseDir, entry.name.Replace("/", "\\"));
                    bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);

                    if (await HandleSpecialDllDownload(entry, filelist, path, isDinput8, true, cancellationToken))
                    {
                        lock (progressLock)
                        {
                            currentBytes += entry.size;
                            patchedBytes += entry.size;
                            UpdateCombinedProgress();
                        }
                    }
                    else
                    {
                        _logAction($"[Warning] Failed to handle locked file: {entry.name}");
                        hasErrors = true;
                    }
                }
            }

            // Final progress update
            UpdateProgressWithThrottling(10000);

            return (patchedBytes, hasErrors);
        }

        /// <summary>
        /// Handles download and scheduling of locked DLL files
        /// </summary>
        private async Task<bool> HandleSpecialDllDownload(
            FileEntry entry, 
            FileList filelist, 
            string path, 
            bool isDinput8, 
            bool isFileInUse,
            CancellationTokenSource cancellationToken)
        {
            try
            {
                _logAction($"[Info] Using special handling for {entry.name}");

                // Create a temp backup path with unique name to avoid conflicts
                string tempFileName = $"{Path.GetFileNameWithoutExtension(path)}_{Guid.NewGuid().ToString().Substring(0, 8)}.tmp";
                string tempPath = Path.Combine(Path.GetDirectoryName(path), tempFileName);
                string directory = Path.GetDirectoryName(tempPath);
                
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use our new streaming hash verification
                bool downloadSuccess = false;
                string actualHash = null;
                int maxRetries = 3;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Try primary URL first
                    string url = filelist.downloadprefix + entry.name.Replace("\\", "/");
                    if (_isDebugMode) _logAction($"[DEBUG] Attempt {attempt}: Downloading {entry.name} to temp file using {url}");

                    var (success, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                        cancellationToken, url, tempPath, entry.md5, null); // No progress callback for now
                    
                    actualHash = hashResult;
                    
                    if (success)
                    {
                        downloadSuccess = true;
                        break;
                    }
                    else
                    {
                        // Try backup URL on primary failure
                        string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                        
                        if (_isDebugMode) _logAction($"[DEBUG] Primary URL failed, trying backup URL: {backupUrl}");
                        
                        (success, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                            cancellationToken, tempPath, entry.name, entry.md5, null); // No progress callback
                        
                        actualHash = hashResult;
                        
                        if (success)
                        {
                            downloadSuccess = true;
                            break;
                        }
                        else if (attempt < maxRetries)
                        {
                            if (hashResult != null)
                            {
                                _logAction($"[Warning] Hash mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {hashResult}");
                            }
                            else
                            {
                                _logAction($"[Warning] Failed to download {entry.name} to temp file");
                            }
                            _logAction($"Retrying download of {entry.name} (attempt {attempt+1}/{maxRetries})...");
                            await Task.Delay(1000 * attempt); // Exponential backoff
                        }
                    }
                }

                if (!downloadSuccess)
                {
                    _logAction($"[Error] Failed to download {entry.name} to temp file after {maxRetries} attempts");
                    return false;
                }

                // Verify the temp file exists
                if (!File.Exists(tempPath))
                {
                    _logAction($"[Error] Temp file for {entry.name} not created properly");
                    return false;
                }

                // If the file is not in use (which shouldn't happen, but just in case), try direct replacement
                if (!isFileInUse)
                {
                    try
                    {
                        _logAction($"[Info] Attempting direct replacement of {entry.name}");
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        File.Move(tempPath, path);
                        _logAction($"Successfully updated {entry.name}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[Warning] Direct replacement failed: {ex.Message}");
                        // Fall through to the other methods
                        isFileInUse = true; // Treat as in-use for the next steps
                    }
                }

                // Schedule the file to be replaced on next reboot if it's in use
                if (isFileInUse && _isAdministrator)
                {
                    try
                    {
                        _logAction($"[Info] Scheduling {entry.name} replacement on reboot");
                        // Try Windows' MoveFileEx API to replace on reboot
                        if (UtilityLibrary.ScheduleFileOperation(tempPath, path, UtilityLibrary.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT | UtilityLibrary.MoveFileFlags.MOVEFILE_REPLACE_EXISTING))
                        {
                            _logAction($"{entry.name} will be updated on next reboot");
                            return true;
                        }
                        else
                        {
                            _logAction($"[Warning] Failed to schedule {entry.name} replacement");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[Warning] Could not schedule {entry.name} replacement: {ex.Message}");
                    }
                }

                // If MoveFileEx fails or we're not admin, try a more aggressive approach
                try
                {
                    _logAction($"[Info] Attempting aggressive replacement of {entry.name}");
                    File.Delete(path);
                    File.Move(tempPath, path);
                    _logAction($"Successfully updated {entry.name}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logAction($"[Warning] Could not force update {entry.name}: {ex.Message}");
                    if (File.Exists(tempPath))
                    {
                        // Keep the temp file for next run
                        _logAction($"Keeping temporary file for {entry.name} for next run");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logAction($"[Warning] Special handling for {entry.name} failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates progress with throttling to prevent flickering
        /// </summary>
        /// <param name="progressValue">The progress value to display (0-10000)</param>
        private void UpdateProgressWithThrottling(int progressValue)
        {
            // Clamp to valid range
            progressValue = Math.Max(0, Math.Min(10000, progressValue));
            
            // Calculate time since last update and progress change amount
            int progressDelta = Math.Abs(progressValue - _lastProgressValue);
            double timeSinceLastUpdate = (DateTime.Now - _lastProgressUpdateTime).TotalMilliseconds;
            
            // Update more frequently during downloads
            // First update (no previous value)
            // Significant change (>0.1%)
            // Regular updates at least every 100ms to keep UI responsive
            bool shouldUpdate = 
                _lastProgressValue == -1 ||                // First update
                progressDelta > 10 ||                      // Significant change (>0.1%)
                timeSinceLastUpdate > 100;                 // Regular updates every 100ms
            
            if (shouldUpdate)
            {
                // Update progress and record time and value
                _progressAction(progressValue);
                _lastProgressUpdateTime = DateTime.Now;
                _lastProgressValue = progressValue;
            }
        }

        /// <summary>
        /// Deletes files listed in the filelist.deletes collection
        /// </summary>
        /// <param name="filelist">The file list containing files to delete</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <param name="startProgress">Starting progress value (0-10000)</param>
        /// <param name="progressWeight">Weight of this operation in overall progress (0-10000)</param>
        /// <returns>True if there were any errors, false otherwise</returns>
        public async Task<bool> DeleteFilesAsync(
            FileList filelist, 
            CancellationTokenSource cancellationToken,
            int startProgress = 9000,
            int progressWeight = 1000)
        {
            bool hasErrors = false;
            
            if (filelist.deletes != null && filelist.deletes.Count > 0)
            {
                _logAction($"Processing {filelist.deletes.Count} file deletion(s)...");
                int totalFiles = filelist.deletes.Count;
                int processedFiles = 0;
                
                foreach (var entry in filelist.deletes)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logAction("Patching cancelled.");
                        return true; // Return with error since operation was cancelled
                    }
                    
                    processedFiles++;
                    // Calculate progress within the allocated range
                    int currentProgress = startProgress + (int)((double)processedFiles / totalFiles * progressWeight);
                    UpdateProgressWithThrottling(Math.Min(currentProgress, 10000));

                    var path = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        _logAction($"[Warning] Path {entry.name} might be outside your EverQuest directory. Skipping deletion.");
                        continue;
                    }

                    if (await Task.Run(() => File.Exists(path)))
                    {
                        try
                        {
                            await Task.Run(() => File.Delete(path));
                            _logAction($"Deleted {entry.name}");
                        }
                        catch (Exception ex)
                        {
                            _logAction($"[Warning] Failed to delete {entry.name}: {ex.Message}");
                            hasErrors = true;
                        }
                    }
                }
            }
            
            // Ensure we reach the end of our progress allocation
            UpdateProgressWithThrottling(startProgress + progressWeight);
            
            return hasErrors;
        }
    }
} 