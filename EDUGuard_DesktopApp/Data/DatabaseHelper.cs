using EDUGuard_DesktopApp.Models;
using MongoDB.Driver;
using System;

namespace EDUGuard_DesktopApp
{
    public class DatabaseHelper
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoClient _client;

        public DatabaseHelper()
        {
            var connectionString = "mongodb+srv://myUser:myPassword123@cluster0.qk0epky.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0";
            _client = new MongoClient(connectionString);
            _database = _client.GetDatabase("EDUGuardDB");
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<ProgressReports> ProgressReports => _database.GetCollection<ProgressReports>("ProgressReports");

        /// <summary>
        /// Creates a new progress report entry for posture or stress monitoring.
        /// </summary>
        public string CreateProgressReport(string userEmail, string modelName, DateTime startTime)
        {
            var userId = GetUserIdByEmail(userEmail);
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            var progressReport = new ProgressReports
            {
                UserId = userId
            };

            // Assign start time to the appropriate field
            if (modelName.IndexOf("Stress", StringComparison.OrdinalIgnoreCase) >= 0 || modelName == "Model2")
            {
                progressReport.StressData.StartTime = startTime;
            }
            else if (modelName.IndexOf("Posture", StringComparison.OrdinalIgnoreCase) >= 0 || modelName == "Model1")
            {
                progressReport.PostureData.StartTime = startTime;
            }
            else if (modelName.IndexOf("cvs", StringComparison.OrdinalIgnoreCase) >= 0 || modelName == "Model3")
            {
                progressReport.CVSData.StartTime = startTime;
            }

            ProgressReports.InsertOne(progressReport);
            return progressReport.Id;
        }

        /// <summary>
        /// Retrieves the User ID based on the given email.
        /// </summary>
        public string GetUserIdByEmail(string email)
        {
            var user = Users.Find(u => u.Email == email).FirstOrDefault();
            return user?.Id;
        }

        public string GetProgressReportId(string email, string modelName)
        {
            try
            {
                var filter = Builders<ProgressReports>.Filter.Eq(r => r.UserId, email);
                var report = ProgressReports.Find(filter)
                              .SortByDescending(r => r.PostureData.StartTime)
                              .FirstOrDefault();

                return report?.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching progress report ID: {ex.Message}");
                return null;
            }
        }



    }
}
