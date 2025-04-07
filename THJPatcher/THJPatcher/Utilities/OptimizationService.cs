using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace THJPatcher.Utilities
{
    /// <summary>
    /// Service responsible for handling game optimization and configuration tasks
    /// </summary>
    public class OptimizationService
    {
        private readonly Action<string> _logAction;
        private readonly Func<string, string, MessageBoxButton, MessageBoxImage, bool?> _showMessageAction;
        private readonly string _eqPath;

        /// <summary>
        /// Initializes a new instance of the OptimizationService
        /// </summary>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="showMessageAction">Action to display message boxes</param>
        /// <param name="eqPath">Path to the EverQuest directory</param>
        public OptimizationService(
            Action<string> logAction,
            Func<string, string, MessageBoxButton, MessageBoxImage, bool?> showMessageAction,
            string eqPath)
        {
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _showMessageAction = showMessageAction ?? ((message, title, button, icon) => null);
            _eqPath = eqPath ?? Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        }

        /// <summary>
        /// Applies the 4GB patch to eqgame.exe to allow using more memory
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task Apply4GBPatchAsync()
        {
            string eqExePath = Path.Combine(_eqPath, "eqgame.exe");
            _logAction("Checking for 4GB patch applicability...");

            try
            {
                if (!File.Exists(eqExePath))
                {
                    _showMessageAction("Could not find eqgame.exe", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if patch is already applied
                bool isPatchApplied = await Task.Run(() => PEModifier.Is4GBPatchApplied(eqExePath));
                if (isPatchApplied)
                {
                    _showMessageAction("4GB patch is already applied to eqgame.exe", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Confirm with user
                var result = _showMessageAction(
                    "Are you sure you want to apply the 4GB patch? This will enable EverQuest to use up to 4GB of RAM on 64-bit systems and will create a backup of the original file.",
                    "Confirm 4GB Patch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != true)
                {
                    return;
                }

                // Apply the patch
                bool success = await Task.Run(() => PEModifier.Apply4GBPatch(eqExePath));
                if (success)
                {
                    // Verify the patch was applied successfully
                    bool verifyPatch = await Task.Run(() => PEModifier.Is4GBPatchApplied(eqExePath));
                    if (verifyPatch)
                    {
                        _showMessageAction(
                            "Successfully applied 4GB patch. A backup of the original file has been created as eqgame.exe.bak",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        _showMessageAction(
                            "Failed to verify 4GB patch application. Please try again.",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    _showMessageAction(
                        "Failed to apply 4GB patch. The file may already be patched or is not compatible.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                _showMessageAction(fnfEx.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                _showMessageAction($"Error applying 4GB patch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Fixes the UI scale in eqclient.ini to 1.0
        /// </summary>
        public void FixUIScale()
        {
            _logAction("Setting UI scale to 1.0...");
            try
            {
                string eqcfgPath = Path.Combine(_eqPath, "eqclient.ini");

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
                    _showMessageAction("UI Scale has been set to 1.0", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _showMessageAction("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _showMessageAction($"Error fixing UI scale: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Optimizes graphics settings for better performance
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task OptimizeGraphicsAsync()
        {
            _logAction("Optimizing graphics settings...");
            try
            {
                string eqcfgPath = Path.Combine(_eqPath, "eqclient.ini");

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

                    _showMessageAction("Graphics settings have been optimized", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _showMessageAction("eqclient.ini not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _showMessageAction($"Error optimizing graphics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Clears cache directories for maps and database strings
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task ClearCacheAsync()
        {
            _logAction("Clearing cache directories...");
            try
            {
                string[] cacheDirs = { "dbstr", "maps" };

                await Task.Run(() =>
                {
                    foreach (string dir in cacheDirs)
                    {
                        string path = Path.Combine(_eqPath, dir);
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                            Directory.CreateDirectory(path);
                        }
                    }
                });

                _showMessageAction("Cache has been cleared", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _showMessageAction($"Error clearing cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Resets all game settings to defaults
        /// </summary>
        /// <returns>A task representing the asynchronous operation and a boolean indicating success</returns>
        public async Task<bool> ResetSettingsAsync()
        {
            _logAction("Resetting game settings...");
            try
            {
                return await Task.Run(() =>
                {
                    // List of files/directories to delete
                    string[] filesToDelete = {
                        "eqclient.ini",
                        "UI_Default.ini",
                        "UI_current.ini"
                    };

                    foreach (string file in filesToDelete)
                    {
                        string path = Path.Combine(_eqPath, file);
                        if (File.Exists(path))
                        {
                            try
                            {
                                File.Delete(path);
                            }
                            catch (Exception ex)
                            {
                                _logAction($"Error deleting {file}: {ex.Message}");
                                return false;
                            }
                        }
                    }

                    // Delete UserSettings directory if it exists
                    string userSettingsPath = Path.Combine(_eqPath, "UserSettings");
                    if (Directory.Exists(userSettingsPath))
                    {
                        try
                        {
                            Directory.Delete(userSettingsPath, true);
                        }
                        catch (Exception ex)
                        {
                            _logAction($"Error deleting UserSettings directory: {ex.Message}");
                            return false;
                        }
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                _logAction($"Error in ResetSettingsAsync: {ex.Message}");
                return false;
            }
        }
    }
} 