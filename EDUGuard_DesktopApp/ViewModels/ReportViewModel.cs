using EDUGuard_DesktopApp.Models;
using EDUGuard_DesktopApp.Utilities;
using LiveCharts;
using LiveCharts.Wpf;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;

namespace EDUGuard_DesktopApp.ViewModels
{
    public class ReportViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseHelper _dbHelper;

        //posture varibles
        private SeriesCollection _postureSeries;
        private ObservableCollection<string> _timeLabels;
        public SeriesCollection PosturePieSeries { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        //PieChart Data Collection
        private ChartValues<double> _postureValues = new ChartValues<double> { 0, 0 };
        public ChartValues<double> PostureValues
        {
            get => _postureValues;
            set
            {
                _postureValues = value;
                OnPropertyChanged();
            }
        }

        // Bindable property for X-axis labels (Dates)
        public ObservableCollection<string> TimeLabels
        {
            get => _timeLabels;
            set
            {
                _timeLabels = value;
                OnPropertyChanged();
            }
        }

        //Bindable property for posture data series (Chart Bars)
        public SeriesCollection PostureSeries
        {
            get => _postureSeries;
            set
            {
                _postureSeries = value;
                OnPropertyChanged();
            }
        }


        // Stress Varibles
        // Pie Chart for Stress Data (Emotions)
        public SeriesCollection StressPieSeries { get; set; }

        // Bar Chart for Stress Data (Daily Emotions)
        public SeriesCollection StressBarSeries { get; set; }
        public ObservableCollection<string> StressLabels { get; set; }

        // Emotion Categories
        private readonly string[] _emotions = { "happy", "surprise", "sad", "neutral", "fear", "disgust", "angry" };


        //Constructor: Initializes collections & Fetches data
        public ReportViewModel()
        {
            _dbHelper = new DatabaseHelper();

            TimeLabels = new ObservableCollection<string>();

            PostureSeries = new SeriesCollection
            {
                new ColumnSeries { Values = new ChartValues<double>(), Title = "Good Posture" },
                new ColumnSeries { Values = new ChartValues<double>(), Title = "Bad Posture" }
            };

            //Initialize PieChart data series
            PosturePieSeries = new SeriesCollection
            {
                new PieSeries
                {
                    Title = "Good Posture",
                    Values = new ChartValues<double> { 0 },
                    Fill = Brushes.ForestGreen,
                    DataLabels = true
                },
                new PieSeries
                {
                    Title = "Bad Posture",
                    Values = new ChartValues<double> { 0 },
                    Fill = Brushes.OrangeRed,
                    DataLabels = true
                }
            };

            // Initialize Series Collections
            StressPieSeries = new SeriesCollection();
            StressBarSeries = new SeriesCollection();
            StressLabels = new ObservableCollection<string>();

            foreach (var emotion in _emotions)
            {
                StressPieSeries.Add(new PieSeries
                {
                    Title = emotion,
                    Values = new ChartValues<double> { 0 },
                    DataLabels = true
                });

                StressBarSeries.Add(new ColumnSeries
                {
                    Title = emotion,
                    Values = new ChartValues<double>()
                });
            }

            FetchPostureData();
            FetchPostureDataForPieChart();
            FetchStressData();
        }



        //Fetch and process posture data from MongoDB
        public async void FetchPostureData()
        {
            try
            {
                // 🟢 Get the logged-in user ID
                var currentUserId = SessionManager.CurrentUser.Id;
                Console.WriteLine($"User Id data : {currentUserId}");

                // 🟢 Create MongoDB filter for user reports
                var filter = Builders<ProgressReports>.Filter.Eq(p => p.UserId, currentUserId);
                Console.WriteLine($"Filter data : {filter}");

                // 🟢 Retrieve all progress reports
                var progressReports = await _dbHelper.ProgressReports
                    .Find(filter)
                    .SortByDescending(p => p.PostureData.StartTime)
                    .ToListAsync();

                if (progressReports == null || progressReports.Count == 0)
                {
                    Console.WriteLine("No progress reports found.");
                    return;
                }

                Console.WriteLine($"progressReports data count: {progressReports.Count}");

                // 🟢 Dictionary to store total Good/Bad posture duration **by day**
                var postureDataByDay = new Dictionary<DateTime, (double goodPosture, double badPosture)>();

                foreach (var report in progressReports)
                {
                    Console.WriteLine($"Processing report ID: {report.Id}");

                    if (report.PostureData?.Outputs == null || report.PostureData.Outputs.Count == 0)
                    {
                        Console.WriteLine($"PostureData.Outputs is EMPTY in report ID: {report.Id}");
                        continue;
                    }

                    // 🟢 Get the date of the posture session
                    var reportDate = report.PostureData.StartTime.Date;

                    double totalGood = 0;
                    double totalBad = 0;

                    foreach (var batch in report.PostureData.Outputs)
                    {
                        if (batch == null || batch.Count == 0) continue; // Skip empty batches

                        int badCount = batch.Count(p => p == "Bad Posture");
                        int goodCount = batch.Count - badCount;

                        // Each batch represents 2 minutes
                        double batchDuration = 2.0;

                        totalGood += (goodCount / (double)batch.Count) * batchDuration;
                        totalBad += (badCount / (double)batch.Count) * batchDuration;
                    }

                    // 🟢 Store posture data by date
                    if (!postureDataByDay.ContainsKey(reportDate))
                    {
                        postureDataByDay[reportDate] = (totalGood, totalBad);
                    }
                    else
                    {
                        var existing = postureDataByDay[reportDate];
                        postureDataByDay[reportDate] = (existing.goodPosture + totalGood, existing.badPosture + totalBad);
                    }

                    Console.WriteLine($"Processed data for {reportDate}: Good={totalGood}, Bad={totalBad}");
                }

                Console.WriteLine($"postureDataByDay data count : {postureDataByDay.Count}");

                if (postureDataByDay.Count == 0)
                {
                    Console.WriteLine("No valid posture data found after processing.");
                    return;
                }

                // 🟢 Update UI Chart (Ensure **Thread Safety**)
                App.Current.Dispatcher.Invoke(() =>
                {
                    TimeLabels.Clear();
                    PostureSeries[0].Values.Clear();
                    PostureSeries[1].Values.Clear();

                    foreach (var entry in postureDataByDay.OrderBy(e => e.Key))
                    {
                        TimeLabels.Add(entry.Key.ToString("MM/dd/yyyy"));
                        PostureSeries[0].Values.Add(entry.Value.goodPosture);
                        PostureSeries[1].Values.Add(entry.Value.badPosture);
                    }

                    // ✅ Notify UI about updates
                    OnPropertyChanged(nameof(TimeLabels));
                    OnPropertyChanged(nameof(PostureSeries));

                    Console.WriteLine("Successfully updated chart data.");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching posture data: {ex.Message}");
            }
        }

        //Fetch Posture Data for PieChart
        public async void FetchPostureDataForPieChart()
        {
            try
            {
                var currentUserId = SessionManager.CurrentUser.Id;
                Console.WriteLine($"User Id data : {currentUserId}");

                var filter = Builders<ProgressReports>.Filter.Eq(p => p.UserId, currentUserId);
                var latestReport = await _dbHelper.ProgressReports
                    .Find(filter)
                    .SortByDescending(p => p.PostureData.StartTime)
                    .FirstOrDefaultAsync();

                if (latestReport == null || latestReport.PostureData?.Outputs == null || latestReport.PostureData.Outputs.Count == 0)
                {
                    Console.WriteLine("⚠ No valid posture data found. Setting default values.");
                    PosturePieSeries[0].Values = new ChartValues<double> { 1 };
                    PosturePieSeries[1].Values = new ChartValues<double> { 1 };
                    return;
                }

                double totalGood = 0;
                double totalBad = 0;

                foreach (var batch in latestReport.PostureData.Outputs)
                {
                    if (batch == null || batch.Count == 0) continue;

                    int badCount = batch.Count(p => p == "Bad Posture");
                    int goodCount = batch.Count - badCount;

                    // Each batch represents 2 minutes
                    double batchDuration = 2.0;

                    totalGood += (goodCount / (double)batch.Count) * batchDuration;
                    totalBad += (badCount / (double)batch.Count) * batchDuration;
                }

                Console.WriteLine($"✅ Processed Pie Chart Data: Good={totalGood}, Bad={totalBad}");

                // Update UI on the main thread
                App.Current.Dispatcher.Invoke(() =>
                {
                    PosturePieSeries[0].Values = new ChartValues<double> { totalGood };
                    PosturePieSeries[1].Values = new ChartValues<double> { totalBad };

                    PostureValues = new ChartValues<double> { totalGood, totalBad };
                    OnPropertyChanged(nameof(PosturePieSeries));
                    OnPropertyChanged(nameof(PostureValues));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching Pie Chart data: {ex.Message}");
            }
        }

        //Fetch Stress Data for pie chart and bar graph
        public async void FetchStressData()
        {
            try
            {
                // Get the currently logged-in user ID
                var currentUserId = SessionManager.CurrentUser.Id;
                Console.WriteLine($"User Id data : {currentUserId}");

                // Create MongoDB filter
                var filter = Builders<ProgressReports>.Filter.Eq(p => p.UserId, currentUserId);
                Console.WriteLine($"Filter data : {filter}");

                // Retrieve all progress reports
                var progressReports = await _dbHelper.ProgressReports
                    .Find(filter)
                    .SortByDescending(p => p.StressData.StartTime)
                    .ToListAsync();

                if (progressReports == null || progressReports.Count == 0)
                {
                    Console.WriteLine("No progress reports found.");
                    return; // No data to process
                }

                Console.WriteLine($"progressReports data count: {progressReports.Count}");

                // Categories for stress emotions
                string[] categories = { "happy", "surprise", "sad", "neutral", "fear", "disgust", "angry" };

                // Dictionary to store total duration per category
                Dictionary<string, double> totalCategoryDuration = new Dictionary<string, double>();

                // Initialize all categories with 0
                foreach (var category in categories)
                {
                    totalCategoryDuration[category] = 0;
                }

                // Dictionary to store stress data per day
                Dictionary<DateTime, Dictionary<string, double>> stressDataByDay = new Dictionary<DateTime, Dictionary<string, double>>();

                foreach (var report in progressReports)
                {
                    Console.WriteLine($"Processing report ID: {report.Id}");

                    if (report.StressData?.Outputs == null || report.StressData.Outputs.Count == 0)
                    {
                        Console.WriteLine($"StressData.Outputs is EMPTY in report ID: {report.Id}");
                        continue; // Skip empty records
                    }

                    // Get the date of the stress session
                    var reportDate = report.StressData.StartTime.Date;

                    // Initialize category totals for the day
                    if (!stressDataByDay.ContainsKey(reportDate))
                    {
                        stressDataByDay[reportDate] = new Dictionary<string, double>();

                        foreach (var category in categories)
                        {
                            stressDataByDay[reportDate][category] = 0;
                        }
                    }

                    // Process each 2-minute batch
                    foreach (var batch in report.StressData.Outputs)
                    {
                        if (batch == null || batch.Count == 0) continue; // Skip empty batches

                        double batchDuration = 2.0; // Each batch represents 2 minutes

                        foreach (var category in categories)
                        {
                            int categoryCount = batch.Count(e => e.Equals(category, StringComparison.OrdinalIgnoreCase));
                            double categoryDuration = (categoryCount / (double)batch.Count) * batchDuration;

                            // Update total duration for the entire dataset
                            totalCategoryDuration[category] += categoryDuration;

                            // Update per-day totals
                            stressDataByDay[reportDate][category] += categoryDuration;
                        }
                    }
                }

                Console.WriteLine($"StressData Processed: {string.Join(", ", totalCategoryDuration.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

                if (totalCategoryDuration.Values.All(v => v == 0))
                {
                    Console.WriteLine("No valid stress data found after processing.");
                    return;
                }

                // Update Pie Chart for Stress Categories
                StressPieSeries = new SeriesCollection
        {
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["happy"] }, Title = "Happy", Fill = System.Windows.Media.Brushes.Green, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["surprise"] }, Title = "Surprise", Fill = System.Windows.Media.Brushes.Blue, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["sad"] }, Title = "Sad", Fill = System.Windows.Media.Brushes.Gray, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["neutral"] }, Title = "Neutral", Fill = System.Windows.Media.Brushes.LightGray, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["fear"] }, Title = "Fear", Fill = System.Windows.Media.Brushes.Purple, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["disgust"] }, Title = "Disgust", Fill = System.Windows.Media.Brushes.Brown, DataLabels = true },
            new PieSeries { Values = new ChartValues<double> { totalCategoryDuration["angry"] }, Title = "Angry", Fill = System.Windows.Media.Brushes.Red, DataLabels = true }
        };

                // Update Bar Chart for Stress Data Per Day
                StressLabels.Clear();
                for (int i = 0; i < StressBarSeries.Count; i++)
                {
                    StressBarSeries[i].Values.Clear();
                }

                foreach (var entry in stressDataByDay.OrderBy(e => e.Key))
                {
                    StressLabels.Add(entry.Key.ToString("MM/dd/yyyy"));

                    for (int i = 0; i < categories.Length; i++)
                    {
                        StressBarSeries[i].Values.Add(entry.Value[categories[i]]);
                    }
                }

                // Notify UI
                OnPropertyChanged(nameof(StressPieSeries));
                OnPropertyChanged(nameof(StressBarSeries));
                OnPropertyChanged(nameof(StressLabels));

                Console.WriteLine("Successfully updated Stress Charts.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching stress data: {ex.Message}");
            }
        }





        //Notify UI about data updates
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
