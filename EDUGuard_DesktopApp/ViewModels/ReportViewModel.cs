using EDUGuard_DesktopApp.Models;
using EDUGuard_DesktopApp.Utilities;
using LiveCharts;
using LiveCharts.Wpf;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EDUGuard_DesktopApp.ViewModels
{
    public class ReportViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseHelper _dbHelper;
        private ChartValues<int> _postureValues;
        private List<string> _postureLabels;

        public event PropertyChangedEventHandler PropertyChanged;

        public ChartValues<int> PostureValues
        {
            get => _postureValues;
            set
            {
                _postureValues = value;
                OnPropertyChanged();
            }
        }

        public List<string> PostureLabels
        {
            get => _postureLabels;
            set
            {
                _postureLabels = value;
                OnPropertyChanged();
            }
        }

        public ReportViewModel()
        {
            _dbHelper = new DatabaseHelper();
            PostureValues = new ChartValues<int> { 0, 0 }; // Initial values for Good & Bad Posture
            PostureLabels = new List<string> { "Good Posture", "Bad Posture" };

            FetchPostureData();
        }

        public async void FetchPostureData()
        {
            try
            {
                var currentUserId = SessionManager.CurrentUser.Id; // Get logged-in user ID

                // Find all completed posture monitoring sessions for the user
                var filter = Builders<ProgressReports>.Filter.And(
                    Builders<ProgressReports>.Filter.Eq(p => p.UserId, currentUserId),
                    Builders<ProgressReports>.Filter.Ne(p => p.PostureData.EndTime, DateTime.MinValue) // Ensure session ended
                );

                var reports = await _dbHelper.ProgressReports.Find(filter)
                                   .SortByDescending(p => p.PostureData.StartTime)
                                   .ToListAsync(); // Get all relevant reports

                int totalGood = 0;
                int totalBad = 0;

                foreach (var report in reports)
                {
                    if (report.PostureData.Outputs != null && report.PostureData.Outputs.Count > 0)
                    {
                        foreach (var array in report.PostureData.Outputs)
                        {
                            int badCount = array.Count(p => p == "Bad Posture");
                            int goodCount = array.Count - badCount; // Remaining are Good Postures

                            totalBad += badCount;
                            totalGood += goodCount;
                        }
                    }
                }

                // Update chart values dynamically
                PostureValues = new ChartValues<int> { totalGood, totalBad };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching posture data: {ex.Message}");
            }
        }


        // Notify UI about data updates
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
