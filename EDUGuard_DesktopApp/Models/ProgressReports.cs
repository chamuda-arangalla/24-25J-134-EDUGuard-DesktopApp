using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;


namespace EDUGuard_DesktopApp.Models
{
    public class ProgressReports
    {
        [BsonId] // MongoDB document primary key
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } 

        // Posture data stored as an object
        public ModelData PostureData { get; set; } = new ModelData();

        // Stress data stored as an object
        public ModelData StressData { get; set; } = new ModelData();

        // Cvs data stored as an object
        public ModelData CVSData { get; set; } = new ModelData();
        // Hydration data stored as an object
        public ModelData HydrationData { get; set; } = new ModelData();

    }

    public class ModelData
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<List<string>> Outputs { get; set; } = new List<List<string>>();
    }

    
}
