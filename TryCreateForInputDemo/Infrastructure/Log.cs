using System;

namespace GitConverter.Lib.Logging
{
    /// <summary>
    /// Simple logging utility for the converter infrastructure.
    /// </summary>
    public static class Log
    {
        public static void Debug(string message)
        {
            Console.WriteLine($"[DEBUG] {message}");
        }

        public static void Warn(string message)
        {
            Console.WriteLine($"[WARN] {message}");
        }

        public static void Error(string message)
        {
            Console.WriteLine($"[ERROR] {message}");
        }

        public static void Error(string message, Exception ex)
        {
            Console.WriteLine($"[ERROR] {message}");
            Console.WriteLine($"        Exception: {ex.GetType().Name}: {ex.Message}");
        }

        public static void Info(string message)
        {
            Console.WriteLine($"[INFO] {message}");
        }
    }
}
