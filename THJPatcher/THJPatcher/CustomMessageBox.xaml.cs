using System.Windows;

namespace THJPatcher
{
    public partial class CustomMessageBox : Window
    {
        public bool Result { get; private set; }

        public CustomMessageBox(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
            Owner = System.Windows.Application.Current.MainWindow;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        public static bool Show(string message)
        {
            var dialog = new CustomMessageBox(message);
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
} 