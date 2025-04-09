#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http;
using System.Threading;
using YamlDotNet.Core.Tokens;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Policy;
using System.IO.MemoryMappedFiles;

namespace THJPatcher
{
    /* General Utility Methods */
    public static class UtilityLibrary
    {
        // Win32 constants for window management
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, 
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        //Download a file to current directory
        public static async Task<string> DownloadFile(CancellationTokenSource cts, string url, string outFile, Action<long, long> progressCallback = null)
        {
            try
            {
                var client = new HttpClient();
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                
                // Get the total size of the file we're downloading
                long? totalBytes = response.Content.Headers.ContentLength;
                if (totalBytes == null)
                {
                    totalBytes = 1000000; // Default size if we can't determine
                }
                
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var outPath = outFile.Replace("/", "\\");
                    if (outFile.Contains("\\")) { //Make directory if needed.
                        string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile.Substring(0, outFile.LastIndexOf("\\"));
                        Directory.CreateDirectory(dir);
                    }
                    outPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile;

                    using (var w = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 
                        bufferSize: 131072, useAsync: true)) 
                    {
                        // Use a larger buffer size of 128KB for better throughput
                        byte[] buffer = new byte[131072];
                        long totalBytesRead = 0;
                        int bytesRead;
                        
                        // Read in chunks and report progress
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                        {
                            await w.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            
                            // Report progress through callback if provided
                            progressCallback?.Invoke(totalBytesRead, totalBytes.Value);
                        }
                    }
                }
            } 
            catch(ArgumentNullException e)
            {
                return "ArgumentNullExpception: " + e.Message;
            } 
            catch(HttpRequestException e)
            {
                return "HttpRequestException: " + e.Message;
            } 
            catch (Exception e)
            {
                return "Exception: " + e.Message;
            }
            return "";
        }

        /// <summary>
        /// Downloads a file, saves it, and verifies its hash against an expected value simultaneously.
        /// </summary>
        /// <param name="cts">Cancellation token source.</param>
        /// <param name="url">URL to download from.</param>
        /// <param name="outFile">Relative path to save the file.</param>
        /// <param name="expectedHash">The expected MD5 hash (uppercase).</param>
        /// <param name="progressCallback">Callback for download progress.</param>
        /// <returns>Tuple: (bool success, string? actualHash) - success indicates download AND hash match, actualHash is the computed hash or null on error.</returns>
        public static async Task<(bool success, string? actualHash)> DownloadFileAndVerifyHashAsync(
            CancellationTokenSource cts, 
            string url, 
            string outFile, 
            string expectedHash, 
            Action<long, long>? progressCallback = null)
        {
            string? actualHash = null;
            try
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                if (!totalBytes.HasValue)
                {
                    // Cannot reliably report progress or verify size easily without content length
                    // Consider logging a warning or handling this case differently if needed
                    return (false, null); // Indicate failure if content length is missing
                }

                var outPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty, outFile.Replace("/", "\\"));
                string? directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 
                    bufferSize: 131072, useAsync: true); // 128KB buffer, optimized for async
                using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var md5 = MD5.Create();
                using var cryptoStream = new CryptoStream(fileStream, md5, CryptoStreamMode.Write);

                byte[] buffer = new byte[131072]; // Increased to 128 KB buffer for better throughput
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await cryptoStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    totalBytesRead += bytesRead;
                    progressCallback?.Invoke(totalBytesRead, totalBytes.Value);
                }

                // Finalize the hash computation
                cryptoStream.FlushFinalBlock(); 
                byte[] hashBytes = md5.Hash ?? Array.Empty<byte>();
                actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();

                // Verify hash
                bool hashMatch = actualHash.Equals(expectedHash.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
                
                // Ensure file stream is properly closed before returning
                await fileStream.FlushAsync(cts.Token); 
                fileStream.Close(); 

                return (hashMatch, actualHash);
            }
            catch (OperationCanceledException)
            {
                // Don't return error message, just indicate failure
                return (false, null); 
            }
            catch (Exception ex) // Catch more specific exceptions if needed
            {
                // Log the exception details for debugging
                Debug.WriteLine($"[Error] Download/Verify failed for {url}: {ex.Message}"); 
                return (false, actualHash); // Return actual hash if computed, but indicate failure
            }
        }

        // Download will grab a remote URL's file and return the data as a byte array
        public static async Task<byte[]> Download(CancellationTokenSource cts, string url, Action<long, long> progressCallback = null)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            
            // Get the total size of the file we're downloading
            long? totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes == null)
            {
                totalBytes = 1000000; // Default size if we can't determine
            }
            
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                using (var w = new MemoryStream(capacity: 131072)) // Pre-allocate with larger capacity
                {
                    // Use a larger buffer size of 128KB
                    byte[] buffer = new byte[131072];
                    long totalBytesRead = 0;
                    int bytesRead;
                    
                    // Read in chunks and report progress
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                    {
                        await w.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;
                        
                        // Report progress through callback if provided
                        progressCallback?.Invoke(totalBytesRead, totalBytes.Value);
                    }
                    
                    return w.ToArray();
                }
            }
        }

        /// <summary>
        /// Computes the MD5 hash of a file, using memory-mapped I/O for large files.
        /// </summary>
        /// <param name="filename">The path to the file.</param>
        /// <param name="useMemoryMappedFile">Whether to use memory mapping for large files.</param>
        /// <param name="largeFileThreshold">Size threshold in bytes for using memory mapping.</param>
        /// <returns>The uppercase MD5 hash string.</returns>
        public static string GetMD5(string filename, bool useMemoryMappedFile = true, long largeFileThreshold = 100 * 1024 * 1024)
        {
            try
            {
                var fileInfo = new FileInfo(filename);
                
                // For large files, use a chunked approach with a larger buffer
                if (fileInfo.Length > largeFileThreshold)
                {
                    using (var md5 = MD5.Create())
                    {
                        using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 
                                                         262144, FileOptions.SequentialScan))
                        {
                            // Process in 64MB chunks to avoid excessive memory usage
                            byte[] buffer = new byte[262144]; // 256KB buffer
                            int bytesRead;
                            
                            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                            }
                            
                            md5.TransformFinalBlock(buffer, 0, 0);
                            if (md5.Hash == null) return string.Empty;
                            
                            StringBuilder sb = new StringBuilder(md5.Hash.Length * 2);
                            foreach (byte b in md5.Hash)
                            {
                                sb.Append(b.ToString("X2"));
                            }
                            return sb.ToString();
                        }
                    }
                }
                else
                {
                    // Use the regular file streaming approach for normal-sized files
                    using (var stream = File.OpenRead(filename))
                    {
                        return GetMD5(stream);
                    }
                }
            }
            catch (IOException ex) // Handle potential file access errors
            {
                 Debug.WriteLine($"[Error] Failed to open file for MD5 {filename}: {ex.Message}");
                 return string.Empty; // Or throw, depending on desired behavior
            }
        }

        /// <summary>
        /// Computes the MD5 hash of a stream.
        /// </summary>
        /// <param name="stream">The input stream.</param>
        /// <returns>The uppercase MD5 hash string, or empty string if hash is null.</returns>
        public static string GetMD5(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                if (hash == null)
                {
                     return string.Empty; 
                }
                // Use efficient StringBuilder allocation
                StringBuilder sb = new StringBuilder(hash.Length * 2); 
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }

        public static System.Diagnostics.Process StartEverquest()
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\eqgame.exe",
                Arguments = "patchme",
                WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false,
                LoadUserProfile = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var process = System.Diagnostics.Process.Start(startInfo);

            // Wait for the window to be created (up to 10 seconds)
            for (int i = 0; i < 20 && process.MainWindowHandle == IntPtr.Zero; i++)
            {
                Thread.Sleep(500);
                process.Refresh();
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                // Ensure window is not minimized
                if (IsIconic(process.MainWindowHandle))
                {
                    ShowWindow(process.MainWindowHandle, SW_RESTORE);
                }

                // Force window to show properly
                ShowWindow(process.MainWindowHandle, SW_SHOW);
                SetWindowPos(process.MainWindowHandle, IntPtr.Zero, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }

            return process;
        }

        //Pass the working directory (or later, you can pass another directory) and it returns a hash if the file is found
        public static string GetEverquestExecutableHash(string path)
        {
            var di = new System.IO.DirectoryInfo(path);
            var files = di.GetFiles("eqgame.exe");
            if (files == null || files.Length == 0)
            {
                return "";
            }
            return UtilityLibrary.GetMD5(files[0].FullName);
        }

        // Returns true only if the path is a relative and does not contain ..
        public static bool IsPathChild(string path)
        {
            // get the absolute path
            var absPath = Path.GetFullPath(path);
            var basePath = Path.GetDirectoryName(Application.ExecutablePath); 
            // check if absPath contains basePath
            if (!absPath.Contains(basePath))
            {
                return false;
            }
            if (path.Contains("..\\"))
            {
                return false;
            }
            return true;
        }
        
        // MoveFileEx flags for scheduling file operations
        [Flags]
        public enum MoveFileFlags
        {
            MOVEFILE_REPLACE_EXISTING = 0x00000001,
            MOVEFILE_COPY_ALLOWED = 0x00000002,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004,
            MOVEFILE_WRITE_THROUGH = 0x00000008
        }
        
        // Import the MoveFileEx function from kernel32.dll
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);
        
        // Wrapper method for MoveFileEx with a different name to avoid conflict
        public static bool ScheduleFileOperation(string existingFile, string newFile, MoveFileFlags flags)
        {
            return MoveFileEx(existingFile, newFile, flags);
        }
        
        /// <summary>
        /// Queues a file replacement using the Windows MoveFileEx API with MOVEFILE_DELAY_UNTIL_REBOOT flag.
        /// This schedules the file to be replaced on the next system reboot.
        /// </summary>
        /// <param name="sourceFile">The path to the source file (new file)</param>
        /// <param name="destinationFile">The path to the destination file (file to be replaced)</param>
        /// <returns>True if the file replacement was successfully queued, false otherwise</returns>
        public static bool QueueFileReplacement(string sourceFile, string destinationFile)
        {
            try
            {
                // Ensure both files exist
                if (!File.Exists(sourceFile))
                {
                    Debug.WriteLine($"[Error] Source file does not exist: {sourceFile}");
                    return false;
                }
                
                // Create the directory for the destination file if it doesn't exist
                string destinationDir = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }
                
                // Use MoveFileEx to schedule the file replacement on next reboot
                return MoveFileEx(sourceFile, destinationFile, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT | MoveFileFlags.MOVEFILE_REPLACE_EXISTING);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Error] Failed to queue file replacement: {ex.Message}");
                return false;
            }
        }
    }
}
