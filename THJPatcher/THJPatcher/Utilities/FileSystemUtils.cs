#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using THJPatcher.Models; // For FileEntry, FileList
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace THJPatcher.Utilities
{
    internal static class FileSystemUtils
    {
        // Checks the status of dinput8.dll against the latest filelist.
        // Returns the FileEntry for dinput8.dll if it needs an update, otherwise null.
        internal static async Task<FileEntry?> ForceDinput8CheckAsync(
            string eqPath,
            string primaryFilelistUrl,
            string fallbackFilelistUrl, // Assuming RoF suffix is handled by caller
            CancellationTokenSource cts,
            bool isDebugMode,
            Action<string> log)
        {
            if (isDebugMode) log("[DEBUG] Silently checking dinput8.dll for updates...");

            string dinput8Path = Path.Combine(eqPath, "dinput8.dll");
            string localFilelistPath = Path.Combine(eqPath, "filelist_dinput_check.yml"); // Use a distinct name

            // Download the filelist (using fallback logic)
            string filelistContent = "";
            string webUrl = primaryFilelistUrl; // e.g., https://.../filelist_rof.yml
            bool primarySucceeded = false;

            try
            {
                 // Use UtilityLibrary's DownloadFile which includes timeout handling
                 string downloadResult = await UtilityLibrary.DownloadFile(cts, webUrl, localFilelistPath, null);
                 if (string.IsNullOrEmpty(downloadResult))
                 {
                     filelistContent = await File.ReadAllTextAsync(localFilelistPath);
                     if (isDebugMode) log("[DEBUG] Successfully downloaded filelist from primary URL for dinput8 check");
                     primarySucceeded = true;
                 }
                 else 
                 {
                      if (isDebugMode) log($"[DEBUG] Primary filelist download failed for dinput8 check: {downloadResult}");
                 }
            }
            catch (Exception ex)
            { 
                 if (isDebugMode) log($"[DEBUG] Exception downloading primary filelist for dinput8 check: {ex.Message}");
            }

            if (!primarySucceeded && !string.IsNullOrEmpty(fallbackFilelistUrl))
            {
                webUrl = fallbackFilelistUrl;
                try
                {
                     string downloadResult = await UtilityLibrary.DownloadFile(cts, webUrl, localFilelistPath);
                     if (string.IsNullOrEmpty(downloadResult))
                     {
                        filelistContent = await File.ReadAllTextAsync(localFilelistPath);
                        if (isDebugMode) log("[DEBUG] Successfully downloaded filelist from fallback URL for dinput8 check");
                     }
                     else
                     {
                        if (isDebugMode) log($"[DEBUG] Fallback filelist download failed for dinput8 check: {downloadResult}");
                     }
                }
                catch (Exception ex)
                {
                     if (isDebugMode) log($"[DEBUG] Exception downloading fallback filelist for dinput8 check: {ex.Message}");
                }
            }
            
            // Clean up downloaded filelist check file
            try { if(File.Exists(localFilelistPath)) File.Delete(localFilelistPath); }
            catch(Exception ex) { if(isDebugMode) log($"[DEBUG] Failed to delete temp dinput8 check filelist: {ex.Message}"); }

            if (string.IsNullOrEmpty(filelistContent))
            {
                if (isDebugMode) log("[DEBUG] Could not download filelist to check dinput8.dll");
                return null;
            }

            // Parse the filelist
            FileList filelist = null;
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                filelist = deserializer.Deserialize<FileList>(filelistContent);
                if (isDebugMode) log("[DEBUG] Successfully parsed filelist for dinput8 check");
            }
            catch (Exception ex)
            {
                if (isDebugMode) log($"[DEBUG] Failed to parse filelist for dinput8 check: {ex.Message}");
                return null;
            }

            if (filelist?.downloads == null)
            {
                if (isDebugMode) log("[DEBUG] Invalid filelist format for dinput8 check");
                return null;
            }

            // Find dinput8.dll in the filelist
            FileEntry dinput8Entry = filelist.downloads.FirstOrDefault(e => e.name.EndsWith("dinput8.dll", StringComparison.OrdinalIgnoreCase));

            if (dinput8Entry == null)
            {
                if (isDebugMode) log("[DEBUG] Could not find dinput8.dll in filelist");
                return null;
            }

            // Check if dinput8.dll exists locally and compare hash
            bool needsUpdate = false;
            if (!File.Exists(dinput8Path))
            {
                if (isDebugMode) log("[DEBUG] dinput8.dll not found, needs update");
                needsUpdate = true;
            }
            else
            {
                string localMd5 = await Task.Run(() => UtilityLibrary.GetMD5(dinput8Path)); // MD5 check on background thread
                if (!localMd5.Equals(dinput8Entry.md5, StringComparison.OrdinalIgnoreCase))
                {
                    if (isDebugMode)
                    {
                        log("[DEBUG] dinput8.dll is outdated, needs update");
                        log($"[DEBUG] Current MD5: {localMd5.ToUpper()}");
                        log($"[DEBUG] Expected MD5: {dinput8Entry.md5.ToUpper()}");
                    }
                    needsUpdate = true;
                }
                else if (isDebugMode)
                {
                    log("[DEBUG] dinput8.dll is up to date");
                }
            }

            return needsUpdate ? dinput8Entry : null;
        }

        // Removes known conflicting DLL files from the specified path.
        internal static async Task RemoveConflictingFilesAsync(string gamePath, bool isDebugMode, Action<string> log)
        {
            // List of files that can cause conflicts if left loose alongside the embedded ones.
             string[] conflictingFiles = {
                    "MaterialDesignThemes.Wpf.dll",
                    "Microsoft.Xaml.Behaviors.dll"
                };

             await Task.Run(() => // Perform file operations on background thread
             {
                foreach (string file in conflictingFiles)
                {
                    string filePath = Path.Combine(gamePath, file);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            if (isDebugMode) log($"[DEBUG] Removing conflicting file: {file}");
                            File.Delete(filePath);
                            log($"Removed conflicting file: {file}");
                        }
                        catch (IOException ioEx)
                        {
                             log($"[Warning] Failed to remove {file} (IOException): {ioEx.Message}");
                             if (isDebugMode) log($"[DEBUG] Full path: {filePath}");
                        }
                         catch (UnauthorizedAccessException uaEx)
                        {
                             log($"[Warning] Failed to remove {file} (Unauthorized): {uaEx.Message}");
                             if (isDebugMode) log($"[DEBUG] Full path: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            log($"[Warning] Failed to remove {file}: {ex.Message}");
                            if (isDebugMode) log($"[DEBUG] Full path: {filePath}");
                        }
                    }
                }
            });
        }

        // Checks for dinput8.dll.new and attempts to replace dinput8.dll.
        // Schedules replacement on reboot if necessary and possible.
        internal static async Task HandlePendingDinput8Async(string eqPath, bool isDebugMode, Func<bool> isAdministrator, Action<string> log)
        {
            string dinput8Path = Path.Combine(eqPath, "dinput8.dll");
            string dinput8NewPath = Path.Combine(eqPath, "dinput8.dll.new");

            try
            {
                // Check if there's a pending dinput8.dll.new file
                if (File.Exists(dinput8NewPath))
                {
                    log("Found pending dinput8.dll.new file from previous update");

                    // Check if the target dinput8.dll is currently in use
                    bool isFileInUse = false;
                    if (File.Exists(dinput8Path))
                    {
                        try
                        {
                            using (FileStream fs = File.Open(dinput8Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                fs.Close(); // File is not locked
                            }
                        }
                        catch (IOException)
                        {
                            isFileInUse = true; // File is locked
                            if(isDebugMode) log("[DEBUG] dinput8.dll appears to be in use.");
                        }
                        catch(Exception ex)
                        {
                            // Log other potential errors checking file lock
                             if(isDebugMode) log($"[DEBUG] Error checking dinput8.dll lock status: {ex.Message}");
                        }
                    }

                    // Attempt direct replacement if not in use or target doesn't exist
                    if (!isFileInUse)
                    {
                         try 
                         {
                             log("Attempting to replace/install dinput8.dll with pending update");
                             if (File.Exists(dinput8Path)) File.Delete(dinput8Path);
                             File.Move(dinput8NewPath, dinput8Path);
                             log("Successfully updated/installed dinput8.dll from pending file.");
                             return; // Success
                         }
                         catch(Exception ex)
                         {
                            log($"[Warning] Failed direct replacement/move of dinput8.dll: {ex.Message}");
                            // Fall through to scheduled move if possible
                         }
                    }
                    
                    // If file is in use, try scheduling replacement on reboot (requires admin)
                    if (isFileInUse && isAdministrator()) // Check admin rights via callback
                    {
                        log("Attempting to schedule dinput8.dll replacement on next reboot");
                        try
                        {
                            // Use UtilityLibrary's wrapper for MoveFileEx
                            if (UtilityLibrary.ScheduleFileOperation(dinput8NewPath, dinput8Path,
                                UtilityLibrary.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT | UtilityLibrary.MoveFileFlags.MOVEFILE_REPLACE_EXISTING))
                            {
                                log("dinput8.dll will be updated on next reboot");
                                // We consider this "handled" for this run, even if reboot is needed.
                                // The .new file remains until reboot.
                                return; 
                            }
                            else
                            {
                                log("[Warning] Failed to schedule dinput8.dll replacement (MoveFileEx returned false)");
                            }
                        }
                        catch (Exception ex)
                        {
                             log($"[Warning] Failed to schedule dinput8.dll replacement (Exception): {ex.Message}");
                        }
                    }
                    else if (isFileInUse && !isAdministrator())
                    {
                         if (isDebugMode) log("[DEBUG] Administrator privileges required to schedule dinput8.dll replacement.");
                         log("[Warning] Could not update dinput8.dll as it is in use and administrator privileges are not available.");
                    }
                    // If we reach here, replacement failed or couldn't be scheduled.
                    // The .new file remains.
                }
            }
            catch (Exception ex)
            {
                log($"[Warning] Error checking for/handling pending dinput8.dll update: {ex.Message}");
            }
        }

        // Methods for file system operations and checks will be added here.
    }
} 