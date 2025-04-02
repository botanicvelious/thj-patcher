using System;
using System.IO;
using System.Windows;
using System.Reflection;

namespace THJPatcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Set up exception handling
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            
            // Set up assembly resolution
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            
            // Initialize resources
            InitializeComponent();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            string message = $"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}";
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Mark as handled to prevent application crash
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unhandled exception: {ex.Message}\n\n{ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
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
