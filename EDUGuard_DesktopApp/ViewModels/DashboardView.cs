using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EDUGuard_DesktopApp.Models;
using EDUGuard_DesktopApp.Utilities;
using MongoDB.Driver;
using System.Linq;
using System.Timers;
using MongoDB.Bson;
using EDUGuard_DesktopApp.ViewModels;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace EDUGuard_DesktopApp.Views
{
    public partial class DashboardView : Window
    {
        private readonly ReportViewModel _reportViewModel;
        private readonly DatabaseHelper _dbHelper = new DatabaseHelper();
        private readonly Dictionary<string, Process> _modelProcesses = new Dictionary<string, Process>();
        private Process _webcamServerProcess;
        private bool _isModel1Running = false, _isModel2Running = false, _isModel3Running = false, _isModel4Running = false;
        private readonly string _currentUserEmail;
        //private Timer _alertTimer;
        private System.Timers.Timer _monitorTimer;
        private int _processedArraysCount = 0; // Keep track of already processed arrays
        private readonly Dictionary<string, string> _modelProgressReports = new Dictionary<string, string>();

        public DashboardView()
        {
            if (!SessionManager.IsLoggedIn)
            {
                Logger.LogError("Access Denied: User not logged in.");
                Close();
                return;
            }
        
            
            InitializeComponent();
            StartWebcamServer();
            LoadUserProfile();
            
            // Set ViewModel for Data Binding
            _reportViewModel = new ReportViewModel();
            DataContext = _reportViewModel;

            // Get the current user's email
            _currentUserEmail = SessionManager.CurrentUser?.Email;
            if (string.IsNullOrEmpty(_currentUserEmail))
            {
                Logger.LogError("Failed to retrieve the user's email.");
                Close();
            }


            }

        private void LoadUserProfile()
        {
            var user = SessionManager.CurrentUser;
            if (user != null)
            {
                FirstNameTextBox.Text = user.FirstName;
                LastNameTextBox.Text = user.LastName;
                EmailTextBox.Text = user.Email;
                AgeTextBox.Text = user.Age.ToString();
                ContactNumberTextBox.Text = user.ContactNumber;
            }
        }

        private void StartWebcamServer()
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\webcam_server.py",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                _webcamServerProcess = Process.Start(processInfo);
                if (_webcamServerProcess == null)
                {
                    Logger.LogError("Failed to start the webcam server.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error starting the webcam server: {ex.Message}");
            }

        }

        //private void ClearPostureDataInDatabase()
        //{
        //    try
        //    {
        //        var filter = Builders<User>.Filter.Eq(u => u.Email, _currentUserEmail);
        //        var update = Builders<User>.Update.Set(u => u.PostureData, new List<List<string>>());

        //        _dbHelper.Users.UpdateOne(filter, update);
        //    }
        //    catch (Exception ex)
        //    {
        //        LogError($"Error clearing posture data: {ex.Message}");
        //    }
        //}

        private void ToggleModel(string modelName, ref bool isRunning, Button modelButton, string scriptPath)
        {
            if (!isRunning)
            {
                StartModel(modelName, ref isRunning, modelButton, scriptPath);
            }
            else
            {
                StopModel(modelName, ref isRunning, modelButton);
            }
        }



        private void StartModel(string modelName, ref bool isRunning, Button modelButton, string scriptPath)
        {
            var startTime = DateTime.UtcNow;
            var progressReportId = _dbHelper.CreateProgressReport(_currentUserEmail, modelName, startTime);

            if (string.IsNullOrEmpty(progressReportId))
            {
                Logger.LogError($"Failed to create progress report for {modelName}.");
                return;
            }

            // Store the progress report ID for later retrieval
            _modelProgressReports[modelName] = progressReportId;

            Logger.LogError($"modelReport : {_modelProgressReports}");

            var processInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"{scriptPath} {_currentUserEmail} {progressReportId}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                var process = Process.Start(processInfo);
                if (process == null)
                {
                    Logger.LogError($"Failed to start {modelName}.");
                    return;
                }

                _modelProcesses[modelName] = process;
                UpdateButtonUI(modelButton, true, $"Start {modelName}", $"Stop {modelName}");

                Task.Run(() =>
                {
                    ReadStreamAsync(process.StandardOutput, line => Console.WriteLine($"[{modelName} Output]: {line}"));
                    ReadStreamAsync(process.StandardError, line => Logger.LogError($"[{modelName} Error]: {line}"));
                });

                isRunning = true;

                
                // Start posture monitoring if it's the posture model
                StartMonitoring(progressReportId);
                
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error starting {modelName}: {ex.Message}");
            }
        }


        private void StopModel(string modelName, ref bool isRunning, Button modelButton)
        {
            try
            {
                if (_modelProcesses.ContainsKey(modelName))
                {
                    var process = _modelProcesses[modelName];
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.Dispose();
                    }
                    _modelProcesses.Remove(modelName);

                    // Retrieve the correct progress report ID
                    if (_modelProgressReports.TryGetValue(modelName, out string progressReportId))
                    {
                        SaveEndTimeToDatabase(progressReportId, modelName);
                        _modelProgressReports.Remove(modelName); // Remove after use
                    }
                    else
                    {
                        Logger.LogError($"Could not find progress report ID for {modelName}.");
                    }

                    //Stop posture monitoring when posture model stops
                    if (modelName.ToLower() == "posture")
                    {
                        _monitorTimer?.Stop();
                        _monitorTimer?.Dispose();
                    }

                    UpdateButtonUI(modelButton, false, $"Start {modelName}", $"Stop {modelName}");
                    isRunning = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error stopping {modelName}: {ex.Message}");
            }
        }


        private void UpdateButtonUI(Button button, bool isRunning, string startText, string stopText)
        {
            button.Content = isRunning ? stopText : startText;
            button.Background = isRunning ? Brushes.LightCoral : Brushes.LightGreen;
            button.Foreground = isRunning ? Brushes.White : Brushes.Black;
        }

        /// <summary>
        /// Deletes the current user's account.
        /// </summary>
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete your account? This action is irreversible.",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var filter = Builders<User>.Filter.Eq(u => u.Email, SessionManager.CurrentUser.Email);
                _dbHelper.Users.DeleteOne(filter);

                MessageBox.Show("Account deleted successfully.", "Account Deleted", MessageBoxButton.OK, MessageBoxImage.Information);

                SessionManager.EndSession();
                Close();
                var loginView = new MainWindow();
                loginView.Show();
            }
        }



        /// <summary>
        /// Opens Settings.
        /// </summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings functionality is under development.", "Settings",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Updates the current user's profile.
        /// </summary>
        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var updatedUser = SessionManager.CurrentUser;
            updatedUser.FirstName = FirstNameTextBox.Text;
            updatedUser.LastName = LastNameTextBox.Text;
            updatedUser.ContactNumber = ContactNumberTextBox.Text;

            if (int.TryParse(AgeTextBox.Text, out int updatedAge))
            {
                updatedUser.Age = updatedAge;
            }
            else
            {
                MessageBox.Show("Invalid Age value. Please enter a valid number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var filter = Builders<User>.Filter.Eq(u => u.Email, updatedUser.Email);
            var update = Builders<User>.Update
                .Set(u => u.FirstName, updatedUser.FirstName)
                .Set(u => u.LastName, updatedUser.LastName)
                .Set(u => u.ContactNumber, updatedUser.ContactNumber)
                .Set(u => u.Age, updatedUser.Age);

            _dbHelper.Users.UpdateOne(filter, update);

            MessageBox.Show("Profile updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private Task ReadStreamAsync(StreamReader reader, Action<string> onLineRead)
        {
            return Task.Run(async () =>
            {
                try
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        onLineRead(line);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Stream read error: {ex.Message}");
                }
            });
        }

        private void StartMonitoring(string progressReportId)
        {
            //_processedArraysCount = 0; // Reset count when model starts
            //_monitorTimer = new System.Timers.Timer(120000); // Runs every 2 minutes (120000 ms) 
            //_monitorTimer.Elapsed += async (sender, e) => await CheckPostureAlerts(progressReportId);
            //_monitorTimer.Elapsed += async (sender, e) => await CheckBlinkAlerts(progressReportId);
            //_monitorTimer.Elapsed += async (sender, e) => await CheckStressAlerts(progressReportId);
            //_monitorTimer.AutoReset = true;
            //_monitorTimer.Start();

            _processedArraysCount = 0; // Reset count when model starts
            _monitorTimer = new System.Timers.Timer(128000); 
            _monitorTimer.Elapsed += async (sender, e) =>
            {
                _processedArraysCount = 0; // Reset so alerts do not get stuck
                await CheckPostureAlerts(progressReportId);
                await CheckBlinkAlerts(progressReportId);
                await CheckStressAlerts(progressReportId);
            };
            _monitorTimer.AutoReset = true;
            _monitorTimer.Start();

        }

        //Check posture
        //private async Task CheckPostureAlerts(string progressReportId)
        //{

        //    try
        //    {
        //        // Fetch latest progress report
        //        var filter = Builders<ProgressReports>.Filter.Eq(r => r.Id, progressReportId);
        //        var report = await _dbHelper.ProgressReports.Find(filter).FirstOrDefaultAsync();

        //        if (report == null || report.PostureData?.Outputs == null || report.PostureData.Outputs.Count == 0)
        //        {
        //            Logger.LogError("No posture data found for monitoring.");
        //            return;
        //        }

        //        // Process only new arrays (ignore already processed ones)
        //        for (int i = _processedArraysCount; i < report.PostureData.Outputs.Count; i++)
        //        {
        //            var batch = report.PostureData.Outputs[i];
        //            Logger.LogError($"PostureData.Outputs {batch}");


        //            if (batch.Count == 0)
        //            {
        //                Logger.LogError($"Batch empty {batch}");
        //                continue;
        //            } // Skip empty batches

        //            // Calculate the percentage of "Bad Posture" occurrences in the batch
        //            int badPostureCount = batch.Count(p => p == "Bad Posture");
        //            double badPosturePercentage = (double)badPostureCount / batch.Count * 100;

        //            // Trigger notification only if "Bad Posture" exceeds 60%
        //            if (badPosturePercentage > 60)
        //            {
        //                Logger.LogError($"badposture Notify: {DateTime.Now}");
        //                ShowNotification($"Alert: Your posture quality is poor! Currect it immediately ");
        //            }

        //            // Mark this batch as processed
        //            _processedArraysCount++;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.LogError($"Error checking posture alerts: {ex.Message}");
        //    }
        //}

        //new pooooooooooooooooooooooo
        private async Task CheckPostureAlerts(string progressReportId)
        {
            try
            {
                // Fetch latest progress report
                var filter = Builders<ProgressReports>.Filter.Eq(r => r.Id, progressReportId);
                var report = await _dbHelper.ProgressReports.Find(filter).FirstOrDefaultAsync();

                if (report == null || report.PostureData?.Outputs == null || report.PostureData.Outputs.Count == 0)
                {
                    Logger.LogError("No posture data found for monitoring.");
                    return;
                }

                // Get the last (most recent) batch only
                var latestIndex = report.PostureData.Outputs.Count - 1;

                if (latestIndex < _processedArraysCount)
                {
                    Logger.LogError("No new posture data to process.");
                    return;
                }

                var latestBatch = report.PostureData.Outputs[latestIndex]; // Get the latest batch

                Logger.LogError($"Latest PostureData.Outputs: {latestBatch}");

                if (latestBatch.Count == 0)
                {
                    Logger.LogError("Latest batch is empty, skipping.");
                    return;
                }

                // Calculate the percentage of "Bad Posture" occurrences in the batch
                int badPostureCount = latestBatch.Count(p => p == "Bad Posture");
                double badPosturePercentage = (double)badPostureCount / latestBatch.Count * 100;

                // Trigger notification only if "Bad Posture" exceeds 60%
                if (badPosturePercentage > 60)
                {
                    Logger.LogError($"badposture Notify: {DateTime.Now},{badPosturePercentage}");
                    ShowNotification($"Alert: Your posture quality is poor! Correct it immediately.");
                }

                // Update processed count to avoid rechecking the same batch
                _processedArraysCount = latestIndex + 1;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking posture alerts: {ex.Message}");
            }
        }


        //Check blink count
        private async Task CheckBlinkAlerts(string progressReportId)
        {
            try
            {
                // Fetch latest progress report
                var filter = Builders<ProgressReports>.Filter.Eq(r => r.Id, progressReportId);
                var report = await _dbHelper.ProgressReports.Find(filter).FirstOrDefaultAsync();

                if (report == null || report.CVSData?.Outputs == null || report.CVSData.Outputs.Count == 0)
                {
                    Logger.LogError("No blink data found for monitoring.");
                    return;
                }

                // Process only new arrays (ignore already processed ones)
                for (int i = _processedArraysCount; i < report.CVSData.Outputs.Count; i++)
                {
                    var batch = report.CVSData.Outputs[i];
                    Logger.LogError($"BlinkData.Outputs {batch}");

                    if (batch.Count == 0)
                    {
                        Logger.LogError($"Batch empty {batch}");
                        continue;
                    } // Skip empty batches

                    // Extract latest blink count from the batch
                    int blinkCount = ExtractBlinkCount(batch);
                    Logger.LogError($"Processed Blink Count: {blinkCount}");

                    // Alert for eye strain (15-17 blinks)
                    if (!(blinkCount >= 15 && blinkCount <= 17))
                    {
                        // Alert for vision strain or dry eyes (above 17 blinks)
                        if (blinkCount > 17)
                        {
                            Logger.LogError($"High blink rate detected: {DateTime.Now}");
                            ShowNotification("Warning: High blink rate detected. Look at a long-distance object!");
                        }
                        else {
                            Logger.LogError($"Eye strain detected: {DateTime.Now}");
                            ShowNotification("Alert: You have eye strain. Take a break!");
                        }
                        
                    }
                    // Mark this batch as processed
                    _processedArraysCount++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking blink alerts: {ex.Message}");
            }
        }

        //Check Stress
        private async Task CheckStressAlerts(string progressReportId)
        {
            try
            {
                // Fetch latest progress report
                var filter = Builders<ProgressReports>.Filter.Eq(r => r.Id, progressReportId);
                var report = await _dbHelper.ProgressReports.Find(filter).FirstOrDefaultAsync();

                if (report == null || report.StressData?.Outputs == null || report.StressData.Outputs.Count == 0)
                {
                    Logger.LogError("No stress data found for monitoring.");
                    return;
                }

                // Process only new batches (ignore already processed ones)
                for (int i = _processedArraysCount; i < report.StressData.Outputs.Count; i++)
                {
                    var batch = report.StressData.Outputs[i];
                    Logger.LogError($"StressData.Outputs {batch}");

                    if (batch.Count == 0)
                    {
                        Logger.LogError($"Batch empty {batch}");
                        continue;
                    } // Skip empty batches

                    // Count occurrences of each emotion in the batch
                    int angerCount = batch.Count(e => e == "angry");
                    int fearCount = batch.Count(e => e == "fear");
                    int disgustCount = batch.Count(e => e == "disgust");
                    int sadnessCount = batch.Count(e => e == "sad");
                    int neutralCount = batch.Count(e => e == "neutral");
                    int surpriseCount = batch.Count(e => e == "surprise");
                    int happinessCount = batch.Count(e => e == "happy");

                    int totalEmotions = batch.Count;

                    // Calculate stress levels
                    int negativeEmotions = angerCount + fearCount + disgustCount + sadnessCount;
                    double negativePercentage = (double)negativeEmotions / totalEmotions * 100;

                    Logger.LogError($"Stress Calculation - Negative: {negativePercentage:F1}%, Happy: {happinessCount}, Neutral: {neutralCount}");

                    string stressLevel = "Unknown";

                    if (negativePercentage > 60)
                    {
                        stressLevel = "High Stress";
                        ShowNotification("High Stress Detected! Try relaxation techniques.");
                    }
                    else if (neutralCount >= happinessCount && neutralCount >= surpriseCount)
                    {
                        stressLevel = "Medium Stress";
                        ShowNotification("Medium Stress Level. Consider taking a short break.");
                    }
                    else if (happinessCount > neutralCount)
                    {
                        stressLevel = "Low Stress";
                    }

                    Logger.LogError($"Determined Stress Level: {stressLevel}");

                    // Mark this batch as processed
                    _processedArraysCount++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking stress alerts: {ex.Message}");
            }
        }


        private int ExtractBlinkCount(List<string> batchData)
        {
            try
            {
                foreach (var entry in batchData)
                {
                    Logger.LogError($"Extracting Blink Data: {entry}"); // Debug log

                    // If the data is in JSON format, attempt to parse it
                    try
                    {
                        var parsedEntry = Newtonsoft.Json.Linq.JObject.Parse(entry);

                        if (parsedEntry.ContainsKey("blink_count"))
                        {
                            return parsedEntry["blink_count"].Value<int>();
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        Logger.LogError($"JSON Parse Error: {jsonEx.Message}");
                    }

                    // If data is not in JSON format, attempt string parsing
                    if (entry.Contains("blink_count"))
                    {
                        var parts = entry.Split(':');
                        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int blinkValue))
                        {
                            return blinkValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error extracting blink count: {ex.Message}");
            }

            return 0; // Default return statement to satisfy the compiler
        }

        //warning alert 
        private void ShowNotification(string message)
        {
            // Example: Windows Toast Notification (you can modify based on your UI)

            //Thread notificationThread = new Thread(() =>
            //{
            //    // Create and show the custom message box on the new thread
            //    CustomMessageBox msgBox = new CustomMessageBox(message);
            //    msgBox.ShowDialog();
            //});


            //// Set the thread to STA before starting it
            //notificationThread.SetApartmentState(ApartmentState.STA);
            //notificationThread.Start();

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Create and show the custom message box
                CustomMessageBox msgBox = new CustomMessageBox(message);
                msgBox.ShowDialog();
            });
        }
        

        private void Model1Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleModel("posture", ref _isModel1Running, (Button)sender, "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\posture_detection1.py");
        }

        private void Model2Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleModel("stress", ref _isModel2Running, (Button)sender, "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\stress_detection.py");
        }

        private void Model3Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleModel("cvs", ref _isModel3Running, (Button)sender, "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\cvs_detection.py");
        }

        private void Model4Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleModel("hydration", ref _isModel4Running, (Button)sender, "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\EDUGuard_DesktopApp\\PyFiles\\model4.py");
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to log out?", "Logout Confirmation",
                             MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SessionManager.EndSession();
                StopAllProcesses();
                var mainWindow = new MainWindow();
                mainWindow.Show();
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopAllProcesses();
        }

        private void StopAllProcesses()
        {
            foreach (var modelName in _modelProcesses.Keys)
            {
                try
                {
                    var process = _modelProcesses[modelName];
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error stopping {modelName}: {ex.Message}");
                }
            }

            if (_webcamServerProcess != null && !_webcamServerProcess.HasExited)
            {
                try
                {
                    _webcamServerProcess.Kill();
                    _webcamServerProcess.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error stopping Webcam Server: {ex.Message}");
                }
            }
        }

        private void SaveEndTimeToDatabase(string progressReportId, string modelName)
        {
            try
            {
                if (string.IsNullOrEmpty(progressReportId) || !ObjectId.TryParse(progressReportId, out _))
                {
                    Logger.LogError($"Invalid progress report ID: {progressReportId}");
                    return;
                }

                var filter = Builders<ProgressReports>.Filter.Eq(r => r.Id, progressReportId);
                UpdateDefinition<ProgressReports> update = null;

                if (modelName.ToLower().Contains("posture"))
                {
                    update = Builders<ProgressReports>.Update.Set(r => r.PostureData.EndTime, DateTime.UtcNow);
                }
                else if (modelName.ToLower().Contains("stress"))
                {
                    update = Builders<ProgressReports>.Update.Set(r => r.StressData.EndTime, DateTime.UtcNow);
                }
                else if (modelName.ToLower().Contains("cvs"))
                {
                    update = Builders<ProgressReports>.Update.Set(r => r.CVSData.EndTime, DateTime.UtcNow);
                }

                if (update != null)
                {
                    var result = _dbHelper.ProgressReports.UpdateOne(filter, update);
                    if (result.ModifiedCount == 0)
                    {
                        Logger.LogError($"Failed to update end time for {modelName} (ID: {progressReportId}).");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error saving end time for {modelName} (ID: {progressReportId}): {ex.Message}");
            }
        }

    }

 
}
