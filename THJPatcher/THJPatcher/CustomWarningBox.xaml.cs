using System.Windows;

namespace THJPatcher
{
    public partial class CustomWarningBox : Window
    {
        public CustomWarningBox(string message, string title = "Warning")
        {
            InitializeComponent();
            MessageText.Text = message;
            Title = title;
            Owner = System.Windows.Application.Current.MainWindow;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public static void Show(string message, string title = "Warning")
        {
            var dialog = new CustomWarningBox(message, title);
            dialog.ShowDialog();
        }
    }
}
