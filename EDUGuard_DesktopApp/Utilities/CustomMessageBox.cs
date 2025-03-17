using System.Media;
using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace EDUGuard_DesktopApp.Utilities
{
    public partial class CustomMessageBox : Window
    {
        // This variable stores the final left position for animation.
        private double finalLeft;
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
            //this.Left = workingArea.Right - this.Width - 10;   // 10px offset from the right edge
            //this.Top = workingArea.Bottom - this.Height - 10;   // 10px offset from the bottom edge

            finalLeft = workingArea.Right - this.Width - 10;

            // Set the top position (10px offset from the bottom)
            this.Top = workingArea.Bottom - this.Height - 10;

            // Set initial Left off-screen (to the right)
            this.Left = workingArea.Right;

            // Hook into the Loaded event to start the animation & play sound
            this.Loaded += CustomMessageBox_Loaded;
        }

        private void CustomMessageBox_Loaded(object sender, RoutedEventArgs e)
        {
            // Animate the window's Left property from its initial position to the finalLeft value.
            DoubleAnimation slideAnimation = new DoubleAnimation
            {
                From = this.Left,
                To = finalLeft,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(Window.LeftProperty, slideAnimation);

            // Play the notification sound.
            // Ensure "notification.wav" is in the Assets folder,
            // set as Content, and copied to the output directory.
            try
            {
                SoundPlayer player = new SoundPlayer("Assets/notification.wav");
                player.Play();
            }
            catch (Exception ex)
            {
                // Optionally handle or log the error if the sound fails to play.
                Console.WriteLine("Sound playback error: " + ex.Message);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
