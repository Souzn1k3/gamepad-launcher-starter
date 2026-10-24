using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace GamepadLauncherStarter
{
    public static class AppCatalog
    {
        public const string SteamId = "steam";
        public const string EpicId = "epic";
        public const string DiscordId = "discord";

        public static AppEntry CreateSteam()
        {
            AppEntry entry = new AppEntry();
            entry.Id = SteamId;
            entry.Name = "Steam";
            entry.Enabled = true;
            entry.ProcessName = "steam";
            entry.Arguments = "";
            entry.MinimizedArguments = "-silent";
            entry.LaunchMode = LaunchMode.Normal;
            entry.ShowWindowIfRunning = true;
            entry.BuiltIn = true;
            return entry;
        }

        public static AppEntry CreateEpic()
        {
            AppEntry entry = new AppEntry();
            entry.Id = EpicId;
            entry.Name = "Epic Games";
            entry.Enabled = true;
            entry.ProcessName = "EpicGamesLauncher";
            entry.Arguments = "";
            entry.MinimizedArguments = "-silent";
            entry.LaunchMode = LaunchMode.Normal;
            entry.ShowWindowIfRunning = true;
            entry.BuiltIn = true;
            return entry;
        }

        public static AppEntry CreateDiscord()
        {
            AppEntry entry = new AppEntry();
            entry.Id = DiscordId;
            entry.Name = "Discord";
            entry.Enabled = false;
            entry.ProcessName = "Discord";
            entry.Arguments = "--processStart Discord.exe";
            entry.MinimizedArguments = "--processStart Discord.exe --start-minimized";
            entry.LaunchMode = LaunchMode.Normal;
            entry.ShowWindowIfRunning = true;
            entry.BuiltIn = true;
            return entry;
        }

        public static void AutodetectPaths(IList<AppEntry> apps)
        {
            if (apps == null)
                return;
            foreach (AppEntry app in apps)
            {
                if (app == null || !app.BuiltIn)
                    continue;

                bool needsPath = string.IsNullOrEmpty(app.ExecutablePath) || !File.Exists(app.ExecutablePath);

                if (string.Equals(app.Id, SteamId, StringComparison.OrdinalIgnoreCase) && needsPath)
                {
                    string path = FindSteamPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Info("Найден Steam: " + path);
                        app.ExecutablePath = path;
                    }
                }
                else if (string.Equals(app.Id, EpicId, StringComparison.OrdinalIgnoreCase) && needsPath)
                {
                    string path = FindEpicPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        Logger.Info("Найден Epic Games Launcher: " + path);
                        app.ExecutablePath = path;
                    }
                }
                else if (string.Equals(app.Id, DiscordId, StringComparison.OrdinalIgnoreCase) && needsPath)
                {
                    string path;
                    string arguments;
                    string minimizedArguments;
                    if (TryFindDiscord(out path, out arguments, out minimizedArguments))
                    {
                        Logger.Info("Найден Discord: " + path);
                        app.ExecutablePath = path;
                        app.Arguments = arguments;
                        app.MinimizedArguments = minimizedArguments;
                    }
                }

                if (string.IsNullOrEmpty(app.ProcessName) && !string.IsNullOrEmpty(app.ExecutablePath))
                    app.ProcessName = SafeFileNameWithoutExtension(app.ExecutablePath);
            }
        }

        public static string FindSteamPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string value = NormalizePath(key.GetValue("SteamExe") as string);
                        if (File.Exists(value))
                            return value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать путь Steam из HKCU: " + ex.Message);
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string value = NormalizePath(key.GetValue("InstallPath") as string);
                        if (!string.IsNullOrEmpty(value))
                        {
                            string exe = Path.Combine(value, "steam.exe");
                            if (File.Exists(exe))
                                return exe;
                        }
                    }
                }
            }
            catch
            {
            }

            string[] defaults = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe")
            };
            foreach (string candidate in defaults)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return "";
        }

        public static string FindEpicPath()
        {
            string[] commandKeys = new string[]
            {
                @"com.epicgames.launcher\shell\open\command",
                @"Software\Classes\com.epicgames.launcher\shell\open\command"
            };

            foreach (string subKey in commandKeys)
            {
                try
                {
                    RegistryKey root = subKey.StartsWith("Software\\", StringComparison.OrdinalIgnoreCase)
                        ? Registry.CurrentUser
                        : Registry.ClassesRoot;
                    using (RegistryKey key = root.OpenSubKey(subKey))
                    {
                        if (key == null)
                            continue;
                        string command = key.GetValue(null) as string;
                        string exe = ExtractExecutableFromCommand(command);
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                            return exe;
                    }
                }
                catch
                {
                }
            }

            string[] defaults = new string[]
            {
                @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
                @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe"
            };
            foreach (string candidate in defaults)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return "";
        }

        public static bool TryFindDiscord(out string path, out string arguments, out string minimizedArguments)
        {
            path = "";
            arguments = "";
            minimizedArguments = "";

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string[] updateCandidates = new string[]
            {
                Path.Combine(localAppData, "Discord", "Update.exe"),
                Path.Combine(programFiles, "Discord", "Update.exe")
            };
            foreach (string candidate in updateCandidates)
            {
                if (File.Exists(candidate))
                {
                    path = candidate;
                    arguments = "--processStart Discord.exe";
                    minimizedArguments = "--processStart Discord.exe --start-minimized";
                    return true;
                }
            }

            try
            {
                string discordRoot = Path.Combine(localAppData, "Discord");
                if (Directory.Exists(discordRoot))
                {
                    string[] directories = Directory.GetDirectories(discordRoot, "app-*");
                    Array.Sort(directories, delegate(string a, string b)
                    {
                        return CompareVersionFolderNames(b, a);
                    });
                    foreach (string directory in directories)
                    {
                        string exe = Path.Combine(directory, "Discord.exe");
                        if (File.Exists(exe))
                        {
                            path = exe;
                            arguments = "";
                            minimizedArguments = "--start-minimized";
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Discord"))
                {
                    if (key != null)
                    {
                        string icon = key.GetValue("DisplayIcon") as string;
                        if (!string.IsNullOrEmpty(icon))
                        {
                            int comma = icon.IndexOf(',');
                            if (comma >= 0)
                                icon = icon.Substring(0, comma);
                            icon = NormalizePath(icon);
                            if (File.Exists(icon))
                            {
                                path = icon;
                                if (string.Equals(Path.GetFileName(icon), "Update.exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    arguments = "--processStart Discord.exe";
                                    minimizedArguments = "--processStart Discord.exe --start-minimized";
                                }
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static string ExtractExecutableFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return "";
            string trimmed = command.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = trimmed.IndexOf('"', 1);
                if (end > 1)
                    return NormalizePath(trimmed.Substring(1, end - 1));
            }
            int space = trimmed.IndexOf(' ');
            string result = space > 0 ? trimmed.Substring(0, space) : trimmed;
            return NormalizePath(result);
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            string result = path.Trim().Trim('"').Replace('/', '\\');
            return result;
        }

        private static string SafeFileNameWithoutExtension(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                return "";
            }
        }

        private static int CompareVersionFolderNames(string left, string right)
        {
            string a = Path.GetFileName(left).Replace("app-", "");
            string b = Path.GetFileName(right).Replace("app-", "");
            Version va;
            Version vb;
            if (Version.TryParse(a, out va) && Version.TryParse(b, out vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
