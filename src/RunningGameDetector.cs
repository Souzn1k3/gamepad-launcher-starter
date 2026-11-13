using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GamepadLauncherStarter
{
    public sealed class GameRoot
    {
        public string Path = "";
        public string DisplayName = "";
        public string Source = "";
    }

    public sealed class RunningGameInfo
    {
        public bool IsGameRunning;
        public string DisplayName = "";
        public string ProcessName = "";
        public string ExecutablePath = "";
        public string Source = "";

        public string Describe()
        {
            string name = string.IsNullOrEmpty(DisplayName) ? "неизвестная игра" : DisplayName;
            string details = "";
            if (!string.IsNullOrEmpty(ProcessName))
                details = ProcessName;
            if (!string.IsNullOrEmpty(Source))
                details += (details.Length > 0 ? ", " : "") + Source;
            return details.Length > 0 ? name + " (" + details + ")" : name;
        }
    }

    public static class RunningGameDetector
    {
        private static readonly Regex SteamPathRegex = new Regex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SteamNameRegex = new Regex("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SteamInstallDirRegex = new Regex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpicInstallLocationRegex = new Regex("\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpicDisplayNameRegex = new Regex("\"DisplayName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] NonGameProcessNames = new string[]
        {
            "yandex", "browser", "chromium", "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi",
            "samsunginternet", "iexplore", "arc", "zen", "librewolf", "waterfox", "palemoon", "thorium", "360chrome",
            "explorer", "mstsc",
            "vlc", "mpv", "wmplayer", "potplayer", "potplayermini", "potplayermini64",
            "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64",
            "powershell", "pwsh", "windowsterminal", "cmd", "conhost",
            "gamepadlauncherstarter",
            "wallpaper32", "wallpaper64", "wallpaperservice32", "wallpaperservice64", "ui32", "ui64",
            "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "discord", "discordptb", "discordcanary",
            "riotclient", "riotclientservices",
            "battle.net", "battle.net helper", "ubisoftconnect", "uplay", "eadesktop", "ealauncher", "origin",
            "galaxyclient", "goggalaxy", "playnite", "itch", "itchio", "xboxpcapp", "gamebar", "xboxgamebar",
            "amazon games"
        };

        public static RunningGameInfo Detect()
        {
            return Detect(CollectGameRoots());
        }

        public static RunningGameInfo Detect(List<GameRoot> roots)
        {
            RunningGameInfo result = new RunningGameInfo();
            try
            {
                RunningGameInfo libraryGame = FindRunningGame(roots);
                if (libraryGame != null)
                    return libraryGame;

                if (FullscreenDetector.IsFullscreenAppActive())
                {
                    ForegroundWindowInfo foreground = FullscreenDetector.GetForegroundInfo();
                    if (foreground != null && !IsExcludedForeground(foreground))
                    {
                        RunningGameInfo fullscreen = new RunningGameInfo();
                        fullscreen.IsGameRunning = true;
                        fullscreen.DisplayName = string.IsNullOrEmpty(foreground.Title)
                            ? foreground.ProcessName
                            : foreground.Title;
                        fullscreen.ProcessName = foreground.ProcessName;
                        fullscreen.Source = "полноэкранный режим";
                        return fullscreen;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка определения запущенной игры", ex);
            }
            return result;
        }

        public static List<GameRoot> CollectGameRoots()
        {
            List<GameRoot> roots = new List<GameRoot>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                CollectSteamRoots(roots, seen);
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать библиотеки Steam: " + ex.Message);
            }
            try
            {
                CollectEpicRoots(roots, seen);
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать библиотеки Epic: " + ex.Message);
            }
            return roots;
        }

        private static void CollectSteamRoots(List<GameRoot> roots, HashSet<string> seen)
        {
            string steamExe = AppCatalog.FindSteamPath();
            if (string.IsNullOrEmpty(steamExe))
                return;
            string steamDirectory = Path.GetDirectoryName(steamExe);
            if (string.IsNullOrEmpty(steamDirectory))
                return;

            List<string> libraries = new List<string>();
            AddLibrary(libraries, steamDirectory);

            string libraryFile = Path.Combine(steamDirectory, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(libraryFile))
                    {
                        Match match = SteamPathRegex.Match(line);
                        if (!match.Success)
                            continue;
                        string libraryPath = UnescapePath(match.Groups[1].Value);
                        if (!string.IsNullOrEmpty(libraryPath) && Directory.Exists(libraryPath))
                            AddLibrary(libraries, libraryPath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось разобрать libraryfolders.vdf: " + ex.Message);
                }
            }

            foreach (string library in libraries)
            {
                string steamApps = Path.Combine(library, "steamapps");
                string common = Path.Combine(steamApps, "common");
                if (!Directory.Exists(common))
                    continue;

                try
                {
                    string[] manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf");
                    foreach (string manifest in manifests)
                    {
                        try
                        {
                            string gameName = "";
                            string installDir = "";
                            foreach (string line in File.ReadAllLines(manifest))
                            {
                                if (gameName.Length == 0)
                                {
                                    Match nameMatch = SteamNameRegex.Match(line);
                                    if (nameMatch.Success)
                                        gameName = UnescapePath(nameMatch.Groups[1].Value);
                                }
                                if (installDir.Length == 0)
                                {
                                    Match installMatch = SteamInstallDirRegex.Match(line);
                                    if (installMatch.Success)
                                        installDir = UnescapePath(installMatch.Groups[1].Value);
                                }
                                if (gameName.Length > 0 && installDir.Length > 0)
                                    break;
                            }

                            if (!string.IsNullOrEmpty(installDir))
                            {
                                string gamePath = Path.Combine(common, installDir);
                                if (Directory.Exists(gamePath))
                                    AddRoot(roots, seen, gamePath, gameName, "Steam");
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось прочитать appmanifest-файлы Steam: " + ex.Message);
                }

                AddRoot(roots, seen, common, "", "Steam");
            }
        }

        private static void CollectEpicRoots(List<GameRoot> roots, HashSet<string> seen)
        {
            string manifestsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "EpicGamesLauncher",
                "Data",
                "Manifests");
            if (!Directory.Exists(manifestsDirectory))
                return;

            foreach (string manifest in Directory.GetFiles(manifestsDirectory, "*.item"))
            {
                try
                {
                    string content = File.ReadAllText(manifest);
                    Match locationMatch = EpicInstallLocationRegex.Match(content);
                    if (!locationMatch.Success)
                        continue;
                    string installLocation = UnescapePath(locationMatch.Groups[1].Value);
                    if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                        continue;

                    string displayName = "";
                    Match nameMatch = EpicDisplayNameRegex.Match(content);
                    if (nameMatch.Success)
                        displayName = UnescapePath(nameMatch.Groups[1].Value);

                    AddRoot(roots, seen, installLocation, displayName, "Epic");
                }
                catch
                {
                }
            }
        }

        private static RunningGameInfo FindRunningGame(List<GameRoot> roots)
        {
            if (roots == null || roots.Count == 0)
                return null;

            HashSet<string> excluded = BuildNonGameProcesses();
            HashSet<int> windowedProcessIds = GetWindowedProcessIds();
            int currentProcessId;
            using (Process current = Process.GetCurrentProcess())
                currentProcessId = current.Id;

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось получить список процессов: " + ex.Message);
                return null;
            }

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.Id == currentProcessId)
                            continue;

                        string processName;
                        try
                        {
                            processName = process.ProcessName;
                        }
                        catch
                        {
                            continue;
                        }
                        if (excluded.Contains(processName))
                            continue;

                        if (windowedProcessIds.Count > 0)
                        {
                            if (!windowedProcessIds.Contains(process.Id))
                                continue;
                        }
                        else
                        {
                            IntPtr handle = process.MainWindowHandle;
                            if (handle == IntPtr.Zero || !IsWindowVisible(handle))
                                continue;
                        }

                        string executablePath = GetProcessPath(process);
                        if (string.IsNullOrEmpty(executablePath))
                            continue;

                        GameRoot root = FindBestRoot(roots, executablePath);
                        if (root == null)
                            continue;

                        RunningGameInfo info = new RunningGameInfo();
                        info.IsGameRunning = true;
                        info.ProcessName = processName;
                        info.ExecutablePath = executablePath;
                        info.Source = root.Source;
                        info.DisplayName = !string.IsNullOrEmpty(root.DisplayName)
                            ? root.DisplayName
                            : DeriveDisplayName(root, executablePath, processName);
                        return info;
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        private static GameRoot FindBestRoot(List<GameRoot> roots, string executablePath)
        {
            if (roots == null || string.IsNullOrEmpty(executablePath))
                return null;

            GameRoot best = null;
            int bestLength = -1;
            foreach (GameRoot root in roots)
            {
                if (root == null || string.IsNullOrEmpty(root.Path))
                    continue;
                string rootWithSlash = root.Path.TrimEnd('\\') + "\\";
                if (rootWithSlash.Length > bestLength &&
                    executablePath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
                {
                    best = root;
                    bestLength = rootWithSlash.Length;
                }
            }
            return best;
        }

        private static string DeriveDisplayName(GameRoot root, string executablePath, string processName)
        {
            try
            {
                string basePath = root.Path.TrimEnd('\\');
                if (executablePath.Length > basePath.Length)
                {
                    string relative = executablePath.Substring(basePath.Length).TrimStart('\\');
                    int separator = relative.IndexOf('\\');
                    string topFolder = separator >= 0 ? relative.Substring(0, separator) : relative;
                    if (!string.IsNullOrEmpty(topFolder))
                        return topFolder;
                }
            }
            catch
            {
            }
            if (!string.IsNullOrEmpty(processName))
                return processName;
            try
            {
                return Path.GetFileNameWithoutExtension(executablePath);
            }
            catch
            {
                return "";
            }
        }

        private static HashSet<int> GetWindowedProcessIds()
        {
            HashSet<int> ids = new HashSet<int>();
            EnumWindowsProc callback = delegate(IntPtr handle, IntPtr parameter)
            {
                try
                {
                    if (!IsWindowVisible(handle))
                        return true;
                    if (GetWindow(handle, GW_OWNER) != IntPtr.Zero)
                        return true;
                    long extendedStyle = GetWindowLong(handle, GWL_EXSTYLE);
                    if ((extendedStyle & WS_EX_TOOLWINDOW) != 0)
                        return true;

                    uint processId;
                    GetWindowThreadProcessId(handle, out processId);
                    if (processId != 0)
                        ids.Add((int)processId);
                }
                catch
                {
                }
                return true;
            };

            try
            {
                EnumWindows(callback, IntPtr.Zero);
                GC.KeepAlive(callback);
            }
            catch
            {
            }
            return ids;
        }

        private static string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch
            {
            }

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
            if (handle == IntPtr.Zero)
                return "";
            try
            {
                int size = 1024;
                System.Text.StringBuilder buffer = new System.Text.StringBuilder(size);
                if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                    return buffer.ToString();
            }
            finally
            {
                CloseHandle(handle);
            }
            return "";
        }

        private static HashSet<string> BuildNonGameProcesses()
        {
            return new HashSet<string>(NonGameProcessNames, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsExcludedForeground(ForegroundWindowInfo foreground)
        {
            if (foreground == null)
                return true;
            if (string.IsNullOrEmpty(foreground.ProcessName))
                return false;
            return BuildNonGameProcesses().Contains(foreground.ProcessName);
        }

        private static void AddLibrary(List<string> libraries, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            foreach (string existing in libraries)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            libraries.Add(path);
        }

        private static void AddRoot(List<GameRoot> roots, HashSet<string> seen, string path, string displayName, string source)
        {
            if (string.IsNullOrEmpty(path))
                return;
            string normalized;
            try
            {
                normalized = Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                normalized = path.TrimEnd('\\');
            }
            if (!Directory.Exists(normalized))
                return;

            if (seen.Contains(normalized))
            {
                foreach (GameRoot existing in roots)
                {
                    if (string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(existing.DisplayName) && !string.IsNullOrEmpty(displayName))
                            existing.DisplayName = displayName;
                        return;
                    }
                }
                return;
            }

            seen.Add(normalized);
            GameRoot root = new GameRoot();
            root.Path = normalized;
            root.DisplayName = displayName == null ? "" : displayName;
            root.Source = source == null ? "" : source;
            roots.Add(root);
        }

        private static string UnescapePath(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\\\\", "\\").Replace('/', '\\').Trim();
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int GWL_EXSTYLE = -20;
        private const uint GW_OWNER = 4;
        private const long WS_EX_TOOLWINDOW = 0x00000080;

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr handle, uint command);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
