using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GamepadLauncherStarter
{
    public sealed class ForegroundWindowInfo
    {
        public IntPtr Handle;
        public string Title = "";
        public string ProcessName = "";

        public string Describe()
        {
            string title = string.IsNullOrEmpty(Title) ? "(без заголовка)" : Title;
            if (string.IsNullOrEmpty(ProcessName))
                return "\"" + title + "\"";
            return "\"" + title + "\" (" + ProcessName + ")";
        }
    }

    public static class FullscreenDetector
    {
        private const int QUNS_NOT_PRESENT = 1;
        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;
        private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        private const int QUNS_QUIET_TIME = 6;
        private const int QUNS_APP = 7;

        public static bool IsFullscreenAppActive()
        {
            int state;
            return TryGetState(out state) && IsFullscreenState(state);
        }

        public static bool TryGetState(out int state)
        {
            state = 0;
            try
            {
                int result = SHQueryUserNotificationState(out state);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static string GetStateText()
        {
            int state;
            if (!TryGetState(out state))
                return "не удалось определить";
            switch (state)
            {
                case QUNS_NOT_PRESENT:
                    return "сессия заблокирована или экран выключен";
                case QUNS_BUSY:
                    return "полноэкранное приложение";
                case QUNS_RUNNING_D3D_FULL_SCREEN:
                    return "игра в эксклюзивном полноэкранном режиме";
                case QUNS_PRESENTATION_MODE:
                    return "режим презентации";
                case QUNS_ACCEPTS_NOTIFICATIONS:
                    return "обычный рабочий стол";
                case QUNS_QUIET_TIME:
                    return "режим тишины";
                case QUNS_APP:
                    return "полноэкранное приложение Windows";
                default:
                    return "состояние " + state;
            }
        }

        public static ForegroundWindowInfo GetForegroundInfo()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return null;

                uint processId;
                GetWindowThreadProcessId(handle, out processId);

                StringBuilder title = new StringBuilder(512);
                GetWindowText(handle, title, title.Capacity);

                string processName = "";
                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                        processName = process.ProcessName;
                }
                catch
                {
                }

                ForegroundWindowInfo info = new ForegroundWindowInfo();
                info.Handle = handle;
                info.Title = title.ToString();
                info.ProcessName = processName;
                return info;
            }
            catch
            {
                return null;
            }
        }

        public static string GetForegroundDescription()
        {
            ForegroundWindowInfo info = GetForegroundInfo();
            return info == null ? "не удалось определить" : info.Describe();
        }

        private static bool IsFullscreenState(int state)
        {
            return state == QUNS_BUSY || state == QUNS_RUNNING_D3D_FULL_SCREEN ||
                   state == QUNS_PRESENTATION_MODE || state == QUNS_APP;
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);
    }
}
