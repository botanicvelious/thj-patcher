using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace THJPatcher.Utilities
{
    using WpfPanel = System.Windows.Controls.Panel;
    using FormsPanel = System.Windows.Forms.Panel;

    /// <summary>
    /// Service responsible for handling UI navigation and panel switching
    /// </summary>
    public class NavigationService
    {
        private readonly Action<string> _logAction;
        private readonly Func<string, string, MessageBoxButton, MessageBoxImage, bool?> _showMessageAction;

        // UI panels that can be navigated to
        private readonly Card _logPanel;
        private readonly Card _optimizationsPanel;
        
        // Optional callback to initialize optimization panel when shown
        private readonly Action _initializeOptimizationsPanel;
        
        /// <summary>
        /// Initializes a new instance of the NavigationService
        /// </summary>
        /// <param name="logAction">Action to log messages</param>
        /// <param name="showMessageAction">Action to display message boxes</param>
        /// <param name="logPanel">The main log panel</param>
        /// <param name="optimizationsPanel">The optimizations panel</param>
        /// <param name="initializeOptimizationsPanel">Optional callback to initialize the optimizations panel</param>
        public NavigationService(
            Action<string> logAction,
            Func<string, string, MessageBoxButton, MessageBoxImage, bool?> showMessageAction,
            Card logPanel,
            Card optimizationsPanel,
            Action initializeOptimizationsPanel = null)
        {
            _logAction = logAction ?? (message => { /* No logging if null */ });
            _showMessageAction = showMessageAction ?? ((message, title, button, icon) => null);
            _logPanel = logPanel;
            _optimizationsPanel = optimizationsPanel;
            _initializeOptimizationsPanel = initializeOptimizationsPanel;
        }
        
        /// <summary>
        /// Shows the optimizations panel and hides other panels
        /// </summary>
        public void ShowOptimizationsPanel()
        {
            _logPanel.Visibility = Visibility.Collapsed;
            _optimizationsPanel.Visibility = Visibility.Visible;
            
            // Initialize the panel if a callback was provided
            _initializeOptimizationsPanel?.Invoke();
        }
        
        /// <summary>
        /// Shows the log panel and hides other panels
        /// </summary>
        public void ShowLogPanel()
        {
            _optimizationsPanel.Visibility = Visibility.Collapsed;
            _logPanel.Visibility = Visibility.Visible;
        }
        
        /// <summary>
        /// Opens the EverQuest directory in File Explorer
        /// </summary>
        public void OpenGameFolder()
        {
            try
            {
                string path = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _showMessageAction($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Opens a URL in the default browser
        /// </summary>
        /// <param name="url">The URL to open</param>
        public void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _showMessageAction($"Failed to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 