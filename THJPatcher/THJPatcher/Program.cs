using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Collections.Generic;
using MessageBox = System.Windows.MessageBox;

namespace THJPatcher
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                // Set up assembly resolution to help find dependencies
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

                // Set up exception handling for unhandled exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // Create themes directory if it doesn't exist
                string exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string themesFolder = Path.Combine(exeFolder, "themes");

                
                

                if (!Directory.Exists(themesFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(themesFolder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error creating themes folder: {ex.Message}\n\nPlease try running the application from a different folder.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // List of theme files to extract
                Dictionary<string, string> themeFiles = new Dictionary<string, string>
                {
                    { "materialdesigntheme.defaults.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" },
                    { "materialdesigntheme.light.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" },
                    { "materialdesigntheme.dark.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" },
                    { "materialdesigntheme.colors.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" },
                    { "materialdesigntheme.button.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" },
                    { "materialdesigntheme.textblock.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>" }
                };

                // Extract all theme files
                foreach (var themeFile in themeFiles)
                {
                    string filePath = Path.Combine(themesFolder, themeFile.Key);
                    if (!File.Exists(filePath))
                    {
                        try
                        {
                            // Try to extract the embedded resource
                            string resourceName = "THJPatcher.Resources." + themeFile.Key.ToLower();
                            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                            {
                                if (resourceStream != null)
                                {
                                    using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                                    {
                                        resourceStream.CopyTo(fileStream);
                                    }
                                }
                                else
                                {
                                    // If resource not found, create a minimal version
                                    File.WriteAllText(filePath, themeFile.Value);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log the error but continue - we'll create a minimal version
                            Console.WriteLine($"Error extracting theme file {themeFile.Key}: {ex.Message}");
                            try
                            {
                                // Create a minimal version as fallback
                                File.WriteAllText(filePath, themeFile.Value);
                            }
                            catch
                            {
                                // If we can't even create the minimal version, just continue
                                // The application will try to use the embedded resources
                            }
                        }
                    }
                }

                // Create a components directory for MaterialDesign
                string componentsFolder = Path.Combine(themesFolder, "components");
                if (!Directory.Exists(componentsFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(componentsFolder);
                    }
                    catch
                    {
                        // Silently fail - we'll try to use embedded resources
                    }
                }

                // Modify the application configuration to handle resource loading
                AppDomain.CurrentDomain.SetData("PRIVATE_BINPATH", exeFolder);

                // Start the application with error handling
                try
                {
                    var application = new System.Windows.Application();

                    // Add a handler for unhandled exceptions
                    application.DispatcherUnhandledException += (s, e) =>
                    {
                        MessageBox.Show($"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Handled = true;
                    };

                    // Set the startup URI
                    application.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);

                    // Run the application
                    application.Run();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error starting the application: {ex.Message}\n\nPlease try running the application from a different folder or as administrator.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting the application: {ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                MessageBox.Show($"Unhandled exception: {ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // This helps resolve dependencies that might be in the same folder
                string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyName = new AssemblyName(args.Name).Name;
                string assemblyPath = Path.Combine(folderPath, assemblyName + ".dll");

                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }

                // Also check in a lib subfolder
                string libPath = Path.Combine(folderPath, "lib", assemblyName + ".dll");
                if (File.Exists(libPath))
                {
                    return Assembly.LoadFrom(libPath);
                }

                // Check for MaterialDesignThemes.Wpf specifically
                if (assemblyName == "MaterialDesignThemes.Wpf")
                {
                    // Try to find it in the application folder
                    string[] files = Directory.GetFiles(folderPath, "MaterialDesignThemes.Wpf.dll", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        return Assembly.LoadFrom(files[0]);
                    }
                }
            }
            catch
            {
                // Silently fail and let the default resolver handle it
            }

            return null;
        }
    }
}
