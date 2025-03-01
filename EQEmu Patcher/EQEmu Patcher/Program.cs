using System;

namespace EQEmu_Patcher
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
            application.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
            application.Run();
        }
    }
}
