using System;

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
            var application = new System.Windows.Application();
            application.StartupUri = new Uri("pack://application:,,,/MainWindow.xaml", UriKind.Absolute);
            application.Run();
        }
    }
}
