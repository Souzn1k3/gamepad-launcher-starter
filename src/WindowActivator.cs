using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GamepadLauncherStarter
{
    public static class WindowActivator
    {
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private const uint GW_OWNER = 4;

        public static bool ShowAppWindow(AppEntry app)
        {
            if (app == null)
                return false;

            try
            {
                if (string.Equals(app.Id, AppCatalog.SteamId, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryStartUri("steam://open/main"))
                    {
                        Logger.Info("Steam: отправлен запрос на показ главного окна.");
                        return true;
                    }
                }

                string primary = LauncherService.ResolveProcessName(app);
                string[] names = BuildProcessNames(app, primary);
                IntPtr handle = FindBestWindow(names);
                if (handle != IntPtr.Zero)
                {
                    RestoreAndActivate(handle);
                    Logger.Info("Окно развёрнуто: " + app.Name);
                    return true;
                }

                if (string.Equals(app.Id, AppCatalog.EpicId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(app.ExecutablePath) && System.IO.File.Exists(app.ExecutablePath))
                {
                    try
                    {
                        Process.Start(app.ExecutablePath);
                        Logger.Info("Epic Games: окно не найдено, выполнен повторный запуск для активации.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Epic Games: не удалось активировать окно повторным запуском: " + ex.Message);
                    }
                }

                Logger.Warn("Окно приложения не найдено: " + app.Name);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка при показе окна приложения " + app.Name, ex);
                return false;
            }
        }

        public static void ScheduleMinimize(string processName, int timeoutMs)
        {
            if (string.IsNullOrEmpty(processName))
                return;

            string[] names = SplitNames(processName);
            DateTime started = DateTime.UtcNow;
            Timer timer = null;
            timer = new Timer(delegate(object state)
            {
                try
                {
                    if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                    {
                        if (timer != null)
                            timer.Dispose();
                        return;
                    }

                    IntPtr handle = FindBestWindow(names);
                    if (handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle))
                    {
                        ShowWindow(handle, SW_MINIMIZE);
                        Logger.Info("Окно свёрнуто после запуска: " + processName);
                        if (timer != null)
                            timer.Dispose();
                    }
                }
                catch
                {
                    if (timer != null)
                        timer.Dispose();
                }
            }, null, 800, 500);
        }

        private static bool TryStartUri(string uri)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = uri;
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось открыть URI " + uri + ": " + ex.Message);
                return false;
            }
        }

        private static string[] BuildProcessNames(AppEntry app, string primary)
        {
            List<string> names = new List<string>();
            AddName(names, primary);

            if (app != null)
            {
                if (string.Equals(app.Id, AppCatalog.SteamId, StringComparison.OrdinalIgnoreCase))
                {
                    AddName(names, "steam");
                    AddName(names, "steamwebhelper");
                }
                else if (string.Equals(app.Id, AppCatalog.EpicId, StringComparison.OrdinalIgnoreCase))
                {
                    AddName(names, "EpicGamesLauncher");
                }
                else if (string.Equals(app.Id, AppCatalog.DiscordId, StringComparison.OrdinalIgnoreCase))
                {
                    AddName(names, "Discord");
                    AddName(names, "DiscordPTB");
                }
            }

            if (!string.IsNullOrEmpty(app.ProcessName))
            {
                foreach (string name in SplitNames(app.ProcessName))
                    AddName(names, name);
            }

            return names.ToArray();
        }

        private static void AddName(List<string> names, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            string cleaned = name.Trim();
            if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - 4);
            if (cleaned.Length == 0)
                return;
            foreach (string existing in names)
            {
                if (string.Equals(existing, cleaned, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            names.Add(cleaned);
        }

        private static string[] SplitNames(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new string[0];
            string[] parts = value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> result = new List<string>();
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    result.Add(trimmed);
            }
            return result.ToArray();
        }

        private static IntPtr FindBestWindow(string[] processNames)
        {
            if (processNames == null || processNames.Length == 0)
                return IntPtr.Zero;

            HashSet<int> processIds = new HashSet<int>();
            foreach (string name in processNames)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    foreach (Process process in processes)
                    {
                        try
                        {
                            processIds.Add(process.Id);
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch
                {
                }
            }

            if (processIds.Count == 0)
                return IntPtr.Zero;

            IntPtr bestHandle = IntPtr.Zero;
            long bestScore = long.MinValue;

            EnumWindowsProc callback = delegate(IntPtr handle, IntPtr parameter)
            {
                try
                {
                    if (handle == IntPtr.Zero)
                        return true;

                    uint processId;
                    GetWindowThreadProcessId(handle, out processId);
                    if (!processIds.Contains((int)processId))
                        return true;

                    long extendedStyle = GetWindowLong(handle, GWL_EXSTYLE);
                    if ((extendedStyle & WS_EX_TOOLWINDOW) != 0)
                        return true;

                    if (GetWindow(handle, GW_OWNER) != IntPtr.Zero)
                        return true;

                    int titleLength = GetWindowTextLength(handle);
                    if (titleLength <= 0)
                        return true;

                    RECT rectangle;
                    GetWindowRect(handle, out rectangle);
                    long width = Math.Max(0, rectangle.Right - rectangle.Left);
                    long height = Math.Max(0, rectangle.Bottom - rectangle.Top);
                    long area = width * height;

                    bool visible = IsWindowVisible(handle);
                    bool iconic = IsIconic(handle);

                    long score = area + 10000;
                    if (visible)
                        score += 1000000;
                    if (!iconic)
                        score += 100000;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHandle = handle;
                    }
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

            return bestHandle;
        }

        private static void RestoreAndActivate(IntPtr handle)
        {
            try
            {
                if (IsIconic(handle))
                    ShowWindow(handle, SW_RESTORE);
                else
                    ShowWindow(handle, SW_SHOW);

                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = 0;
                uint currentThread = GetCurrentThreadId();
                if (foreground != IntPtr.Zero)
                {
                    uint foregroundProcessId;
                    foregroundThread = GetWindowThreadProcessId(foreground, out foregroundProcessId);
                }

                bool attached = false;
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(foregroundThread, currentThread, true);

                try
                {
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                    SwitchToThisWindow(handle, true);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(foregroundThread, currentThread, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось активировать окно: " + ex.Message);
            }
        }

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr handle, bool unknown);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out RECT rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr handle, uint command);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
