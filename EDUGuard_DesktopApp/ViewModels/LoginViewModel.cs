using EDUGuard_DesktopApp.Utilities;
using MongoDB.Driver;
using System.Windows;
using System.Windows.Controls;

namespace EDUGuard_DesktopApp.Views
{
    public partial class LoginView : UserControl
    {
        public event RoutedEventHandler NavigateToSignUpEvent;

        public LoginView()
        {
            InitializeComponent();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            var dbHelper = new DatabaseHelper();
            var user = dbHelper.Users.Find(u => u.Email == username).FirstOrDefault();

            if (user != null && PasswordHelper.VerifyPassword(user.Password, password))
            {
                MessageBox.Show("Login successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                //CustomMessageBox msgBox = new CustomMessageBox("Alert: Your posture quality is poor! Correct it immediately.");
                //msgBox.ShowDialog();

                // Navigate to dashboard or application home
                // Start session
                SessionManager.StartSession(user);

                // Open DashboardView
                DashboardView dashboard = new DashboardView();
                dashboard.Show();

                // Close the login window
                Window.GetWindow(this)?.Close();
            }
            else
            {
                MessageBox.Show("Invalid credentials!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavigateToSignUp(object sender, RoutedEventArgs e)
        {
            NavigateToSignUpEvent?.Invoke(this, new RoutedEventArgs());
        }
    }
}
