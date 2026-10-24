using System;
using System.IO;
using System.Text;

namespace GamepadLauncherStarter
{
    public static class Logger
    {
        private static readonly object Sync = new object();
        private static string _logPath;
        private static bool _initialized;
        private static string _initError = "";

        public static string LogPath
        {
            get { return _logPath; }
        }

        public static string InitError
        {
            get { return _initError; }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized)
                    return;
                _initialized = true;
                try
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                    _logPath = AppPaths.LogFile;
                    RotateIfNeeded();
                }
                catch (Exception ex)
                {
                    _initError = ex.Message;
                    try
                    {
                        _logPath = Path.Combine(Path.GetTempPath(), "GamepadLauncherStarter.log");
                    }
                    catch
                    {
                        _logPath = null;
                    }
                }
            }

            Info("=== " + AppInfo.ProductName + " " + AppInfo.Version + " ===");
            Info("Exe: " + AppInfo.ExePath);
            Info("OS: " + Environment.OSVersion.VersionString + "; 64-bit: " + Environment.Is64BitOperatingSystem);
            if (!string.IsNullOrEmpty(_initError))
                Info("Не удалось использовать папку данных, журнал перенесён: " + _initError);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception exception)
        {
            string detail = exception == null ? "" : " | " + exception.GetType().Name + ": " + exception.Message + Environment.NewLine + exception.StackTrace;
            Write("ERROR", message + detail);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    if (string.IsNullOrEmpty(_logPath))
                        return;
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + (message ?? "") + Environment.NewLine;
                    File.AppendAllText(_logPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath))
                    return;
                FileInfo info = new FileInfo(_logPath);
                if (info.Length < 1024 * 1024)
                    return;

                string p1 = _logPath + ".1";
                string p2 = _logPath + ".2";
                string p3 = _logPath + ".3";
                if (File.Exists(p3))
                    File.Delete(p3);
                if (File.Exists(p2))
                    File.Move(p2, p3);
                if (File.Exists(p1))
                    File.Move(p1, p2);
                File.Move(_logPath, p1);
            }
            catch
            {
            }
        }
    }

    public static class AppIcon
    {
        private static System.Drawing.Icon _icon;

        public static System.Drawing.Icon Get()
        {
            if (_icon != null)
                return _icon;
            try
            {
                using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico"))
                {
                    if (stream != null)
                        _icon = new System.Drawing.Icon(stream);
                }
            }
            catch
            {
            }
            if (_icon == null)
            {
                try
                {
                    _icon = System.Drawing.Icon.ExtractAssociatedIcon(AppInfo.ExePath);
                }
                catch
                {
                }
            }
            if (_icon == null)
                _icon = System.Drawing.SystemIcons.Application;
            return _icon;
        }
    }
}
