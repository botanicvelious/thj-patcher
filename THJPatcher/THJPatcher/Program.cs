using System;
using System.IO;
using System.Reflection;
using System.Windows;

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
    }
}
