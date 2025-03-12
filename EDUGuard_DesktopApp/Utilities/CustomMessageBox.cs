using System.Windows;

namespace EDUGuard_DesktopApp.Utilities
{
    public partial class CustomMessageBox : Window
    {
        public CustomMessageBox()
        {
            InitializeComponent();
        }

        // Constructor that takes a string parameter
        public CustomMessageBox(string message)
        {
            InitializeComponent();
            MessageTextBlock.Text = message;

            // Position the window at the bottom-right corner
            var workingArea = SystemParameters.WorkArea;
            this.Left = workingArea.Right - this.Width - 10;   // 10px offset from the right edge
            this.Top = workingArea.Bottom - this.Height - 10;   // 10px offset from the bottom edge
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
