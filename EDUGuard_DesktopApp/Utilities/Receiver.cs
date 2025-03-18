using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EDUGuard_DesktopApp.Utilities
{
    public class Receiver
    {
        private const string HOST = "127.0.0.1";
        private const int PORT = 5001;

        public static async Task ReceiveLatestPostureDataAsync()
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    Console.WriteLine($"Attempting to connect to Python server at {HOST}:{PORT}...");

                    // Connect to Python server (with a timeout of 5 seconds)
                    await client.ConnectAsync(HOST, PORT);
                    Console.WriteLine("Connected to Python server!");

                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string jsonData = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(jsonData))
                        {
                            Console.WriteLine("No data received from Python server.");
                            return;
                        }

                        try
                        {
                            // Parse JSON data
                            JObject parsedData = JObject.Parse(jsonData);
                            JArray latestPostureArray = (JArray)parsedData["latest_posture"];

                            if (latestPostureArray == null || latestPostureArray.Count == 0)
                            {
                                Console.WriteLine("Received empty posture data.");
                                return;
                            }

                            // Log received data
                            Console.WriteLine($"Received Latest Posture Data: {latestPostureArray}");

                            // Calculate Bad Posture Percentage
                            int badPostureCount = latestPostureArray.Count(e => e.ToString() == "Bad Posture");
                            double badPosturePercentage = (double)badPostureCount / latestPostureArray.Count * 100;

                            // Trigger notification if posture is poor
                            if (badPosturePercentage > 60)
                            {
                                Console.WriteLine("Warning: Your posture is poor! Correct it immediately.");
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            Console.WriteLine($"JSON Parse Error: {jsonEx.Message}");
                        }
                    }
                }
            }
            catch (SocketException)
            {
                Console.WriteLine("Python server is not available. Please start the Python server and try again.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving posture data: {ex.Message}");
            }
        }
    }
}
