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

            // --- Optimize download order by file type ---
            // Group files by type for improved UI feedback
            var mapFiles = filesToProcess.Where(f =>
                f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

            var dllFiles = filesToProcess.Where(f =>
                f.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

            var otherFiles = filesToProcess.Except(mapFiles).Except(dllFiles).ToList();

            // Log download counts by type
            if (dllFiles.Any())
                _logAction($"Downloading {dllFiles.Count} DLL files...");
            if (otherFiles.Any())
                _logAction($"Downloading {otherFiles.Count} other game files...");
            if (mapFiles.Any())
                _logAction($"Downloading {mapFiles.Count} map files...");

            _logAction($"Found {filesToProcess.Count} files to update.");
            await Task.Delay(500); // Shorter pause to show the message

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
            int currentBytes = 0; // Start at 0, not 1
            int processedFiles = 0;
            int totalFiles = filesToProcess.Count;
            object progressLock = new object(); // Lock for progress updates
            
            // Create a semaphore to limit concurrent downloads
            using var downloadSemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);
            
            // Use faster throttling for progress updates
            _lastProgressUpdateTime = DateTime.MinValue;

            // Helper method to update progress with combined file count and byte metrics
            void UpdateCombinedProgress()
            {
                double fileProgress = (double)processedFiles / totalFiles;
                double byteProgress = (double)currentBytes / totalBytes;
                double combinedProgress = (fileProgress + byteProgress) / 2.0 * 10000;
                UpdateProgressWithThrottling((int)combinedProgress);
            }
            
            // Process files in a specific order: DLLs first, then other files, maps last
            var orderedFiles = new List<FileEntry>();
            orderedFiles.AddRange(dllFiles);
            orderedFiles.AddRange(otherFiles);
            orderedFiles.AddRange(mapFiles);
            var prioritizedFiles = orderedFiles;
            
            // For limiting map file logging
            int loggedMapFiles = 0;
            const int maxLoggedMapFiles = 20; // Limit map file logging
            int lastLoggedMapCount = 0; // Track the last number we logged to prevent duplicates

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

                        // Determine if this is a map file for logging purposes
                        bool isMapFile = entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                        entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                        entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

                        // For map files, limit logging to prevent UI flooding
                        bool shouldLogFile = !isMapFile;
                        if (isMapFile)
                        {
                            int mapFileNumber = Interlocked.Increment(ref loggedMapFiles);
                            shouldLogFile = mapFileNumber <= maxLoggedMapFiles || (mapFileNumber % 50 == 0);
                            
                            // For map files, periodically force UI updates
                            if (mapFileNumber % 10 == 0)
                            {
                                // Force a small delay to keep UI responsive during map processing
                                await Task.Delay(1);
                            }
                        }

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
                            if (_isDebugMode && shouldLogFile) _logAction($"[DEBUG] Attempt {attempt}: Downloading {entry.name} from {url}");

                            var (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                                cancellationToken, url, entry.name, entry.md5, progressCallback);
                            
                            actualHash = hashResult;

                            if (downloadSuccess)
                            {
                                success = true;
                                
                                if (shouldLogFile)
                                {
                                    _logAction($"Completed: {entry.name} ({FormattingUtils.GenerateSize(entry.size)})");
                                }
                                
                                // Periodically provide download status for different file types
                                int currentProcessed = Interlocked.Increment(ref processedFiles);
                                
                                // Provide summary updates at intervals
                                if (currentProcessed % 20 == 0 && !isMapFile)
                                {
                                    _logAction($"Progress: {currentProcessed}/{totalFiles} files ({(int)(currentProcessed * 100.0 / totalFiles)}%)");
                                }
                                
                                break; // Exit retry loop on success
                            }
                            else
                            {
                                // Try backup URL on primary failure
                                string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + entry.name.Replace("\\", "/");
                                
                                if (_isDebugMode && shouldLogFile) _logAction($"[DEBUG] Primary URL failed, trying backup URL: {backupUrl}");
                                
                                (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                                    cancellationToken, backupUrl, entry.name, entry.md5, progressCallback);
                                
                                actualHash = hashResult;
                                
                                if (downloadSuccess)
                                {
                                    success = true;
                                    if (shouldLogFile)
                                    {
                                        _logAction($"Completed: {entry.name} ({FormattingUtils.GenerateSize(entry.size)})");
                                    }
                                    
                                    // Update file counter and provide periodic summaries
                                    int currentProcessed = Interlocked.Increment(ref processedFiles);
                                    
                                    // Provide summary updates at intervals - ONLY log non-map file progress here
                                    if (currentProcessed % 20 == 0 && !isMapFile)
                                    {
                                        _logAction($"Progress: {currentProcessed}/{totalFiles} files ({(int)(currentProcessed * 100.0 / totalFiles)}%)");
                                    }
                                    
                                    break; // Exit retry loop on success
                                }
                                else if (hashResult != null && shouldLogFile)
                                {
                                    _logAction($"[Warning] Hash mismatch for {entry.name}. Expected: {entry.md5.ToUpper()}, Got: {hashResult}");
                                }
                                else if (shouldLogFile)
                                {
                                    _logAction($"[Warning] Failed to download {entry.name}");
                                }

                                if (attempt < maxRetries && shouldLogFile)
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
                                if (shouldLogFile)
                                {
                                    _logAction($"[Error] Failed to download and verify {entry.name} after {maxRetries} attempts.");
                                    if (actualHash != null)
                                    {
                                        _logAction($"[Error] Last hash: {actualHash}, Expected: {entry.md5.ToUpper()}");
                                    }
                                }
                                hasErrors = true;
                            }
                        }
                        
                        // Periodically update UI thread to keep it responsive
                        if (processedFiles % 20 == 0)
                        {
                            await Task.Delay(1); // Small delay to allow UI to process
                        }
                        
                        // Consolidated map file reporting - report map download progress in ONE place only
                        if (success && isMapFile && loggedMapFiles % 50 == 0)
                        {
                            // Use Interlocked to safely compare and update last logged count
                            int mapCount = loggedMapFiles;
                            
                            // Only update if this is a higher count than previously logged
                            if (mapCount > lastLoggedMapCount)
                            {
                                int prevCount = Interlocked.Exchange(ref lastLoggedMapCount, mapCount);
                                // Only log if we successfully updated the lastLoggedMapCount
                                if (prevCount < mapCount)
                                {
                                    _logAction($"Downloaded {mapCount} map files so far");
                                    // Force UI update for large batches
                                    await Task.Delay(1);
                                }
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
                
                // After downloading map files, show a summary
                if (loggedMapFiles > maxLoggedMapFiles)
                {
                    // Use thread-safe approach for the final summary as well
                    int finalMapCount = loggedMapFiles;
                    int prevCount = Interlocked.CompareExchange(ref lastLoggedMapCount, finalMapCount, lastLoggedMapCount);
                    
                    // Only log if this count hasn't been logged before
                    if (prevCount != finalMapCount && finalMapCount > prevCount)
                    {
                        _logAction($"Downloaded a total of {finalMapCount} map files");
                    }
                }
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

            // --- Special Handling for Locked Files ---
            // Handle any files that were locked during parallel download
            if (lockedFiles.Count > 0)
            {
                _logAction($"Processing {lockedFiles.Count} locked files...");
                
                // Process locked files sequentially
                foreach (var lockedFile in lockedFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logAction("Processing of locked files cancelled.");
                        return (patchedBytes, true);
                    }

                    // For DLL files that are locked, use special dll replacement technique
                    if (lockedFile.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        bool success = await HandleLockedDllFileAsync(lockedFile, filelist.downloadprefix);
                        if (success)
                        {
                            patchedBytes += lockedFile.size;
                            _logAction($"Successfully queued replacement for locked DLL: {lockedFile.name}");
                        }
                        else
                        {
                            hasErrors = true;
                            _logAction($"[Error] Failed to handle locked DLL: {lockedFile.name}");
                        }
                    }
                    else
                    {
                        _logAction($"[Warning] Skipping locked file: {lockedFile.name}");
                        hasErrors = true;
                    }
                }
            }

            // Final progress update
            UpdateProgressWithThrottling(10000);

            // Return the total bytes updated and whether there were any errors
            return (patchedBytes, hasErrors);
        }

        // Special method to handle locked DLL files by using Windows File Replace API
        private async Task<bool> HandleLockedDllFileAsync(FileEntry lockedFile, string downloadPrefix)
        {
            try
            {
                var baseDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                var targetPath = Path.Combine(baseDir, lockedFile.name.Replace("/", "\\"));
                
                // Create a temporary file path for the download
                var tempFilePath = Path.Combine(
                    Path.GetDirectoryName(targetPath), 
                    $"{Path.GetFileNameWithoutExtension(targetPath)}_new{Path.GetExtension(targetPath)}");
                
                // Download to temp file
                string url = downloadPrefix + lockedFile.name.Replace("\\", "/");
                
                // Create directory if it doesn't exist
                string directory = Path.GetDirectoryName(tempFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                _logAction($"Downloading replacement for locked DLL: {lockedFile.name}");
                
                // Download to temporary file
                var (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                    new CancellationTokenSource(), 
                    url, 
                    tempFilePath, 
                    lockedFile.md5,
                    null); // No progress callback for these special cases

                if (!downloadSuccess)
                {
                    // Try backup URL
                    string backupUrl = "https://patch.heroesjourneyemu.com/rof/" + lockedFile.name.Replace("\\", "/");
                    (downloadSuccess, hashResult) = await UtilityLibrary.DownloadFileAndVerifyHashAsync(
                        new CancellationTokenSource(), 
                        backupUrl, 
                        tempFilePath, 
                        lockedFile.md5,
                        null);
                }

                if (downloadSuccess)
                {
                    // Queue the file for replacement on next application start
                    _logAction($"Downloaded replacement for locked DLL: {lockedFile.name}");
                    
                    bool queued = UtilityLibrary.QueueFileReplacement(tempFilePath, targetPath);
                    if (queued)
                    {
                        _logAction($"Successfully queued {lockedFile.name} for replacement on next start");
                        return true;
                    }
                    else
                    {
                        _logAction($"[Error] Failed to queue {lockedFile.name} for replacement");
                        return false;
                    }
                }
                else
                {
                    _logAction($"[Error] Failed to download replacement for locked DLL: {lockedFile.name}");
                    if (hashResult != null)
                    {
                        _logAction($"[Error] Hash mismatch. Expected: {lockedFile.md5.ToUpper()}, Got: {hashResult}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Error] Exception handling locked DLL {lockedFile.name}: {ex.Message}");
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