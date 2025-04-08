#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using THJPatcher.Models;
using System.Security.Cryptography;
using System.Collections;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service responsible for file system operations and integrity checking
    /// </summary>
    public class FileSystemService
    {
        private readonly bool _isDebugMode;
        private readonly Action<string> _logAction;
        private readonly Action<int> _progressAction;
        private readonly Func<bool> _isAdministrator;
        private readonly string _filelistUrl;
        private readonly List<FileEntry> _filesToDownload;
        
        // Increased from 8 to 16 for better parallelism
        private const int MAX_CONCURRENT_FILE_CHECKS = 16;
        
        // File size threshold for optimization strategies
        private const long LARGE_FILE_THRESHOLD = 100 * 1024 * 1024; // 100MB
        
        // Cache for file hashes to avoid recomputing when unchanged
        private readonly ConcurrentDictionary<string, (string Hash, DateTime LastModified)> _fileHashCache = 
            new ConcurrentDictionary<string, (string Hash, DateTime LastModified)>();

        /// <summary>
        /// Initializes a new instance of the FileSystemService
        /// </summary>
        /// <param name="isDebugMode">Whether debug mode is enabled</param>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="progressAction">Action to update progress</param>
        /// <param name="isAdministrator">Function to check administrator privileges</param>
        /// <param name="filelistUrl">Base URL for filelist downloads</param>
        /// <param name="filesToDownload">Reference to the main file download list</param>
        public FileSystemService(
            bool isDebugMode,
            Action<string> logAction,
            Action<int> progressAction,
            Func<bool> isAdministrator,
            string filelistUrl,
            List<FileEntry> filesToDownload)
        {
            _isDebugMode = isDebugMode;
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _progressAction = progressAction ?? (progress => { /* No progress updates if null */ });
            _isAdministrator = isAdministrator ?? (() => false);
            _filelistUrl = filelistUrl;
            _filesToDownload = filesToDownload;
        }

        /// <summary>
        /// Downloads the file list from the primary or fallback URL and parses it.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The parsed FileList object, or null if download/parsing fails.</returns>
        public async Task<FileList?> DownloadAndParseFileListAsync(CancellationTokenSource cancellationToken)
        {
            string suffix = "rof"; // Or determine dynamically if needed
            string primaryUrl = _filelistUrl;
            string fallbackUrl = "https://patch.heroesjourneyemu.com"; // Consider making this configurable
            string webUrl = $"{primaryUrl}/filelist_{suffix}.yml";
            string localFilename = "filelist.yml";
            string filelistPath = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), localFilename);

            // Create a progress callback for filelist download
            Action<long, long> progressCallback = (bytesRead, totalBytes) => 
            {
                // Calculate progress as 0-5% of total (just for filelist download)
                int progressPercentage = (int)((double)bytesRead / totalBytes * 500);
                _progressAction(Math.Min(progressPercentage, 500)); // 0-5% range
            };

            string filelistDownloadError = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, localFilename, progressCallback);

            if (!string.IsNullOrEmpty(filelistDownloadError))
            {
                _logAction($"[Warning] Failed to download filelist from primary URL ({webUrl}): {filelistDownloadError}. Trying fallback...");
                webUrl = $"{fallbackUrl}/filelist_{suffix}.yml";
                filelistDownloadError = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, localFilename, progressCallback);

                if (!string.IsNullOrEmpty(filelistDownloadError))
                {
                    _logAction($"[Error] Failed to download filelist from fallback URL ({webUrl}): {filelistDownloadError}.");
                    return null;
                }
            }

            if (!File.Exists(filelistPath))
            {
                 _logAction($"[Error] Downloaded filelist not found at expected path: {filelistPath}");
                 return null;
            }

            try
            {
                using (var input = File.OpenText(filelistPath))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    var filelist = deserializer.Deserialize<FileList>(input);
                    _logAction("Successfully downloaded and parsed file list.");
                    return filelist;
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Error] Failed to parse filelist: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Performs a file integrity scan to check for missing or modified files
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
        /// <param name="fileList">The already downloaded and parsed file list (optional)</param>
        /// <param name="fullScanOnly">Whether to perform only a full scan</param>
        /// <returns>True if all files are intact, false otherwise</returns>
        public async Task<bool> RunFileIntegrityScanAsync(
            CancellationTokenSource cancellationToken, 
            FileList? fileList = null,
            bool fullScanOnly = false)
        {
            _logAction("Starting file integrity scan...");
            await Task.Delay(500);

            FileList filelist;
            
            // Use provided filelist if available, otherwise download it
            if (fileList != null)
            {
                filelist = fileList;
                _logAction("Using provided file list for integrity scan.");
            }
            else
            {
                // Download and parse the filelist (existing code)
                string suffix = "rof";
                string primaryUrl = _filelistUrl;
                string fallbackUrl = "https://patch.heroesjourneyemu.com";
                string webUrl = $"{primaryUrl}/filelist_{suffix}.yml";
                string filelistResponse = "";

                // Try primary URL first
                _logAction("Downloading file list...");
                filelistResponse = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, "filelist.yml", 
                    (bytesRead, totalBytes) => 
                    {
                        // File scan filelist download is 0-2% of progress
                        int progressPercentage = (int)((double)bytesRead / totalBytes * 200);
                        _progressAction(Math.Min(progressPercentage, 200));
                    });

                // If primary URL fails, try fallback
                if (filelistResponse != "")
                {
                    if (_isDebugMode)
                    {
                        _logAction($"[DEBUG] Primary URL failed, trying fallback URL");
                    }
                    webUrl = $"{fallbackUrl}/filelist_{suffix}.yml";
                    filelistResponse = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, "filelist.yml",
                        (bytesRead, totalBytes) => 
                        {
                            // File scan filelist download is 0-2% of progress
                            int progressPercentage = (int)((double)bytesRead / totalBytes * 200);
                            _progressAction(Math.Min(progressPercentage, 200));
                        });

                    // If fallback also fails, report error
                    if (filelistResponse != "")
                    {
                        _logAction($"Failed to fetch filelist from both primary and fallback URLs:");
                        _logAction($"Primary: {primaryUrl}/filelist_{suffix}.yml");
                        _logAction($"Fallback: {fallbackUrl}/filelist_{suffix}.yml");
                        _logAction($"Error: {filelistResponse}");
                        return false;
                    }
                }

                // Read the filelist
                string filelistPath = $"{Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName)}\\filelist.yml";

                using (var input = File.OpenText(filelistPath))
                {
                    var deserializerBuilder = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    filelist = deserializerBuilder.Deserialize<FileList>(input);
                }
            }

            // Clear any existing files to download
            _filesToDownload.Clear();

            // Calculate total files for progress
            int totalFiles = filelist.downloads.Count;
            string baseDirectory = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            
            // Thread-safe collections for tracking results
            var missingOrModifiedFiles = new ConcurrentBag<FileEntry>();
            int checkedFiles = 0;
            
            // First do a quick check in parallel (file existence and size only)
            _logAction("Starting Quick File Scan...");
            
            // Use Interlocked directly for thread-safe progress updates instead of a separate semaphore
            
            // Create a SemaphoreSlim to limit concurrent operations
            using var throttler = new SemaphoreSlim(MAX_CONCURRENT_FILE_CHECKS);
            var scanTasks = new List<Task>();
            
            // Flag to track quick check success
            bool quickCheckPassed = true;
            int quickCheckPassedInt = 1; // 1 = passed, 0 = failed
            
            // Group files by type for potential optimizations
            var dinput8File = filelist.downloads.FirstOrDefault(f => 
                f.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase));
            
            var uiFiles = filelist.downloads.Where(f => 
                f.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) || 
                f.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase)).ToList();
            
            var mapFiles = filelist.downloads.Where(f => 
                f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) || 
                f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) || 
                f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
            
            var otherFiles = filelist.downloads.Where(f => 
                !f.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase) && 
                !f.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) && 
                !f.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase) &&
                !f.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase) &&
                !f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) && 
                !f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) &&
                !f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
            
            // Process dinput8.dll first as a high priority
            if (dinput8File != null)
            {
                await ProcessDinput8FileAsync(dinput8File, baseDirectory, cancellationToken, missingOrModifiedFiles);
                
                // Update progress for dinput8.dll (special case)
                Interlocked.Increment(ref checkedFiles);
                int progress = 200 + (int)((double)checkedFiles / totalFiles * 4800);
                _progressAction(progress);
                
                // If dinput8.dll failed the check, update flag
                if (missingOrModifiedFiles.Any(f => f.name == dinput8File.name))
                {
                    quickCheckPassed = false;
                }
            }
            
            // Process map files next (high priority for specific game functionality)
            foreach (var entry in mapFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logAction("File scan cancelled.");
                    return false;
                }
                
                await throttler.WaitAsync(cancellationToken.Token);
                
                scanTasks.Add(Task.Run(async () => 
                {
                    try
                    {
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Processing map file: {entry.name}");
                        }
                        
                        var path = Path.Combine(baseDirectory, entry.name.Replace("/", "\\"));
                        
                        if (!UtilityLibrary.IsPathChild(path))
                        {
                            _logAction($"[Warning] Path {entry.name} might be outside of your EverQuest directory.");
                            return;
                        }
                        
                        bool isFileModified = false;
                        
                        // Check if file exists
                        if (!File.Exists(path))
                        {
                            if (_isDebugMode)
                            {
                                _logAction($"[DEBUG] Missing map file detected: {entry.name}");
                            }
                            
                            _logAction($"Missing map file detected: {entry.name}");
                            missingOrModifiedFiles.Add(entry);
                            
                            // Add to download queue for map files
                            lock (_filesToDownload)
                            {
                                if (!_filesToDownload.Any(f => f.name == entry.name))
                                {
                                    _filesToDownload.Add(entry);
                                    if (_isDebugMode)
                                    {
                                        _logAction($"[DEBUG] Added missing map file to download queue: {entry.name}");
                                    }
                                }
                            }
                            
                            isFileModified = true;
                        }
                        else
                        {
                            // For map files, check size first
                            var fileInfo = new FileInfo(path);
                            if (fileInfo.Length != entry.size)
                            {
                                if (_isDebugMode)
                                {
                                    _logAction($"[DEBUG] Map file size mismatch detected: {entry.name}");
                                }
                                
                                _logAction($"Size mismatch detected: {entry.name}");
                                missingOrModifiedFiles.Add(entry);
                                
                                // Add map files with mismatched size to download queue
                                lock (_filesToDownload)
                                {
                                    if (!_filesToDownload.Any(f => f.name == entry.name))
                                    {
                                        _filesToDownload.Add(entry);
                                        if (_isDebugMode)
                                        {
                                            _logAction($"[DEBUG] Added mismatched map file to download queue: {entry.name}");
                                        }
                                    }
                                }
                                
                                isFileModified = true;
                            }
                        }
                        
                        if (isFileModified)
                        {
                            Interlocked.Exchange(ref quickCheckPassedInt, 0); // Thread-safe set to false
                        }
                        
                        // Update progress after each file
                        int filesDone = Interlocked.Increment(ref checkedFiles);
                        int progress = 200 + (int)((double)filesDone / totalFiles * 4800);
                        _progressAction(progress);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }, cancellationToken.Token));
            }
            
            // Wait for map file scanning to complete
            if (scanTasks.Count > 0)
            {
                await Task.WhenAll(scanTasks);
                scanTasks.Clear();
            }
            
            // Process UI files next (high priority for quick detection)
            foreach (var entry in uiFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logAction("File scan cancelled.");
                    return false;
                }
                
                await throttler.WaitAsync(cancellationToken.Token);
                
                scanTasks.Add(Task.Run(async () => 
                {
                    try
                    {
                        // Skip the patcher executable itself
                        if (entry.name.EndsWith("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        
                        var path = Path.Combine(baseDirectory, entry.name.Replace("/", "\\"));
                        
                        if (!UtilityLibrary.IsPathChild(path))
                        {
                            _logAction($"[Warning] Path {entry.name} might be outside of your EverQuest directory.");
                            return;
                        }
                        
                        bool isFileModified = false;
                        
                        // Check if file exists
                        if (!File.Exists(path))
                        {
                            if (_isDebugMode)
                            {
                                _logAction($"[DEBUG] Missing UI file detected: {entry.name}");
                            }
                            
                            missingOrModifiedFiles.Add(entry);
                            
                            // Add to download queue for UI files
                            lock (_filesToDownload)
                            {
                                if (!_filesToDownload.Any(f => f.name == entry.name))
                                {
                                    _filesToDownload.Add(entry);
                                    if (_isDebugMode)
                                    {
                                        _logAction($"[DEBUG] Added missing UI file to download queue: {entry.name}");
                                    }
                                }
                            }
                            
                            isFileModified = true;
                        }
                        else
                        {
                            // For UI files, check size first
                            var fileInfo = new FileInfo(path);
                            if (fileInfo.Length != entry.size)
                            {
                                if (_isDebugMode)
                                {
                                    _logAction($"[DEBUG] UI file size mismatch detected: {entry.name}");
                                }
                                
                                _logAction($"Size mismatch detected: {entry.name}");
                                missingOrModifiedFiles.Add(entry);
                                
                                // Add UI files with mismatched size to download queue
                                lock (_filesToDownload)
                                {
                                    if (!_filesToDownload.Any(f => f.name == entry.name))
                                    {
                                        _filesToDownload.Add(entry);
                                        if (_isDebugMode)
                                        {
                                            _logAction($"[DEBUG] Added mismatched UI file to download queue: {entry.name}");
                                        }
                                    }
                                }
                                
                                isFileModified = true;
                            }
                        }
                        
                        if (isFileModified)
                        {
                            Interlocked.Exchange(ref quickCheckPassedInt, 0); // Thread-safe set to false
                        }
                        
                        // Update progress after each file
                        int filesDone = Interlocked.Increment(ref checkedFiles);
                        int progress = 200 + (int)((double)filesDone / totalFiles * 4800);
                        _progressAction(progress);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }, cancellationToken.Token));
            }
            
            // Wait for UI file scanning to complete
            if (scanTasks.Count > 0)
            {
                await Task.WhenAll(scanTasks);
                scanTasks.Clear();
            }
            
            // If we've already found UI files that need updating, we can proceed to patching
            if (_filesToDownload.Count > 0)
            {
                _logAction($"Found {_filesToDownload.Count} UI file(s) that need to be patched.");
                
                // Print out map files detected (for debugging)
                if (_isDebugMode)
                {
                    var mapFilesToDownload = _filesToDownload.Where(f =>
                        f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                        f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                        f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (mapFilesToDownload.Any())
                    {
                        _logAction($"[DEBUG] Found {mapFilesToDownload.Count} map files that need downloading:");
                        foreach (var map in mapFilesToDownload.Take(10))
                        {
                            _logAction($"[DEBUG] Map: {map.name}");
                        }
                        if (mapFilesToDownload.Count > 10)
                        {
                            _logAction($"[DEBUG] And {mapFilesToDownload.Count - 10} more map files...");
                        }
                    }
                }
                
                // Update integrity check status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "failed";
                await Task.Run(() => IniLibrary.Save());
                
                return false;
            }
            
            // Process remaining files in parallel
            // Use custom partitioning to balance work more effectively
            var otherFilesPartitions = Partitioner.Create(otherFiles, true).GetPartitions(MAX_CONCURRENT_FILE_CHECKS);
            foreach (var partition in otherFilesPartitions)
            {
                scanTasks.Add(Task.Run(async () => 
                {
                    using (partition)
                    {
                        while (partition.MoveNext())
                        {
                            var entry = partition.Current;
                            
                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logAction("File scan cancelled.");
                                break;
                            }
                            
                            try
                            {
                                // Skip the patcher executable itself
                                if (entry.name.EndsWith("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                
                                var path = Path.Combine(baseDirectory, entry.name.Replace("/", "\\"));
                                
                                if (!UtilityLibrary.IsPathChild(path))
                                {
                                    _logAction($"[Warning] Path {entry.name} might be outside of your EverQuest directory.");
                                    continue;
                                }
                                
                                bool isFileModified = false;
                                
                                // Check if file exists
                                if (!File.Exists(path))
                                {
                                    if (_isDebugMode)
                                    {
                                        _logAction($"[DEBUG] Missing file detected: {entry.name}");
                                    }
                                    else
                                    {
                                        _logAction($"Missing file detected: {entry.name}");
                                    }
                                    
                                    missingOrModifiedFiles.Add(entry);
                                    
                                    // Add to download queue
                                    lock (_filesToDownload)
                                    {
                                        if (!_filesToDownload.Any(f => f.name == entry.name))
                                        {
                                            _filesToDownload.Add(entry);
                                            if (_isDebugMode)
                                            {
                                                _logAction($"[DEBUG] Added missing file to download queue: {entry.name}");
                                            }
                                        }
                                    }
                                    
                                    isFileModified = true;
                                }
                                else
                                {
                                    // For other files, check size
                                    var fileInfo = new FileInfo(path);
                                    if (fileInfo.Length != entry.size)
                                    {
                                        if (_isDebugMode)
                                        {
                                            _logAction($"[DEBUG] File size mismatch detected: {entry.name}");
                                        }
                                        
                                        _logAction($"Size mismatch detected: {entry.name}");
                                        missingOrModifiedFiles.Add(entry);
                                        
                                        // Add to download queue
                                        lock (_filesToDownload)
                                        {
                                            if (!_filesToDownload.Any(f => f.name == entry.name))
                                            {
                                                _filesToDownload.Add(entry);
                                                if (_isDebugMode)
                                                {
                                                    _logAction($"[DEBUG] Added mismatched file to download queue: {entry.name}");
                                                }
                                            }
                                        }
                                        
                                        isFileModified = true;
                                    }
                                }
                                
                                if (isFileModified)
                                {
                                    Interlocked.Exchange(ref quickCheckPassedInt, 0); // Thread-safe set to false
                                }
                                
                                // Update progress after each file
                                int filesDone = Interlocked.Increment(ref checkedFiles);
                                int progress = 200 + (int)((double)filesDone / totalFiles * 4800);
                                _progressAction(progress);
                            }
                            catch (Exception ex)
                            {
                                if (_isDebugMode)
                                {
                                    _logAction($"[DEBUG] Error checking file {entry.name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }, cancellationToken.Token));
            }
            
            // Wait for all file scanning to complete
            if (scanTasks.Count > 0)
            {
                await Task.WhenAll(scanTasks);
                scanTasks.Clear();
            }

            // If there are any files to download, we're done with the quick check
            if (_filesToDownload.Count > 0)
            {
                _logAction($"Found {_filesToDownload.Count} file(s) that need to be patched.");
                
                // Print out map files detected (for debugging)
                if (_isDebugMode)
                {
                    var mapFilesToDownload = _filesToDownload.Where(f =>
                        f.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                        f.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                        f.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (mapFilesToDownload.Any())
                    {
                        _logAction($"[DEBUG] Found {mapFilesToDownload.Count} map files that need downloading:");
                        foreach (var map in mapFilesToDownload.Take(10))
                        {
                            _logAction($"[DEBUG] Map: {map.name}");
                        }
                        if (mapFilesToDownload.Count > 10)
                        {
                            _logAction($"[DEBUG] And {mapFilesToDownload.Count - 10} more map files...");
                        }
                    }
                }
                
                // Update integrity check status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "failed";
                await Task.Run(() => IniLibrary.Save());
                
                return false;
            }

            // If quick check passed and this is an automatic scan, we're done
            if (quickCheckPassedInt == 1 && !fullScanOnly)
            {
                // Update integrity check timestamp and status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "success";
                await Task.Run(() => IniLibrary.Save());
                
                _logAction("Quick scan complete - all files are up to date");
                // Final progress - 100%
                _progressAction(10000);
                return true;
            }

            // If we have UI files already queued for download, skip the full scan
            if (missingOrModifiedFiles.Any(f => f.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) ||
                                            f.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase)))
            {
                _logAction("Missing UI files detected - initiating patch...");
                
                // Add all missing files to download queue if they're not already there
                foreach (var entry in missingOrModifiedFiles)
                {
                    if (!_filesToDownload.Any(f => f.name == entry.name))
                    {
                        _filesToDownload.Add(entry);
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Added missing file to download queue: {entry.name}");
                        }
                    }
                }
                
                // Update integrity check status
                IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
                IniLibrary.instance.QuickCheckStatus = "failed";
                await Task.Run(() => IniLibrary.Save());
                
                _logAction($"{missingOrModifiedFiles.Count} file(s) need updating");
                return false;
            }

            // If quick check failed or full scan was requested, do a full integrity check
            _logAction("Performing full file check (MD5)...");
            bool allFilesIntact = true;
            int allFilesIntactInt = 1; // 1 = intact, 0 = modified
            checkedFiles = 0;
            
            // Reset lists for full scan
            var fullScanTasks = new List<Task>();
            var fullScanResults = new ConcurrentBag<FileEntry>();
            
            // Parallelize the MD5 calculations using partitioning for better load balancing
            var filePartitions = Partitioner.Create(filelist.downloads, true).GetPartitions(MAX_CONCURRENT_FILE_CHECKS);
            foreach (var partition in filePartitions)
            {
                fullScanTasks.Add(Task.Run(async () => 
                {
                    using (partition)
                    {
                        while (partition.MoveNext())
                        {
                            var entry = partition.Current;
                            
                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logAction("File scan cancelled.");
                                break;
                            }
                            
                            // Skip already known modified files
                            if (missingOrModifiedFiles.Any(f => f.name == entry.name))
                            {
                                int filesDone = Interlocked.Increment(ref checkedFiles);
                                int progress = 5000 + (int)((double)filesDone / totalFiles * 5000);
                                _progressAction(progress);
                                continue;
                            }
                            
                            try
                            {
                                if (entry.name.Equals("heroesjourneyemu.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                
                                var path = Path.Combine(baseDirectory, entry.name.Replace("/", "\\"));
                                if (!UtilityLibrary.IsPathChild(path))
                                {
                                    continue;
                                }
                                
                                // Check if file exists (double check in case it was deleted since quick scan)
                                if (!File.Exists(path))
                                {
                                    if (!missingOrModifiedFiles.Any(f => f.name == entry.name))
                                    {
                                        // Only log in debug mode or for non-dinput8 files
                                        if (_isDebugMode)
                                        {
                                            if (entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase))
                                            {
                                                _logAction($"[DEBUG] Missing dinput8.dll detected");
                                            }
                                            else
                                            {
                                                _logAction($"[DEBUG] Missing file detected: {entry.name}");
                                            }
                                        }
                                        else if (!entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase))
                                        {
                                            _logAction($"Missing file detected: {entry.name}");
                                        }
                                        
                                        fullScanResults.Add(entry);
                                        
                                        // Add to download queue
                                        lock (_filesToDownload)
                                        {
                                            if (!_filesToDownload.Any(f => f.name == entry.name))
                                            {
                                                bool isMapFile = entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                                               entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                                               entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
                                                    
                                                bool isUiFile = entry.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) ||
                                                               entry.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase);
                                                    
                                                bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                                                    
                                                // Always add UI files, map files, and dinput8.dll to download queue
                                                if (isUiFile || isMapFile || isDinput8)
                                                {
                                                    _filesToDownload.Add(entry);
                                                    
                                                    if (_isDebugMode)
                                                    {
                                                        string fileType = isDinput8 ? "DLL" : (isMapFile ? "map" : "UI");
                                                        _logAction($"[DEBUG] Added {fileType} file to download queue from full scan: {entry.name}");
                                                    }
                                                }
                                                else
                                                {
                                                    _filesToDownload.Add(entry);
                                                }
                                            }
                                        }
                                    }
                                    Interlocked.Exchange(ref allFilesIntactInt, 0); // Thread-safe set to false
                                }
                                else
                                {
                                    // Check hash if necessary
                                    if (File.Exists(path))
                                    {
                                        try
                                        {
                                            // Use cached hash calculation with memory mapping for large files
                                            string hash = await GetCachedFileHashAsync(path, entry.name);
                                            
                                            // Check if hash matches
                                            if (hash != entry.md5.ToUpperInvariant())
                                            {
                                                if (_isDebugMode)
                                                {
                                                    _logAction($"[DEBUG] MD5 mismatch for {entry.name}");
                                                    _logAction($"[DEBUG] Expected: {entry.md5.ToUpperInvariant()}");
                                                    _logAction($"[DEBUG] Got: {hash}");
                                                }
                                                
                                                if (entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (_isDebugMode)
                                                    {
                                                        _logAction($"[DEBUG] dinput8.dll is outdated");
                                                        _logAction($"[DEBUG] Current MD5: {hash}");
                                                        _logAction($"[DEBUG] Expected MD5: {entry.md5.ToUpperInvariant()}");
                                                    }
                                                }
                                                else
                                                {
                                                    _logAction($"Content mismatch detected: {entry.name}");
                                                }
                                                
                                                if (!missingOrModifiedFiles.Any(f => f.name == entry.name))
                                                {
                                                    fullScanResults.Add(entry);
                                                    
                                                    // Add to download queue
                                                    lock (_filesToDownload)
                                                    {
                                                        if (!_filesToDownload.Any(f => f.name == entry.name))
                                                        {
                                                            bool isMapFile = entry.name.StartsWith("maps\\", StringComparison.OrdinalIgnoreCase) ||
                                                                           entry.name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
                                                                           entry.name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
                                                            
                                                            bool isUiFile = entry.name.StartsWith("uifiles\\", StringComparison.OrdinalIgnoreCase) ||
                                                                           entry.name.StartsWith("uifiles/", StringComparison.OrdinalIgnoreCase);
                                                            
                                                            bool isDinput8 = entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase);
                                                            
                                                            // Always add UI files, map files, and dinput8.dll to download queue
                                                            if (isUiFile || isMapFile || isDinput8)
                                                            {
                                                                _filesToDownload.Add(entry);
                                                                
                                                                if (_isDebugMode)
                                                                {
                                                                    string fileType = isDinput8 ? "DLL" : (isMapFile ? "map" : "UI");
                                                                    _logAction($"[DEBUG] Added {fileType} file to download queue from full scan: {entry.name}");
                                                                }
                                                            }
                                                            else
                                                            {
                                                                _filesToDownload.Add(entry);
                                                            }
                                                    }
                                                }
                                                Interlocked.Exchange(ref allFilesIntactInt, 0); // Thread-safe set to false
                                            }
                                            else if (entry.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase) && _isDebugMode)
                                            {
                                                _logAction($"[DEBUG] dinput8.dll is up to date (MD5: {hash})");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            if (_isDebugMode)
                                            {
                                                _logAction($"[DEBUG] Error checking MD5 for {entry.name}: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                                
                                // Update progress after each file
                                int filesDone = Interlocked.Increment(ref checkedFiles);
                                int progress = 5000 + (int)((double)filesDone / totalFiles * 5000);
                                _progressAction(progress);
                            }
                            catch (Exception ex)
                            {
                                if (_isDebugMode)
                                {
                                    _logAction($"[DEBUG] Error processing file {entry.name}: {ex.Message}");
                                }
                                
                                // Update progress even after errors
                                int filesDone = Interlocked.Increment(ref checkedFiles);
                                int progress = 5000 + (int)((double)filesDone / totalFiles * 5000);
                                _progressAction(progress);
                            }
                        }
                    }
                }, cancellationToken.Token));
            }
            
            // Wait for all full scan tasks to complete
            if (fullScanTasks.Count > 0)
            {
                await Task.WhenAll(fullScanTasks);
            }

            // Add all results from full scan to our list
            foreach (var entry in fullScanResults)
            {
                if (!missingOrModifiedFiles.Any(f => f.name == entry.name))
                {
                    missingOrModifiedFiles.Add(entry);
                }
            }

            // Update integrity check timestamp and status
            IniLibrary.instance.LastIntegrityCheck = DateTime.UtcNow.ToString("O");
            IniLibrary.instance.QuickCheckStatus = allFilesIntactInt == 1 ? "success" : "failed";
            await Task.Run(() => IniLibrary.Save());

            // Report results
            if (missingOrModifiedFiles.Count == 0 && allFilesIntactInt == 1)
            {
                _logAction("Scan complete - all files are up to date");
                
                // If files are intact but versions differ, update the version
                IniLibrary.instance.LastPatchedVersion = filelist.version;
                await Task.Run(() => IniLibrary.Save());
                
                // Final progress - 100%
                _progressAction(10000);
                return true;
            }
            else
            {
                _logAction($"Scan complete - {missingOrModifiedFiles.Count} file(s) need updating");
                return false;
            }
        }
        
        /// <summary>
        /// Process dinput8.dll with special handling
        /// </summary>
        private async Task ProcessDinput8FileAsync(
            FileEntry dinput8File, 
            string baseDirectory, 
            CancellationTokenSource cancellationToken,
            ConcurrentBag<FileEntry> missingOrModifiedFiles)
        {
            var path = Path.Combine(baseDirectory, dinput8File.name.Replace("/", "\\"));
            
            if (!File.Exists(path))
            {
                _logAction($"[Important] Missing dinput8.dll detected - will be downloaded");
                missingOrModifiedFiles.Add(dinput8File);
                
                // Add to download queue
                lock (_filesToDownload)
                {
                    if (!_filesToDownload.Any(f => f.name == dinput8File.name))
                    {
                        _filesToDownload.Add(dinput8File);
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Added missing dinput8.dll to download queue");
                        }
                    }
                }
                return;
            }
            
            // Always check MD5 hash for dinput8.dll - it's small so no caching needed
            var md5 = await Task.Run(() => UtilityLibrary.GetMD5(path));
            if (md5.ToUpper() != dinput8File.md5.ToUpper())
            {
                if (_isDebugMode)
                {
                    _logAction($"[DEBUG] Current MD5: {md5.ToUpper()}");
                    _logAction($"[DEBUG] Expected MD5: {dinput8File.md5.ToUpper()}");
                }
                
                _logAction($"[Important] dinput8.dll is outdated - will be updated");
                missingOrModifiedFiles.Add(dinput8File);
                
                // Add to download queue
                lock (_filesToDownload)
                {
                    if (!_filesToDownload.Any(f => f.name == dinput8File.name))
                    {
                        _filesToDownload.Add(dinput8File);
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Added outdated dinput8.dll to download queue");
                        }
                    }
                }
            }
            else if (_isDebugMode)
            {
                _logAction($"[DEBUG] dinput8.dll is up to date (MD5: {md5.ToUpper()})");
            }
        }

        /// <summary>
        /// Calculates MD5 hash with caching for unchanged files to improve performance
        /// </summary>
        private async Task<string> GetCachedFileHashAsync(string path, string filename)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists) return string.Empty;
                
                // Check if we have this file in our cache with matching timestamp
                string cacheKey = Path.GetFullPath(path).ToLowerInvariant();
                if (_fileHashCache.TryGetValue(cacheKey, out var cachedValue))
                {
                    if (cachedValue.LastModified == fileInfo.LastWriteTimeUtc)
                    {
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Using cached hash for {filename}");
                        }
                        return cachedValue.Hash;
                    }
                }
                
                // Not in cache or changed, calculate hash (memory-mapped for large files)
                string hash = await Task.Run(() => 
                    UtilityLibrary.GetMD5(path, useMemoryMappedFile: true, largeFileThreshold: LARGE_FILE_THRESHOLD));
                
                // Store in cache
                _fileHashCache[cacheKey] = (hash, fileInfo.LastWriteTimeUtc);
                return hash;
            }
            catch (Exception ex)
            {
                if (_isDebugMode)
                {
                    _logAction($"[DEBUG] Error calculating hash for {filename}: {ex.Message}");
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Forces a check of dinput8.dll to ensure it's up to date
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if dinput8.dll is up to date, false otherwise</returns>
        public async Task<bool> ForceDinput8CheckAsync(CancellationTokenSource cancellationToken)
        {
            _logAction("Checking dinput8.dll integrity...");
            
            // Download and parse the filelist just to check dinput8.dll
            string suffix = "rof";
            string webUrl = $"{_filelistUrl}/filelist_{suffix}.yml";
            
            // Try to download the filelist
            string filelistResponse = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, "filelist.yml");
            if (filelistResponse != "")
            {
                string fallbackUrl = "https://patch.heroesjourneyemu.com";
                webUrl = $"{fallbackUrl}/filelist_{suffix}.yml";
                filelistResponse = await UtilityLibrary.DownloadFile(cancellationToken, webUrl, "filelist.yml");
                if (filelistResponse != "")
                {
                    _logAction("[Warning] Could not download filelist to check dinput8.dll. Using existing files.");
                    return true;
                }
            }
            
            // Read the filelist
            FileList filelist;
            string filelistPath = $"{Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName)}\\filelist.yml";
            
            using (var input = File.OpenText(filelistPath))
            {
                var deserializerBuilder = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializerBuilder.Deserialize<FileList>(input);
            }
            
            // Find dinput8.dll entry
            FileEntry dinput8Entry = filelist.downloads.FirstOrDefault(e => e.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase));
            if (dinput8Entry == null)
            {
                _logAction("[Warning] dinput8.dll not found in filelist.");
                return true;
            }
            
            // Check if dinput8.dll exists and has correct MD5
            string dinput8Path = Path.Combine(
                Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), 
                dinput8Entry.name.Replace("/", "\\"));
            
            if (!File.Exists(dinput8Path))
            {
                _logAction("[Warning] dinput8.dll missing - will add to download queue.");
                if (!_filesToDownload.Any(f => f.name == dinput8Entry.name))
                {
                    _filesToDownload.Add(dinput8Entry);
                }
                return false;
            }
            
            // Check MD5
            string md5 = await Task.Run(() => UtilityLibrary.GetMD5(dinput8Path));
            if (md5.ToUpper() != dinput8Entry.md5.ToUpper())
            {
                _logAction("[Warning] dinput8.dll MD5 mismatch - will add to download queue.");
                if (_isDebugMode)
                {
                    _logAction($"[DEBUG] Current MD5: {md5.ToUpper()}");
                    _logAction($"[DEBUG] Expected MD5: {dinput8Entry.md5.ToUpper()}");
                }
                
                if (!_filesToDownload.Any(f => f.name == dinput8Entry.name))
                {
                    _filesToDownload.Add(dinput8Entry);
                }
                return false;
            }
            
            _logAction("dinput8.dll is up to date.");
            return true;
        }

        /// <summary>
        /// Builds the file list for patching based on the filelist information
        /// </summary>
        /// <param name="filelist">The file list containing files to download and delete</param>
        /// <param name="cts">Cancellation token source</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task BuildFileList(FileList filelist, CancellationTokenSource cts)
        {
            // Clear any existing download list
            _filesToDownload.Clear();
            
            // If version mismatch, check files
            bool patchNeeded = (filelist.version != IniLibrary.instance.LastPatchedVersion);
            
            if (patchNeeded)
            {
                _logAction($"Version change detected: {IniLibrary.instance.LastPatchedVersion ?? "unknown"} -> {filelist.version}");
            }
            
            // Download delete.txt if it exists
            try
            {
                string deleteUrl = filelist.downloadprefix;
                if (!deleteUrl.EndsWith("/")) deleteUrl += "/";
                deleteUrl += "delete.txt";
                
                // Create a progress callback for delete.txt download
                Action<long, long> progressCallback = (bytesRead, totalBytes) => 
                {
                    // Progress for delete.txt is minor, just use 0-5% range
                    int progressPercentage = (int)((double)bytesRead / totalBytes * 500);
                    _progressAction(Math.Min(progressPercentage, 500)); 
                };
                
                byte[] deleteData = await UtilityLibrary.Download(cts, deleteUrl, progressCallback);
                if (deleteData != null && deleteData.Length > 0)
                {
                    string deleteContent = System.Text.Encoding.Default.GetString(deleteData);
                    string[] lines = deleteContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        {
                            // Add to deletion list if not already there
                            if (!filelist.deletes.Any(d => d.name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                            {
                                filelist.deletes.Add(new FileEntry { name = trimmed });
                                if (_isDebugMode)
                                {
                                    _logAction($"[DEBUG] Added file to delete list from delete.txt: {trimmed}");
                                }
                            }
                        }
                    }
                    
                    _logAction($"Loaded additional {lines.Length} file(s) to delete");
                }
            }
            catch (Exception ex)
            {
                if (_isDebugMode)
                {
                    _logAction($"[DEBUG] Failed to download delete.txt: {ex.Message}");
                }
            }
            
            // Perform file scanning to determine what needs downloading
            if (_filesToDownload.Count == 0 || patchNeeded)
            {
                _logAction("Checking files...");
                
                // Update progress as we scan - continue from previous progress (5%)
                int totalFiles = filelist.downloads.Count;
                int checkedFiles = 0;
                
                foreach (var entry in filelist.downloads)
                {
                    // Check for cancellation
                    if (cts.IsCancellationRequested)
                    {
                        _logAction("File scan cancelled.");
                        return;
                    }
                    
                    // Update progress - file scanning is 5-100%
                    checkedFiles++;
                    _progressAction(500 + (int)((double)checkedFiles / totalFiles * 9500));
                    
                    // Skip the patcher executable itself
                    if (entry.name.Equals("heroesjourneyemu", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    var path = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath) + "\\" + entry.name.Replace("/", "\\");
                    if (!await Task.Run(() => UtilityLibrary.IsPathChild(path)))
                    {
                        _logAction($"[Warning] Path {entry.name} might be outside your EverQuest directory.");
                        continue;
                    }
                    
                    if (!await Task.Run(() => File.Exists(path)))
                    {
                        if (_isDebugMode)
                        {
                            _logAction($"[DEBUG] Missing file detected: {entry.name}");
                        }
                        
                        if (!_filesToDownload.Any(f => f.name == entry.name))
                        {
                            _filesToDownload.Add(entry);
                        }
                    }
                    else
                    {
                        var md5 = await GetCachedFileHashAsync(path, entry.name);
                        if (md5.ToUpper() != entry.md5.ToUpper())
                        {
                            if (_isDebugMode)
                            {
                                _logAction($"[DEBUG] MD5 mismatch for {entry.name}");
                                _logAction($"[DEBUG] Expected: {entry.md5.ToUpper()}");
                                _logAction($"[DEBUG] Got: {md5.ToUpper()}");
                            }
                            
                            if (!_filesToDownload.Any(f => f.name == entry.name))
                            {
                                _filesToDownload.Add(entry);
                            }
                        }
                    }
                }
            }
            
            // No progress reset here
            
            // If no files need updating, we can update the LastPatchedVersion
            if (_filesToDownload.Count == 0 && patchNeeded)
            {
                IniLibrary.instance.LastPatchedVersion = filelist.version;
                await Task.Run(() => IniLibrary.Save());
                _logAction($"Version updated to {filelist.version}");
            }
            
            // Log results
            if (_filesToDownload.Count > 0)
            {
                _logAction($"Found {_filesToDownload.Count} file(s) that need patching");
            }
        }
    }
} 