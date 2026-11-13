using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GamepadLauncherStarter
{
    public enum LaunchOutcome
    {
        Launched,
        AlreadyRunningShown,
        AlreadyRunningSkipped,
        Missing,
        Error
    }

    public sealed class LaunchResult
    {
        public AppEntry App;
        public LaunchOutcome Outcome;
        public string Message = "";

        public string AppName
        {
            get { return App == null ? "" : App.Name; }
        }
    }

    public static class LauncherService
    {
        public static string ResolveProcessName(AppEntry app)
        {
            if (app == null)
                return "";
            if (!string.IsNullOrEmpty(app.ProcessName))
            {
                string[] names = SplitProcessNames(app.ProcessName);
                if (names.Length > 0)
                    return names[0];
            }
            if (!string.IsNullOrEmpty(app.ExecutablePath))
            {
                try
                {
                    return Path.GetFileNameWithoutExtension(app.ExecutablePath);
                }
                catch
                {
                }
            }
            return "";
        }

        public static string[] GetProcessNames(AppEntry app)
        {
            if (app == null)
                return new string[0];

            List<string> names = new List<string>();
            foreach (string name in SplitProcessNames(app.ProcessName))
                AddUnique(names, name);

            if (names.Count == 0)
            {
                string resolved = ResolveProcessName(app);
                if (!string.IsNullOrEmpty(resolved))
                    AddUnique(names, resolved);
            }

            if (string.Equals(app.Id, AppCatalog.SteamId, StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(names, "steam");
                AddUnique(names, "steamwebhelper");
            }
            else if (string.Equals(app.Id, AppCatalog.EpicId, StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(names, "EpicGamesLauncher");
            }
            else if (string.Equals(app.Id, AppCatalog.DiscordId, StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(names, "Discord");
                AddUnique(names, "DiscordPTB");
            }

            return names.ToArray();
        }

        public static bool IsRunning(AppEntry app)
        {
            foreach (string name in GetProcessNames(app))
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    try
                    {
                        if (processes.Length > 0)
                            return true;
                    }
                    finally
                    {
                        foreach (Process process in processes)
                            process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось проверить процесс " + name + ": " + ex.Message);
                }
            }
            return false;
        }

        public static LaunchResult LaunchOrShow(AppEntry app)
        {
            LaunchResult result = new LaunchResult();
            result.App = app;
            if (app == null)
            {
                result.Outcome = LaunchOutcome.Error;
                result.Message = "приложение не задано";
                return result;
            }

            try
            {
                bool running = IsRunning(app);
                if (running)
                {
                    if (app.ShowWindowIfRunning)
                    {
                        bool shown = WindowActivator.ShowAppWindow(app);
                        result.Outcome = shown ? LaunchOutcome.AlreadyRunningShown : LaunchOutcome.AlreadyRunningSkipped;
                        result.Message = shown ? "окно развёрнуто" : "окно не найдено";
                        Logger.Info(app.Name + ": уже запущено; " + result.Message);
                    }
                    else
                    {
                        result.Outcome = LaunchOutcome.AlreadyRunningSkipped;
                        result.Message = "уже запущено";
                        Logger.Info(app.Name + ": уже запущено, окно не трогаю (настройка).");
                    }
                    return result;
                }

                if (string.IsNullOrEmpty(app.ExecutablePath))
                {
                    result.Outcome = LaunchOutcome.Missing;
                    result.Message = "путь не указан";
                    Logger.Warn(app.Name + ": путь не указан, запуск пропущен.");
                    return result;
                }
                if (!File.Exists(app.ExecutablePath))
                {
                    result.Outcome = LaunchOutcome.Missing;
                    result.Message = "файл не найден";
                    Logger.Warn(app.Name + ": файл не найден — " + app.ExecutablePath);
                    return result;
                }

                string arguments = app.Arguments ?? "";
                if (app.LaunchMode == LaunchMode.Minimized && !string.IsNullOrEmpty(app.MinimizedArguments))
                    arguments = app.MinimizedArguments;

                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = app.ExecutablePath;
                info.Arguments = arguments;
                info.UseShellExecute = true;
                try
                {
                    string directory = Path.GetDirectoryName(app.ExecutablePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        info.WorkingDirectory = directory;
                }
                catch
                {
                }

                Process.Start(info);
                Logger.Info("Запущено: " + app.Name + " | " + app.ExecutablePath +
                            (string.IsNullOrEmpty(arguments) ? "" : " | аргументы: " + arguments));

                if (app.LaunchMode == LaunchMode.Minimized)
                {
                    string processName = ResolveProcessName(app);
                    if (!string.IsNullOrEmpty(processName))
                        WindowActivator.ScheduleMinimize(processName, 7000);
                }

                result.Outcome = LaunchOutcome.Launched;
                result.Message = "запущено";
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка запуска " + app.Name, ex);
                result.Outcome = LaunchOutcome.Error;
                result.Message = ex.Message;
                return result;
            }
        }

        private static string[] SplitProcessNames(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new string[0];
            string[] parts = value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> names = new List<string>();
            foreach (string part in parts)
                AddUnique(names, part);
            return names.ToArray();
        }

        private static void AddUnique(List<string> names, string value)
        {
            if (names == null || string.IsNullOrEmpty(value))
                return;
            string cleaned = value.Trim();
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
    }
}
