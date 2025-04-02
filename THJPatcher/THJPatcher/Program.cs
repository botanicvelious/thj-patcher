using System;
using System.IO;
using System.Reflection;
using System.Windows;
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

                // Extract theme files if they don't exist
                string defaultsFile = Path.Combine(themesFolder, "materialdesigntheme.defaults.xaml");
                if (!File.Exists(defaultsFile))
                {
                    try
                    {
                        // Extract the embedded resource
                        using (Stream resourceStream = Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("THJPatcher.Resources.materialdesigntheme.defaults.xaml"))
                        {
                            if (resourceStream != null)
                            {
                                using (FileStream fileStream = new FileStream(defaultsFile, FileMode.Create))
                                {
                                    resourceStream.CopyTo(fileStream);
                                }
                            }
                            else
                            {
                                // If resource not found, create a minimal version
                                File.WriteAllText(defaultsFile, "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></ResourceDictionary>");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error extracting theme files: {ex.Message}\n\nPlease try running the application from a different folder.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Start the application
                var application = new System.Windows.Application();
                application.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
                application.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting the application: {ex.Message}\n\n{ex.StackTrace}",
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
            }
            catch
            {
                // Silently fail and let the default resolver handle it
            }

            return null;
        }
    }
}
