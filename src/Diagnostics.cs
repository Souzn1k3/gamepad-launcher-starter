using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GamepadLauncherStarter
{
    public static class Diagnostics
    {
        public static void RunAndSave()
        {
            try
            {
                Logger.Initialize();
                AppSettings settings = SettingsStore.Load();
                AppCatalog.AutodetectPaths(settings.Apps);
                string report = BuildReport(settings, null, false, "диагностический режим");
                AppPaths.EnsureDataDirectory();
                File.WriteAllText(AppPaths.DiagnosticsFile, report, new UTF8Encoding(false));
                Logger.Info("Диагностический отчёт сохранён: " + AppPaths.DiagnosticsFile);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось сформировать диагностический отчёт", ex);
                Environment.ExitCode = 1;
            }
        }

        public static string BuildReport(AppSettings settings, List<GamepadDevice> currentDevices, bool watcherRunning, string lastScanText)
        {
            StringBuilder report = new StringBuilder();
            try
            {
                report.AppendLine("Gamepad Launcher Starter — диагностика");
                report.AppendLine("Время: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("Версия: " + AppInfo.Version);
                report.AppendLine("Exe: " + AppInfo.ExePath);
                report.AppendLine("Windows: " + Environment.OSVersion.VersionString + ", 64-bit: " + Environment.Is64BitOperatingSystem);
                report.AppendLine("Папка данных: " + AppPaths.DataDirectory);
                report.AppendLine("Настройки: " + AppPaths.SettingsFile + (File.Exists(AppPaths.SettingsFile) ? " (есть)" : " (нет)"));
                report.AppendLine("Журнал: " + AppPaths.LogFile);
                if (!string.IsNullOrEmpty(Logger.InitError))
                    report.AppendLine("Ошибка журнала: " + Logger.InitError);
                report.AppendLine();

                if (settings == null)
                {
                    report.AppendLine("Настройки: не загружены");
                }
                else
                {
                    report.AppendLine("=== Настройки ===");
                    report.AppendLine("Отслеживание включено: " + settings.Enabled);
                    report.AppendLine("Только трей: " + settings.TrayOnly);
                    report.AppendLine("Уведомления: " + settings.ShowNotifications);
                    report.AppendLine("Запуск при старте с геймпадом: " + settings.LaunchOnStartIfConnected);
                    report.AppendLine("Защита во время игры: " + settings.SkipWhenFullscreenGame);
                    report.AppendLine("Режим частичного запуска: " + settings.PartialMode);
                    report.AppendLine("Автозапуск программы: " + settings.StartWithWindows);
                    report.AppendLine("Управление автозапуском Steam: " + settings.ManageSteamAutostart +
                                      " (проверено: " + settings.SteamAutostartChecked + ")");
                    report.AppendLine("Резервная копия автозапуска Steam: " +
                                      (string.IsNullOrEmpty(settings.SteamAutostartBackup) ? "нет" : settings.SteamAutostartBackup));
                    report.AppendLine();

                    report.AppendLine("=== Приложения ===");
                    if (settings.Apps.Count == 0)
                    {
                        report.AppendLine("Список пуст.");
                    }
                    else
                    {
                        foreach (AppEntry app in settings.Apps)
                        {
                            if (app == null)
                                continue;
                            bool exists = !string.IsNullOrEmpty(app.ExecutablePath) && File.Exists(app.ExecutablePath);
                            bool running = false;
                            try
                            {
                                running = LauncherService.IsRunning(app);
                            }
                            catch
                            {
                            }
                            report.AppendLine("- " + app.Name + " [" + app.Id + "]");
                            report.AppendLine("    включено: " + app.Enabled + "; встроенное: " + app.BuiltIn);
                            report.AppendLine("    путь: " + (string.IsNullOrEmpty(app.ExecutablePath) ? "(не задан)" : app.ExecutablePath) +
                                              "; существует: " + exists);
                            report.AppendLine("    процесс: " + LauncherService.ResolveProcessName(app) + "; запущен: " + running);
                            report.AppendLine("    режим: " + app.LaunchMode + "; разворачивать окно: " + app.ShowWindowIfRunning);
                            report.AppendLine("    аргументы: " + (app.Arguments ?? ""));
                            report.AppendLine("    аргументы (свёрнуто): " + (app.MinimizedArguments ?? ""));
                        }
                    }
                    report.AppendLine();
                }

                report.AppendLine("=== Автозапуск ===");
                report.AppendLine("HKCU Run\\" + AutostartManager.OurValueName + ": " + SafeValue(AutostartManager.ReadRunValue(AutostartManager.OurValueName)));
                report.AppendLine("Ожидаемая команда: " + AutostartManager.GetOurCommand());
                report.AppendLine("HKCU Run\\" + AutostartManager.SteamValueName + ": " + SafeValue(AutostartManager.ReadRunValue(AutostartManager.SteamValueName)));
                string backupFile = AutostartManager.ReadSteamBackupFile();
                report.AppendLine("Файл резервной копии Steam: " +
                                  (string.IsNullOrEmpty(backupFile) ? "нет" : AppPaths.SteamBackupFile + " -> " + backupFile));
                report.AppendLine();

                report.AppendLine("=== Защита во время игры ===");
                if (settings == null)
                {
                    report.AppendLine("Настройки не загружены.");
                }
                else
                {
                    report.AppendLine("Включена: " + settings.SkipWhenFullscreenGame);
                    Stopwatch rootsWatch = Stopwatch.StartNew();
                    List<GameRoot> gameRoots = RunningGameDetector.CollectGameRoots(true);
                    rootsWatch.Stop();
                    report.AppendLine("Найдено игровых папок: " + gameRoots.Count + " (сбор: " + rootsWatch.ElapsedMilliseconds + " мс)");
                    foreach (GameRoot root in gameRoots)
                    {
                        string displayName = string.IsNullOrEmpty(root.DisplayName)
                            ? Path.GetFileName(root.Path)
                            : root.DisplayName;
                        report.AppendLine("  - [" + root.Source + "] " + displayName + " -> " + root.Path);
                    }
                    Stopwatch detectionWatch = Stopwatch.StartNew();
                    RunningGameInfo runningGame = RunningGameDetector.Detect(gameRoots);
                    detectionWatch.Stop();
                    report.AppendLine("Время проверки: " + detectionWatch.ElapsedMilliseconds + " мс");
                    report.AppendLine("Игра запущена: " + (runningGame.IsGameRunning ? "да" : "нет"));
                    if (runningGame.IsGameRunning)
                    {
                        report.AppendLine("  Игра: " + runningGame.DisplayName);
                        report.AppendLine("  Процесс: " + runningGame.ProcessName);
                        report.AppendLine("  Источник: " + runningGame.Source);
                        report.AppendLine("  Путь: " + runningGame.ExecutablePath);
                    }
                }
                report.AppendLine();

                report.AppendLine("=== Наблюдатель ===");
                report.AppendLine("Запущен: " + watcherRunning);
                report.AppendLine("Последнее сканирование: " + (lastScanText ?? ""));
                if (currentDevices != null)
                {
                    report.AppendLine("Устройства в наблюдателе (" + currentDevices.Count + "):");
                    foreach (GamepadDevice device in currentDevices)
                        report.AppendLine("  - " + device.DisplayName + " [" + device.InstanceGuid + "]");
                }

                List<GamepadDevice> freshDevices = DirectInputEnumerator.GetConnectedGamepads();
                report.AppendLine("Свежее перечисление DirectInput (" + freshDevices.Count + "):");
                foreach (GamepadDevice device in freshDevices)
                {
                    report.AppendLine("  - " + device.DisplayName);
                    report.AppendLine("      instance guid: " + device.InstanceGuid);
                    report.AppendLine("      product guid:  " + device.ProductGuid);
                    report.AppendLine("      instance name: " + device.InstanceName);
                    report.AppendLine("      type: 0x" + device.DeviceType.ToString("X8"));
                }
                if (!string.IsNullOrEmpty(DirectInputEnumerator.LastError))
                    report.AppendLine("Ошибка DirectInput: " + DirectInputEnumerator.LastError);
                report.AppendLine();

                report.AppendLine("=== Проверка сериализации настроек ===");
                try
                {
                    AppSettings clone = SettingsStore.Clone(settings);
                    report.AppendLine("Клонирование настроек: OK, приложений: " + (clone == null ? -1 : clone.Apps.Count));
                }
                catch (Exception cloneError)
                {
                    report.AppendLine("Клонирование настроек: ОШИБКА: " + cloneError.Message);
                }
                report.AppendLine();

                report.AppendLine("=== Система ===");
                int fullscreenState;
                string stateCode = FullscreenDetector.TryGetState(out fullscreenState)
                    ? fullscreenState.ToString()
                    : "?";
                report.AppendLine("Полноэкранное состояние: " + FullscreenDetector.GetStateText() +
                                  " (код " + stateCode + ", " +
                                  (FullscreenDetector.IsFullscreenAppActive() ? "пропуск активен" : "пропуска нет") + ")");
                report.AppendLine("Геометрический полноэкранный режим: " +
                                  (FullscreenDetector.IsForegroundWindowFullscreen() ? "да" : "нет"));
                report.AppendLine("Активное окно: " + FullscreenDetector.GetForegroundDescription());
                report.AppendLine("Конец отчёта.");
            }
            catch (Exception ex)
            {
                report.AppendLine("Ошибка формирования отчёта: " + ex);
            }
            return report.ToString();
        }

        private static string SafeValue(string value)
        {
            return string.IsNullOrEmpty(value) ? "(нет)" : value;
        }
    }
}
