using System;
using System.Threading;
using System.Windows.Forms;

namespace GamepadLauncherStarter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Logger.Initialize();

            if (HasArg(args, "--diagnose"))
            {
                Diagnostics.RunAndSave();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Logger.Error("Необработанное исключение интерфейса", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Logger.Error("Необработанное исключение", e.ExceptionObject as Exception);
            };

            bool smokeTest = HasArg(args, "--smoke-test");
            bool showSettings = HasArg(args, "--settings");

            if (smokeTest)
            {
                RunSmokeTest();
                return;
            }

            using (Mutex mutex = new Mutex(false, AppInfo.MutexName))
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    SignalExistingInstance();
                    return;
                }

                try
                {
                    using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowSettingsEventName))
                    using (TrayApplicationContext context = new TrayApplicationContext(true, false, showSettings, showEvent))
                    {
                        Application.Run(context);
                    }
                }
                finally
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void RunSmokeTest()
        {
            Logger.Info("SMOKE: старт.");
            using (TrayApplicationContext context = new TrayApplicationContext(false, true, false, null))
            {
                try
                {
                    context.SmokeTestForms();
                }
                catch (Exception ex)
                {
                    Logger.Error("SMOKE: ошибка создания форм", ex);
                    Environment.ExitCode = 2;
                }

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 2500;
                timer.Tick += delegate
                {
                    timer.Stop();
                    timer.Dispose();
                    context.RequestExit();
                };
                timer.Start();
                Application.Run(context);
            }
            Logger.Info("SMOKE: завершено.");
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle existing = EventWaitHandle.OpenExisting(AppInfo.ShowSettingsEventName))
                {
                    existing.Set();
                    return;
                }
            }
            catch
            {
            }
            MessageBox.Show(
                "Gamepad Launcher Starter уже запущен. Иконка программы находится в области уведомлений.",
                AppInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null)
                return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
