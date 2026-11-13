using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
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
        public int ProcessId;
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

    [DataContract]
    internal sealed class EpicManifestData
    {
        [DataMember(Name = "InstallLocation")]
        public string InstallLocation = "";

        [DataMember(Name = "DisplayName")]
        public string DisplayName = "";
    }

    public static class RunningGameDetector
    {
        private static readonly Regex SteamPathRegex = new Regex("\"path\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SteamValueRegex = new Regex("\"(name|installdir)\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpicInstallLocationRegex = new Regex("\"InstallLocation\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpicDisplayNameRegex = new Regex("\"DisplayName\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            "amazon games",
            "rtss", "rtssharedmemory", "radeonsoftware", "amdow", "nvcontainer", "nvsphelper64",
            "nvidia overlay", "nvidia share", "nvshare", "obs", "obs64", "obs32",
            "telegram", "telegramdesktop", "msiafterburner", "overwolf", "overwolfbrowser",
            "gamebarft", "xboxgamebarwidgets"
        };

        private static readonly object RootCacheLock = new object();
        private static readonly TimeSpan RootCacheTtl = TimeSpan.FromSeconds(60);
        private static List<GameRoot> _rootCache;
        private static DateTime _rootCacheAt = DateTime.MinValue;

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
                        fullscreen.ProcessId = FullscreenDetector.GetForegroundProcessId();
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
            return CollectGameRoots(false);
        }

        public static List<GameRoot> CollectGameRoots(bool forceRefresh)
        {
            lock (RootCacheLock)
            {
                if (!forceRefresh && _rootCache != null && DateTime.UtcNow - _rootCacheAt < RootCacheTtl)
                    return CloneRoots(_rootCache);

                List<GameRoot> roots = CollectGameRootsCore();
                _rootCache = roots;
                _rootCacheAt = DateTime.UtcNow;
                return CloneRoots(roots);
            }
        }

        public static void InvalidateGameRoots()
        {
            lock (RootCacheLock)
            {
                _rootCache = null;
                _rootCacheAt = DateTime.MinValue;
            }
        }

        private static List<GameRoot> CollectGameRootsCore()
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

        private static List<GameRoot> CloneRoots(List<GameRoot> roots)
        {
            List<GameRoot> copy = new List<GameRoot>();
            if (roots == null)
                return copy;
            foreach (GameRoot root in roots)
            {
                if (root == null)
                    continue;
                GameRoot item = new GameRoot();
                item.Path = root.Path;
                item.DisplayName = root.DisplayName;
                item.Source = root.Source;
                copy.Add(item);
            }
            return copy;
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
                        string libraryPath = UnescapeValue(match.Groups[1].Value);
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
                                Match valueMatch = SteamValueRegex.Match(line);
                                if (!valueMatch.Success)
                                    continue;

                                string key = valueMatch.Groups[1].Value;
                                string value = UnescapeValue(valueMatch.Groups[2].Value);
                                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && gameName.Length == 0)
                                    gameName = value;
                                else if (string.Equals(key, "installdir", StringComparison.OrdinalIgnoreCase) && installDir.Length == 0)
                                    installDir = value;

                                if (gameName.Length > 0 && installDir.Length > 0)
                                    break;
                            }

                            if (!string.IsNullOrEmpty(installDir))
                            {
                                string gamePath;
                                if (TryCombineUnderBase(common, installDir, out gamePath))
                                    AddRoot(roots, seen, gamePath, gameName, "Steam");
                                else
                                    Logger.Warn("Пропускаю подозрительный installdir в " + Path.GetFileName(manifest) + ": " + installDir);
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

            foreach (string manifestPath in Directory.GetFiles(manifestsDirectory, "*.item"))
            {
                try
                {
                    string installLocation = "";
                    string displayName = "";

                    EpicManifestData data = TryReadEpicManifest(manifestPath);
                    if (data != null)
                    {
                        installLocation = data.InstallLocation == null ? "" : data.InstallLocation;
                        displayName = data.DisplayName == null ? "" : data.DisplayName;
                    }

                    if (string.IsNullOrEmpty(installLocation))
                    {
                        string content = File.ReadAllText(manifestPath);
                        Match locationMatch = EpicInstallLocationRegex.Match(content);
                        if (locationMatch.Success)
                            installLocation = UnescapeValue(locationMatch.Groups[1].Value);
                        if (string.IsNullOrEmpty(displayName))
                        {
                            Match nameMatch = EpicDisplayNameRegex.Match(content);
                            if (nameMatch.Success)
                                displayName = UnescapeValue(nameMatch.Groups[1].Value);
                        }
                    }

                    if (string.IsNullOrEmpty(installLocation) || !Path.IsPathRooted(installLocation))
                        continue;
                    if (!Directory.Exists(installLocation))
                        continue;

                    AddRoot(roots, seen, installLocation, displayName, "Epic");
                }
                catch
                {
                }
            }
        }

        private static EpicManifestData TryReadEpicManifest(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(EpicManifestData));
                    return (EpicManifestData)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось разобрать Epic-манифест " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private static RunningGameInfo FindRunningGame(List<GameRoot> roots)
        {
            if (roots == null || roots.Count == 0)
                return null;

            HashSet<string> excluded = BuildNonGameProcesses();
            HashSet<int> windowedProcessIds = GetWindowedProcessIds();
            int foregroundProcessId = FullscreenDetector.GetForegroundProcessId();
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

            RunningGameInfo best = null;
            DateTime bestStarted = DateTime.MinValue;

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
                        info.ProcessId = process.Id;
                        info.ProcessName = processName;
                        info.ExecutablePath = executablePath;
                        info.Source = root.Source;
                        info.DisplayName = !string.IsNullOrEmpty(root.DisplayName)
                            ? root.DisplayName
                            : DeriveDisplayName(root, executablePath, processName);

                        DateTime started;
                        try
                        {
                            started = process.StartTime;
                        }
                        catch
                        {
                            started = DateTime.MinValue;
                        }

                        if (best == null || started > bestStarted)
                        {
                            best = info;
                            bestStarted = started;
                        }
                        if (foregroundProcessId != 0 && process.Id == foregroundProcessId)
                        {
                            best = info;
                            break;
                        }
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
            return best;
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
                StringBuilder buffer = new StringBuilder(size);
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

        private static bool TryCombineUnderBase(string basePath, string relativePath, out string combined)
        {
            combined = "";
            try
            {
                if (string.IsNullOrEmpty(relativePath))
                    return false;
                string baseFull = Path.GetFullPath(basePath).TrimEnd('\\');
                string candidate = Path.GetFullPath(Path.Combine(baseFull, relativePath));
                if (!candidate.StartsWith(baseFull + "\\", StringComparison.OrdinalIgnoreCase))
                    return false;
                combined = candidate;
                return true;
            }
            catch
            {
                return false;
            }
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

        private static string UnescapeValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current != '\\' || i + 1 >= value.Length)
                {
                    result.Append(current);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case '\\':
                        result.Append('\\');
                        break;
                    case '"':
                        result.Append('"');
                        break;
                    case '/':
                        result.Append('/');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case 'b':
                        result.Append('\b');
                        break;
                    case 'f':
                        result.Append('\f');
                        break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            string hex = value.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                result.Append((char)code);
                                i += 4;
                                break;
                            }
                        }
                        result.Append('u');
                        break;
                    default:
                        result.Append(next);
                        break;
                }
            }
            return result.ToString();
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
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
