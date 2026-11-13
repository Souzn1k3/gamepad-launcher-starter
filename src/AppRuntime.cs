using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GamepadLauncherStarter
{
    public sealed class NotificationEventArgs : EventArgs
    {
        public string Title { get; private set; }
        public string Text { get; private set; }

        public NotificationEventArgs(string title, string text)
        {
            Title = title;
            Text = text;
        }
    }

    public sealed class AppRuntime : IDisposable
    {
        private readonly bool _applySystemChanges;
        private GamepadWatcher _watcher;
        private Action<Action> _uiMarshaller;
        private SynchronizationContext _uiContext;
        private int _guardCheckInProgress;
        private bool _disposed;

        public AppSettings Settings { get; private set; }
        public bool IsFirstRun { get; private set; }

        public GamepadWatcher Watcher
        {
            get { return _watcher; }
        }

        public event EventHandler<NotificationEventArgs> NotificationRequested;
        public event EventHandler StatusChanged;

        public AppRuntime(bool applySystemChanges)
        {
            _applySystemChanges = applySystemChanges;
            Logger.Initialize();
            IsFirstRun = !File.Exists(AppPaths.SettingsFile);
            Settings = SettingsStore.Load();
            AppCatalog.AutodetectPaths(Settings.Apps);

            if (_applySystemChanges)
            {
                AutostartManager.SyncSteamBackupFile(Settings);
                AutostartManager.ApplyOurAutostart(Settings.StartWithWindows);
                if (Settings.ManageSteamAutostart)
                    AutostartManager.ManageSteamAutostart(Settings);
                SettingsStore.Save(Settings);
            }

            if (IsFirstRun)
                Logger.Info("Первый запуск: созданы настройки по умолчанию.");
        }

        public void Start()
        {
            Start(null);
        }

        public void Start(Action<Action> uiMarshaller)
        {
            if (_watcher != null || _disposed)
                return;
            _uiMarshaller = uiMarshaller;
            _uiContext = SynchronizationContext.Current;
            _watcher = new GamepadWatcher(delegate { return Settings; });
            _watcher.GamepadArrived += OnGamepadArrived;
            _watcher.DevicesChanged += OnDevicesChanged;
            _watcher.Start();
            RaiseStatus();
        }

        public void ApplySettings(AppSettings updated)
        {
            if (updated == null || _disposed)
                return;

            bool oldManageSteam = Settings.ManageSteamAutostart;
            bool oldStartWithWindows = Settings.StartWithWindows;

            Settings = updated;
            AppCatalog.AutodetectPaths(Settings.Apps);

            if (_applySystemChanges)
            {
                if (oldManageSteam && !Settings.ManageSteamAutostart)
                {
                    AutostartManager.RestoreSteamAutostart(Settings);
                    Settings.SteamAutostartChecked = false;
                }
                else if (!oldManageSteam && Settings.ManageSteamAutostart && !Settings.SteamAutostartChecked)
                {
                    AutostartManager.ManageSteamAutostart(Settings);
                }

                if (oldStartWithWindows != Settings.StartWithWindows)
                    Logger.Info("Автозапуск программы: " + (Settings.StartWithWindows ? "включён" : "выключен"));
                AutostartManager.ApplyOurAutostart(Settings.StartWithWindows);
                AutostartManager.SyncSteamBackupFile(Settings);
                SettingsStore.Save(Settings);
            }

            if (_watcher != null)
                _watcher.RescanNow();
            RaiseStatus();
            Logger.Info("Настройки применены.");
        }

        public void SaveSettings()
        {
            if (_applySystemChanges)
                SettingsStore.Save(Settings);
            RaiseStatus();
        }

        public void SimulateArrival()
        {
            Trigger(GetSimulatedDevices(), true, false, null);
        }

        public void SimulateArrival(AppSettings overrideSettings)
        {
            Trigger(GetSimulatedDevices(), true, false, overrideSettings);
        }

        public string GetStatusText()
        {
            if (Settings == null)
                return AppInfo.ProductName;
            if (!Settings.Enabled)
                return "Отключено";
            if (Settings.TrayOnly)
                return "Только трей";
            List<GamepadDevice> devices = _watcher != null ? _watcher.Current : new List<GamepadDevice>();
            if (devices.Count == 0)
                return "Ожидание геймпада";
            List<string> names = new List<string>();
            foreach (GamepadDevice device in devices)
                names.Add(device.DisplayName);
            return "Геймпад: " + string.Join(", ", names.ToArray());
        }

        public string BuildDiagnostics()
        {
            return Diagnostics.BuildReport(
                Settings,
                _watcher != null ? _watcher.Current : null,
                _watcher != null,
                _watcher != null ? _watcher.LastScanText : "наблюдатель не запущен");
        }

        private void OnGamepadArrived(object sender, GamepadEventArgs e)
        {
            Trigger(e.Devices, e.Simulated, e.Startup, null);
        }

        private void OnDevicesChanged(object sender, EventArgs e)
        {
            RaiseStatus();
        }

        private List<GamepadDevice> GetSimulatedDevices()
        {
            List<GamepadDevice> devices = _watcher != null ? _watcher.Current : new List<GamepadDevice>();
            if (devices.Count == 0)
            {
                GamepadDevice fake = new GamepadDevice();
                fake.InstanceGuid = "simulated";
                fake.InstanceName = "Тестовый геймпад";
                fake.ProductName = "Тестовый геймпад";
                devices.Add(fake);
            }
            return devices;
        }

        private void Trigger(List<GamepadDevice> devices, bool simulated, bool startup, AppSettings overrideSettings)
        {
            AppSettings settings = overrideSettings != null ? overrideSettings : Settings;
            try
            {
                string deviceText = DescribeDevices(devices);
                string prefix = simulated ? "Симуляция подключения" : (startup ? "Геймпад уже подключён при старте" : "Подключён геймпад");
                Logger.Info(prefix + ": " + deviceText);

                if (!settings.Enabled)
                {
                    Logger.Info("Отслеживание отключено в настройках — действий нет.");
                    if (simulated)
                        RaiseNotification(settings, "Симуляция", "Отслеживание отключено в настройках.");
                    return;
                }

                if (!simulated && settings.SkipWhenFullscreenGame)
                {
                    BeginGuardCheck(settings, deviceText);
                    return;
                }

                ContinueTrigger(settings, deviceText);
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка обработки подключения геймпада", ex);
                RaiseNotification(settings, "Ошибка", "Не удалось обработать подключение геймпада: " + ex.Message);
            }
        }

        private void BeginGuardCheck(AppSettings settings, string deviceText)
        {
            if (Interlocked.CompareExchange(ref _guardCheckInProgress, 1, 0) != 0)
            {
                Logger.Info("Проверка запущенной игры уже выполняется — пропускаю дублирующее событие.");
                return;
            }

            try
            {
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    RunningGameInfo game = null;
                    string error = null;
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        game = RunningGameDetector.Detect();
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        Logger.Error("Ошибка фоновой проверки запущенной игры", ex);
                    }
                    stopwatch.Stop();

                    PostToUi(delegate
                    {
                        Interlocked.Exchange(ref _guardCheckInProgress, 0);
                        if (_disposed)
                            return;

                        if (!string.IsNullOrEmpty(error))
                            Logger.Warn("Проверка запущенной игры завершилась ошибкой: " + error + ". Продолжаю без блокировки.");

                        if (game != null && game.IsGameRunning)
                        {
                            if (Settings.SkipWhenFullscreenGame)
                            {
                                Logger.Info("Обнаружена запущенная игра: " + game.Describe() +
                                            ". Подключение геймпада пропущено (настройка), приложения не запускались. Проверка: " +
                                            stopwatch.ElapsedMilliseconds + " мс.");
                                return;
                            }

                            Logger.Info("Обнаружена запущенная игра: " + game.Describe() +
                                        ", но защита выключена в настройках — продолжаю. Проверка: " +
                                        stopwatch.ElapsedMilliseconds + " мс.");
                        }
                        else
                        {
                            Logger.Info("Проверка запущенной игры: не обнаружено (" + stopwatch.ElapsedMilliseconds + " мс).");
                        }
                        ContinueTrigger(settings, deviceText);
                    });
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _guardCheckInProgress, 0);
                Logger.Error("Не удалось запустить фоновую проверку игры", ex);
                ContinueTrigger(settings, deviceText);
            }
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;
            try
            {
                Action<Action> marshaller = _uiMarshaller;
                if (marshaller != null)
                {
                    marshaller(action);
                    return;
                }

                SynchronizationContext context = _uiContext;
                if (context != null)
                {
                    context.Post(delegate(object state) { action(); }, null);
                    return;
                }

                Logger.Warn("UI-контекст недоступен — результат фоновой проверки не будет применён.");
                Interlocked.Exchange(ref _guardCheckInProgress, 0);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось вернуться в UI-поток", ex);
                Interlocked.Exchange(ref _guardCheckInProgress, 0);
            }
        }

        private void ContinueTrigger(AppSettings settings, string deviceText)
        {
            try
            {
                if (settings.TrayOnly)
                {
                    Logger.Info("Режим «Только трей»: приложения не запускаются.");
                    RaiseNotification(settings, "Геймпад подключён", deviceText + ". Режим «Только трей»: приложения не запускаются.");
                    return;
                }

                List<AppEntry> apps = new List<AppEntry>();
                foreach (AppEntry app in settings.Apps)
                {
                    if (app != null && app.Enabled)
                        apps.Add(app);
                }

                if (apps.Count == 0)
                {
                    Logger.Info("Не выбрано ни одного приложения.");
                    RaiseNotification(settings, "Геймпад подключён", deviceText + ". Не выбрано ни одного приложения.");
                    return;
                }

                if (settings.PartialMode == PartialLaunchMode.DoNothingIfAnyRunning)
                {
                    List<string> running = new List<string>();
                    foreach (AppEntry app in apps)
                    {
                        if (LauncherService.IsRunning(app))
                            running.Add(app.Name);
                    }
                    if (running.Count > 0)
                    {
                        string reason = "Уже запущено: " + string.Join(", ", running.ToArray()) +
                                        ". По настройке ничего не запускаю.";
                        Logger.Info("Частичный запуск: " + reason);
                        RaiseNotification(settings, "Геймпад подключён", deviceText + ". " + reason);
                        return;
                    }
                }

                StringBuilder summary = new StringBuilder();
                foreach (AppEntry app in apps)
                {
                    LaunchResult result = LauncherService.LaunchOrShow(app);
                    summary.AppendLine(app.Name + ": " + DescribeOutcome(result));
                }

                string summaryOneLine = summary.ToString().Trim().Replace(Environment.NewLine, "; ");
                Logger.Info("Результат: " + summaryOneLine);
                RaiseNotification(settings, "Геймпад подключён", deviceText + ". " + summaryOneLine);
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка обработки подключения геймпада", ex);
                RaiseNotification(settings, "Ошибка", "Не удалось обработать подключение геймпада: " + ex.Message);
            }
        }

        private static string DescribeDevices(List<GamepadDevice> devices)
        {
            if (devices == null || devices.Count == 0)
                return "геймпад";
            List<string> names = new List<string>();
            foreach (GamepadDevice device in devices)
                names.Add(device.DisplayName);
            return string.Join(", ", names.ToArray());
        }

        private static string DescribeOutcome(LaunchResult result)
        {
            if (result == null)
                return "пропущено";
            switch (result.Outcome)
            {
                case LaunchOutcome.Launched:
                    return "запущено";
                case LaunchOutcome.AlreadyRunningShown:
                    return "уже работает, окно развёрнуто";
                case LaunchOutcome.AlreadyRunningSkipped:
                    return "уже работает";
                case LaunchOutcome.Missing:
                    return "не найден (" + result.Message + ")";
                case LaunchOutcome.Error:
                    return "ошибка: " + result.Message;
                default:
                    return "пропущено";
            }
        }

        private void RaiseNotification(AppSettings settings, string title, string text)
        {
            if (settings == null || !settings.ShowNotifications)
                return;
            if (text == null)
                text = "";
            if (text.Length > 240)
                text = text.Substring(0, 237) + "...";
            EventHandler<NotificationEventArgs> handler = NotificationRequested;
            if (handler != null)
                handler(this, new NotificationEventArgs(title, text));
            else
                Logger.Info("Уведомление: " + title + " — " + text);
        }

        private void RaiseStatus()
        {
            EventHandler handler = StatusChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_watcher != null)
            {
                _watcher.GamepadArrived -= OnGamepadArrived;
                _watcher.DevicesChanged -= OnDevicesChanged;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
