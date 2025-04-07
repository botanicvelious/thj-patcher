using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using THJPatcher.Utilities; // For PEModifier

namespace THJPatcher.Utilities
{
    internal static class OptimizationUtils
    {
        // Return value indicates: true = success, false = failure, null = already applied or user cancelled.
        internal static async Task<bool?> Apply4GBPatchAsync(string eqExePath, Func<string, bool> confirmAction)
        {
            if (!File.Exists(eqExePath))
            {
                throw new FileNotFoundException("Could not find eqgame.exe", eqExePath);
            }

            // First check if patch is already applied
            bool isPatchApplied = await Task.Run(() => PEModifier.Is4GBPatchApplied(eqExePath));
            if (isPatchApplied)
            {
                return null; // Indicate already applied
            }

            // Show confirmation dialog via callback
            string message = "Are you sure you want to apply the 4GB patch? This will enable EverQuest to use up to 4GB of RAM on 64-bit systems and will create a backup of the original file.";
            if (!confirmAction(message))
            {
                return null; // Indicate user cancelled
            }

            // Apply the patch
            bool success = await Task.Run(() => PEModifier.Apply4GBPatch(eqExePath));
            if (success)
            {
                // Verify the patch was applied successfully
                bool verifyPatch = await Task.Run(() => PEModifier.Is4GBPatchApplied(eqExePath));
                return verifyPatch; // Return true if verified, false if verification failed
            }
            else
            {
                return false; // Indicate patch application failed
            }
        }

        // Modifies eqclient.ini to set UIScale=1.0
        // Returns true on success, false if file not found or error occurs.
        internal static bool FixUIScale(string eqClientIniPath)
        {
            try
            {
                if (!File.Exists(eqClientIniPath))
                {
                    return false; // Indicate file not found
                }

                var lines = File.ReadAllLines(eqClientIniPath).ToList(); // Use ToList for easier modification
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("UIScale=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "UIScale=1.0";
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Add it, ensuring it's under a relevant section if possible, or at the end
                    int defaultsIndex = lines.FindIndex(l => l.Trim().Equals("[Defaults]", StringComparison.OrdinalIgnoreCase));
                    if(defaultsIndex != -1)
                    {
                        // Insert after [Defaults] header or at the end of the section
                        int nextSectionIndex = lines.FindIndex(defaultsIndex + 1, l => l.Trim().StartsWith("[") && l.Trim().EndsWith("]"));
                        if(nextSectionIndex != -1)
                        {
                            lines.Insert(nextSectionIndex, "UIScale=1.0");
                        }
                        else
                        {
                            lines.Add("UIScale=1.0"); // Add to end of file if [Defaults] is last section
                        }
                    }
                    else 
                    { 
                       lines.Add("[Defaults]"); // Add section if not found
                       lines.Add("UIScale=1.0");
                    }
                }

                File.WriteAllLines(eqClientIniPath, lines);
                return true; // Indicate success
            }
            catch (Exception) // Catch specific exceptions like IOException, UnauthorizedAccessException if needed
            {
                // Log exception details here if a logging mechanism is passed in
                return false; // Indicate error
            }
        }

        // Modifies eqclient.ini to apply graphics optimization settings.
        // Returns true on success, false if file not found or error occurs.
        internal static async Task<bool> OptimizeGraphicsAsync(string eqClientIniPath)
        {
            try
            {
                if (!File.Exists(eqClientIniPath))
                {
                    return false; // Indicate file not found
                }

                await Task.Run(() => // Run file I/O on a background thread
                {
                    var lines = File.ReadAllLines(eqClientIniPath).ToList(); // Use ToList for easier modification
                    var optimizations = new Dictionary<string, string>
                    {
                        { "MaxFPS=", "MaxFPS=60" },
                        { "MaxBackgroundFPS=", "MaxBackgroundFPS=60" },
                        { "WaterReflections=", "WaterReflections=0" },
                        { "ParticleDensity=", "ParticleDensity=1000" },
                        { "SpellParticleDensity=", "SpellParticleDensity=1000" },
                        { "EnableVSync=", "EnableVSync=0" }
                    };

                    // Use a dictionary to track found status for easier checking
                    var foundStatus = optimizations.Keys.ToDictionary(key => key, key => false);

                    for (int i = 0; i < lines.Count; i++)
                    {
                        string trimmedLine = lines[i].Trim();
                        foreach (var opt in optimizations)
                        {
                            if (trimmedLine.StartsWith(opt.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                lines[i] = opt.Value;
                                foundStatus[opt.Key] = true;
                                // Don't break here, a line might potentially match multiple (though unlikely for INI)
                            }
                        }
                    }

                    // Add settings that were not found
                    var notFound = optimizations.Where(kvp => !foundStatus[kvp.Key]).ToList();
                    if (notFound.Any())
                    {
                         // Add under [Options] section or create it
                        int optionsIndex = lines.FindIndex(l => l.Trim().Equals("[Options]", StringComparison.OrdinalIgnoreCase));
                        if(optionsIndex == -1)
                        {
                            lines.Add(""); // Add blank line separator
                            lines.Add("[Options]");
                            optionsIndex = lines.Count - 1;
                        }

                        // Insert lines after the [Options] header or at the end of the section
                        int nextSectionIndex = lines.FindIndex(optionsIndex + 1, l => l.Trim().StartsWith("[") && l.Trim().EndsWith("]"));
                        int insertPosition = (nextSectionIndex != -1) ? nextSectionIndex : lines.Count;

                        foreach (var opt in notFound)
                        {
                            lines.Insert(insertPosition++, opt.Value);
                        }
                    }

                    File.WriteAllLines(eqClientIniPath, lines);
                });

                return true; // Indicate success
            }
            catch (Exception)
            {
                // Log exception details here if a logging mechanism is passed in
                return false; // Indicate error
            }
        }

        // Clears specified cache directories.
        // Returns true on success, false if an error occurs during deletion/creation.
        internal static async Task<bool> ClearCacheAsync(string eqPath)
        {
            try
            {
                string[] cacheDirs = { "dbstr", "maps" }; // Consider making this configurable if needed

                await Task.Run(() => // Run potentially long-running deletions on background thread
                {
                    foreach (string dir in cacheDirs)
                    {
                        string path = Path.Combine(eqPath, dir);
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true); // Recursive delete
                            Directory.CreateDirectory(path); // Recreate empty directory
                        }
                        else
                        {
                             Directory.CreateDirectory(path); // Create if it didn't exist
                        }
                    }
                });

                return true; // Indicate success
            }
            catch (Exception) // Catch specific exceptions like IOException, UnauthorizedAccessException if needed
            {
                // Log exception details here if a logging mechanism is passed in
                return false; // Indicate error
            }
        }

        // Deletes specified configuration files.
        // Returns true on success, false if an error occurs during deletion.
        internal static async Task<bool> ResetSettingsAsync(string eqPath)
        {
            try
            {
                 // Consider making this list configurable if needed
                string[] configFiles = { "eqclient.ini", "eqclient_local.ini" };

                await Task.Run(() => // Run file I/O on background thread
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
                 return true; // Indicate success
            }
            catch (Exception)
            {
                 // Log exception details here if a logging mechanism is passed in
                 return false; // Indicate error
            }
        }

        // Modifies eqclient.ini to apply memory optimization settings.
        // Returns true on success, false if file not found or error occurs.
        internal static async Task<bool> ApplyMemoryOptimizationsAsync(string eqClientIniPath)
        {
            try
            {
                if (!File.Exists(eqClientIniPath))
                {
                    return false; // Indicate file not found
                }

                await Task.Run(() => // Run file I/O on background thread
                {
                    // Read the ini file
                    var lines = File.ReadAllLines(eqClientIniPath);

                    // Use a dictionary to manage sections and keys easily
                    var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    string currentSection = "";

                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                        {
                            currentSection = trimmedLine; // Keep the brackets for writing back
                            if (!sections.ContainsKey(currentSection))
                            {
                                sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(trimmedLine) && trimmedLine.Contains("=") && !string.IsNullOrEmpty(currentSection))
                        {
                            // This is a key=value pair within a section
                            var parts = trimmedLine.Split(new[] { '=' }, 2);
                            if (parts.Length == 2)
                            {
                                sections[currentSection][parts[0].Trim()] = parts[1].Trim();
                            }
                        }
                        // Ignore lines that are not section headers or valid key=value pairs within a section
                    }

                    // ---- Apply Optimizations ----

                    // Ensure [Defaults] section exists
                    if (!sections.ContainsKey("[Defaults]"))
                    {
                        sections["[Defaults]"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    sections["[Defaults]"]["VertexShaders"] = "TRUE";
                    sections["[Defaults]"]["PostEffects"] = "0";

                    // Ensure [Options] section exists
                    if (!sections.ContainsKey("[Options]"))
                    {
                        sections["[Options]"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    sections["[Options]"]["MaxFPS"] = "60";
                    sections["[Options]"]["MaxBGFPS"] = "20"; // Renamed from MaxBackgroundFPS for consistency?
                    sections["[Options]"]["ClipPlane"] = "12";

                    // ---- Rebuild INI Content ----
                    var newContent = new List<string>();
                    // Preserve order if possible, but adding ensures they exist
                    var sectionOrder = lines.Where(l => l.Trim().StartsWith("[") && l.Trim().EndsWith("]"))
                                            .Select(l => l.Trim())
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .ToList();
                    
                    // Add any modified/new sections that weren't in the original order
                    foreach(var secKey in sections.Keys)
                    {
                        if(!sectionOrder.Contains(secKey, StringComparer.OrdinalIgnoreCase))
                        {
                            sectionOrder.Add(secKey);
                        }
                    }

                    foreach (var sectionKey in sectionOrder)
                    {
                         if (sections.TryGetValue(sectionKey, out var sectionEntries))
                         {
                            newContent.Add(sectionKey);
                            foreach (var entry in sectionEntries.OrderBy(kvp => kvp.Key)) // Optional: Order keys alphabetically
                            {
                                newContent.Add($"{entry.Key}={entry.Value}");
                            }
                            newContent.Add(""); // Add blank line between sections
                         }
                    }

                    // Write the updated content back to the file
                    File.WriteAllLines(eqClientIniPath, newContent);
                });
                return true; // Indicate success
            }
            catch (Exception)
            {
                // Log exception details here if a logging mechanism is passed in
                return false; // Indicate error
            }
        }

        // Methods for applying optimizations will be added here.
    }
} 