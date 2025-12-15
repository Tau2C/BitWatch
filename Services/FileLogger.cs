using System;
using System.IO;

namespace BitWatch.Services
{
    public class FileLogger
    {
        private static readonly Lazy<FileLogger> _instance = new Lazy<FileLogger>(() => new FileLogger());
        private readonly string _logFilePath = "bitwatch.log";
        private readonly object _lock = new object();

        public static FileLogger Instance => _instance.Value;

        private FileLogger() { }

        public void Info(string message)
        {
            Log("INFO", message);
        }

        public void Warning(string message)
        {
            Log("WARNING", message);
        }

        public void Error(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                message = $"{message}{Environment.NewLine}{ex}";
            }
            Log("ERROR", message);
        }

        public void Debug(string message)
        {
            Log("DEBUG", message);
        }

        private void Log(string level, string message)
        {
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
    }
}
