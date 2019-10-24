using System;
using System.Collections.Generic;

namespace LowLevelTransport.Utils
{
    public static class Log
    {
        public enum LogLevel
        {
            Trace,
            Debug,
            Info,
            Warning,
            Error,
        }

        public static bool Enable = false;

        public static bool ForceLogBinary = false;

        public static LogLevel DefaultLevel = LogLevel.Info;

        private static readonly Dictionary<LogLevel, Action<string>> Loggers = new Dictionary<LogLevel, Action<string>>();

        private static readonly Dictionary<LogLevel, Action<string>> DefaultLoggers = new Dictionary<LogLevel, Action<string>>();

        static Log()
        {
            DefaultLoggers.Add(LogLevel.Trace, Console.WriteLine);
            DefaultLoggers.Add(LogLevel.Debug, Console.WriteLine);
            DefaultLoggers.Add(LogLevel.Info, Console.WriteLine);
            DefaultLoggers.Add(LogLevel.Warning, Console.WriteLine);
            DefaultLoggers.Add(LogLevel.Error, Console.WriteLine);
        }

        public static void RegistLogger(LogLevel level, Action<string> logger)
        {
            Loggers.Add(level, logger);
        }

        private static void Println(LogLevel level, string s, params object[] args)
        {
            if (!Enable)
            {
                return;
            }

            if ((int)level < (int)DefaultLevel)
            {
                return;
            }

            string logData;
            try
            {
                logData = string.Format(s, args);
            }
            catch (Exception)
            {
                logData = s;
            }
            logData = $"[LowLevelTransport|{level}]:{logData}";

            if (Loggers.ContainsKey(level))
            {
                Loggers[level].Invoke(logData);
            }
            else
            {
                DefaultLoggers[level]?.Invoke(logData);
            }
        }

        public static void Trace(string s, params object[] args) => Println(LogLevel.Trace, s, args);

        public static void Debug(string s, params object[] args) => Println(LogLevel.Debug, s, args);

        public static void Info(string s, params object[] args) => Println(LogLevel.Info, s, args);

        public static void Warning(string s, params object[] args) => Println(LogLevel.Warning, s, args);

        public static void Error(string s, params object[] args) => Println(LogLevel.Error, s, args);

        public static string FullDump(byte[] payload, int offset, int sz)
        {
            string outStr = $"len:{sz}";
            outStr += ", data:[";
            for (int i = 0; i < sz; i++)
            {
                outStr += ( payload[offset + i] + "," );
            }

            outStr += "]";
            return outStr;
        }

        public static string Dump(byte[] payload, int offset, int sz)
        {
            string outStr = $"len:{sz}";
            if (!ForceLogBinary)
            {
                return outStr;
            }

            outStr += ", data:[";
            var logSize = sz;
            if (sz > 32)
            {
                logSize = 32;
            }

            for (int i = 0; i < logSize; i++)
            {
                outStr += ( payload[offset + i] + "," );
            }

            outStr += "]";
            return outStr;
        }
    }
}

