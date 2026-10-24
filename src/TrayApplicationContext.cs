using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GamepadLauncherStarter
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppRuntime _runtime;
        private readonly bool _smokeTest;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenu _menu;
        private readonly MenuItem _miEnabled;
        private readonly MenuItem _miTrayOnly;
        private readonly MenuItem _miSkipFullscreen;
        private readonly Control _marshaller;
        private readonly EventWaitHandle _showSettingsEvent;
        private RegisteredWaitHandle _showSettingsWait;
        private SettingsForm _settingsForm;
        private bool _exiting;
        private bool _disposed;

        public TrayApplicationContext(bool applySystemChanges, bool smokeTest, bool showSettingsOnStart, EventWaitHandle showSettingsEvent)
        {
            _smokeTest = smokeTest;
            _showSettingsEvent = showSettingsEvent;

            _runtime = new AppRuntime(applySystemChanges);
            if (smokeTest)
            {
                _runtime.Settings.TrayOnly = true;
                _runtime.Settings.ShowNotifications = false;
            }

            _runtime.NotificationRequested += OnNotificationRequested;
            _runtime.StatusChanged += OnRuntimeStatusChanged;

            _marshaller = new Control();
            _marshaller.CreateControl();

            _miEnabled = new MenuItem("Включено", OnToggleEnabled);
            _miTrayOnly = new MenuItem("Только трей (ничего не запускать)", OnToggleTrayOnly);
            _miSkipFullscreen = new MenuItem("Не реагировать в полноэкранном режиме", OnToggleSkipFullscreen);

            MenuItem settingsItem = new MenuItem("Настройки...", delegate { ShowSettings(); });
            settingsItem.DefaultItem = true;
            MenuItem simulateItem = new MenuItem("Запустить приложения сейчас", delegate
            {
                try
                {
                    _runtime.SimulateArrival();
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка ручного запуска", ex);
                }
            });
            MenuItem logItem = new MenuItem("Открыть журнал", delegate { OpenLog(); });
            MenuItem exitItem = new MenuItem("Выход", delegate { RequestExit(); });

            _menu = new ContextMenu(new MenuItem[]
            {
                settingsItem,
                new MenuItem("-"),
                _miEnabled,
                _miTrayOnly,
                _miSkipFullscreen,
                new MenuItem("-"),
                simulateItem,
                logItem,
                new MenuItem("-"),
                exitItem
            });

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = AppIcon.Get();
            _notifyIcon.Text = LimitTrayText("Gamepad Launcher Starter");
            _notifyIcon.ContextMenu = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate(object sender, EventArgs e)
            {
                ShowSettings();
            };

            _runtime.Start();
            UpdateMenuChecks();
            UpdateTrayText();

            if (_showSettingsEvent != null)
            {
                _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(
                    _showSettingsEvent,
                    delegate(object state, bool timedOut)
                    {
                        try
                        {
                            _marshaller.BeginInvoke(new Action(ShowSettings));
                        }
                        catch
                        {
                        }
                    },
                    null,
                    Timeout.Infinite,
                    false);
            }

            if (!smokeTest && (_runtime.IsFirstRun || showSettingsOnStart))
                Application.Idle += ShowSettingsOnIdle;

            if (smokeTest)
                Logger.Info("SMOKE: трей и наблюдатель созданы, запуск приложений отключён.");
        }

        public void SmokeTestForms()
        {
            using (SettingsForm settings = new SettingsForm(_runtime))
            {
                settings.CreateControl();
            }
            using (AppEditForm editor = new AppEditForm(AppCatalog.CreateSteam()))
            {
                editor.CreateControl();
            }
            Logger.Info("SMOKE: окна настроек и редактирования созданы без ошибок.");
        }

        private void ShowSettingsOnIdle(object sender, EventArgs e)
        {
            Application.Idle -= ShowSettingsOnIdle;
            ShowSettings();
        }

        private void ShowSettings()
        {
            if (_disposed || _exiting)
                return;
            if (_smokeTest)
                return;

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return;
            }

            SettingsForm form = new SettingsForm(_runtime);
            _settingsForm = form;
            try
            {
                DialogResult result = form.ShowDialog();
                if (result == DialogResult.OK && form.ResultSettings != null)
                    _runtime.ApplySettings(form.ResultSettings);
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка окна настроек", ex);
            }
            finally
            {
                try
                {
                    form.Dispose();
                }
                catch
                {
                }
                _settingsForm = null;
                UpdateMenuChecks();
                UpdateTrayText();
            }
        }

        private void OnNotificationRequested(object sender, NotificationEventArgs e)
        {
            if (_notifyIcon == null || e == null || _disposed)
                return;
            try
            {
                _notifyIcon.BalloonTipTitle = e.Title ?? AppInfo.ProductName;
                _notifyIcon.BalloonTipText = e.Text ?? "";
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(5000);
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось показать уведомление: " + ex.Message);
            }
        }

        private void OnRuntimeStatusChanged(object sender, EventArgs e)
        {
            if (_disposed)
                return;
            UpdateTrayText();
        }

        private void OnToggleEnabled(object sender, EventArgs e)
        {
            _runtime.Settings.Enabled = !_runtime.Settings.Enabled;
            _runtime.SaveSettings();
            UpdateMenuChecks();
            UpdateTrayText();
            Logger.Info("Отслеживание " + (_runtime.Settings.Enabled ? "включено" : "выключено") + " через трей.");
        }

        private void OnToggleTrayOnly(object sender, EventArgs e)
        {
            _runtime.Settings.TrayOnly = !_runtime.Settings.TrayOnly;
            _runtime.SaveSettings();
            UpdateMenuChecks();
            UpdateTrayText();
            Logger.Info("Режим «Только трей» " + (_runtime.Settings.TrayOnly ? "включён" : "выключен") + ".");
        }

        private void OnToggleSkipFullscreen(object sender, EventArgs e)
        {
            _runtime.Settings.SkipWhenFullscreenGame = !_runtime.Settings.SkipWhenFullscreenGame;
            _runtime.SaveSettings();
            UpdateMenuChecks();
            Logger.Info("Пропуск в полноэкранном режиме " +
                        (_runtime.Settings.SkipWhenFullscreenGame ? "включён" : "выключен") + ".");
        }

        private void UpdateMenuChecks()
        {
            if (_miEnabled != null)
                _miEnabled.Checked = _runtime.Settings.Enabled;
            if (_miTrayOnly != null)
                _miTrayOnly.Checked = _runtime.Settings.TrayOnly;
            if (_miSkipFullscreen != null)
                _miSkipFullscreen.Checked = _runtime.Settings.SkipWhenFullscreenGame;
        }

        private void UpdateTrayText()
        {
            if (_notifyIcon == null || _disposed)
                return;
            try
            {
                _notifyIcon.Text = LimitTrayText(_runtime.GetStatusText());
            }
            catch
            {
            }
        }

        private static string LimitTrayText(string text)
        {
            if (string.IsNullOrEmpty(text))
                text = AppInfo.ProductName;
            if (text.Length <= 63)
                return text;
            return text.Substring(0, 60) + "...";
        }

        private void OpenLog()
        {
            try
            {
                if (!string.IsNullOrEmpty(Logger.LogPath) && File.Exists(Logger.LogPath))
                    Process.Start("notepad.exe", Logger.LogPath);
                else
                {
                    AppPaths.EnsureDataDirectory();
                    Process.Start("explorer.exe", AppPaths.DataDirectory);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось открыть журнал", ex);
            }
        }

        public void RequestExit()
        {
            if (_exiting)
                return;
            _exiting = true;
            Logger.Info("Завершение работы.");
            try
            {
                if (_showSettingsWait != null)
                {
                    _showSettingsWait.Unregister(null);
                    _showSettingsWait = null;
                }
            }
            catch
            {
            }
            try
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                    _settingsForm.Close();
            }
            catch
            {
            }
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch
            {
            }
            try
            {
                _runtime.Dispose();
            }
            catch
            {
            }
            try
            {
                _marshaller.Dispose();
            }
            catch
            {
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (!_exiting)
                    RequestExit();
            }
            base.Dispose(disposing);
        }
    }
}
