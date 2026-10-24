using System;
using System.IO;
using System.Reflection;

namespace GamepadLauncherStarter
{
    public static class AppInfo
    {
        public const string ProductName = "Gamepad Launcher Starter";
        public const string Version = "1.0.0";
        public const string MutexName = @"Local\GamepadLauncherStarter_8f3b2f5d6f4a4e51";
        public const string ShowSettingsEventName = @"Local\GamepadLauncherStarter_ShowSettings_8f3b2f5d6f4a4e51";

        public static string ExePath
        {
            get
            {
                try
                {
                    Assembly assembly = Assembly.GetEntryAssembly();
                    if (assembly != null && !string.IsNullOrEmpty(assembly.Location))
                        return assembly.Location;
                }
                catch
                {
                }
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GamepadLauncherStarter.exe");
                }
            }
        }
    }

    public static class AppPaths
    {
        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GamepadLauncherStarter");
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(DataDirectory, "settings.xml"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(DataDirectory, "app.log"); }
        }

        public static string DiagnosticsFile
        {
            get { return Path.Combine(DataDirectory, "diagnostics.txt"); }
        }

        public static string SteamBackupFile
        {
            get { return Path.Combine(DataDirectory, "steam_autostart_backup.txt"); }
        }

        public static void EnsureDataDirectory()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
            }
            catch
            {
            }
        }
    }
}
