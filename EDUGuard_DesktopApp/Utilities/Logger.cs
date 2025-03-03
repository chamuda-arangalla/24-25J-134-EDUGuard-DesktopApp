using System;
using System.IO;

namespace EDUGuard_DesktopApp.Utilities
{
    public static class Logger
    {
        private static readonly string LogFilePath = "C:\\Users\\chamu\\source\\repos\\EDUGuard_DesktopApp\\error_log.txt";

        public static void LogError(string message)
        {
            try
            {
                string logMessage = $"{DateTime.Now}: {message}\n";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // Avoid crashes if logging fails
            }
        }
    }
}
