using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamepadLauncherStarter
{
    public sealed class GamepadEventArgs : EventArgs
    {
        public List<GamepadDevice> Devices { get; private set; }
        public bool Simulated { get; private set; }
        public bool Startup { get; private set; }

        public GamepadEventArgs(List<GamepadDevice> devices, bool simulated, bool startup)
        {
            Devices = devices ?? new List<GamepadDevice>();
            Simulated = simulated;
            Startup = startup;
        }
    }

    public sealed class GamepadWatcher : IDisposable
    {
        private readonly Func<AppSettings> _getSettings;
        private readonly object _sync = new object();
        private readonly Dictionary<string, GamepadDevice> _snapshot = new Dictionary<string, GamepadDevice>();
        private readonly Dictionary<string, DateTime> _lastTrigger = new Dictionary<string, DateTime>();
        private readonly Timer _pollTimer;
        private readonly Timer _debounceTimer;
        private readonly Timer _startupTimer;

        private DeviceNotificationForm _notificationForm;
        private bool _started;
        private bool _disposed;
        private string _lastScanText = "ещё не выполнялось";

        public event EventHandler<GamepadEventArgs> GamepadArrived;
        public event EventHandler DevicesChanged;

        public GamepadWatcher(Func<AppSettings> getSettings)
        {
            _getSettings = getSettings;

            _pollTimer = new Timer();
            _pollTimer.Interval = 2000;
            _pollTimer.Tick += OnPollTick;

            _debounceTimer = new Timer();
            _debounceTimer.Interval = 700;
            _debounceTimer.Tick += OnDebounceTick;

            _startupTimer = new Timer();
            _startupTimer.Interval = 2500;
            _startupTimer.Tick += OnStartupTick;
        }

        public string LastScanText
        {
            get { lock (_sync) { return _lastScanText; } }
        }

        public List<GamepadDevice> Current
        {
            get
            {
                lock (_sync)
                {
                    List<GamepadDevice> result = new List<GamepadDevice>();
                    foreach (GamepadDevice device in _snapshot.Values)
                        result.Add(device.Clone());
                    return result;
                }
            }
        }

        public bool IsRunning
        {
            get { return _started && !_disposed; }
        }

        public void Start()
        {
            if (_started || _disposed)
                return;
            _started = true;

            _notificationForm = new DeviceNotificationForm(this);
            Scan(false);
            _pollTimer.Start();

            bool hasDevices;
            lock (_sync)
            {
                hasDevices = _snapshot.Count > 0;
            }
            if (hasDevices)
            {
                Logger.Info("При запуске уже подключены геймпады: " + DescribeDevices(Current));
                _startupTimer.Start();
            }
            else
            {
                Logger.Info("Геймпады не обнаружены. Ожидание подключения.");
            }
        }

        public void RescanNow()
        {
            if (_disposed)
                return;
            Scan(false);
            RaiseDevicesChanged();
        }

        public void ScheduleScan()
        {
            if (_disposed)
                return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        public static bool IsIgnored(GamepadDevice device, AppSettings settings)
        {
            if (device == null || settings == null)
                return false;
            foreach (IgnoredDevice ignored in settings.IgnoredDevices)
            {
                if (ignored != null && !string.IsNullOrEmpty(ignored.Guid) &&
                    string.Equals(ignored.Guid, device.InstanceGuid, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static List<GamepadDevice> FilterActive(List<GamepadDevice> devices, AppSettings settings)
        {
            List<GamepadDevice> result = new List<GamepadDevice>();
            if (devices == null)
                return result;
            foreach (GamepadDevice device in devices)
            {
                if (!IsIgnored(device, settings))
                    result.Add(device);
            }
            return result;
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            Scan(true);
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            Scan(true);
        }

        private void OnStartupTick(object sender, EventArgs e)
        {
            _startupTimer.Stop();
            AppSettings settings = _getSettings();
            if (settings == null || !settings.LaunchOnStartIfConnected)
            {
                Logger.Info("Геймпад был подключён при старте, но запуск при старте отключён в настройках.");
                return;
            }

            List<GamepadDevice> present = FilterActive(Current, settings);
            if (present.Count == 0)
                return;

            Logger.Info("Запуск при старте: геймпад уже подключён — " + DescribeDevices(present));
            MarkTriggered(present);
            RaiseArrived(present, false, true);
        }

        private void Scan(bool allowTrigger)
        {
            if (_disposed)
                return;

            List<GamepadDevice> found;
            try
            {
                found = DirectInputEnumerator.GetConnectedGamepads();
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось перечислить геймпады", ex);
                found = new List<GamepadDevice>();
            }

            List<GamepadDevice> added = new List<GamepadDevice>();
            List<GamepadDevice> removed = new List<GamepadDevice>();

            lock (_sync)
            {
                Dictionary<string, GamepadDevice> next = new Dictionary<string, GamepadDevice>();
                foreach (GamepadDevice device in found)
                {
                    string key = GetKey(device);
                    if (!next.ContainsKey(key))
                        next[key] = device;
                }

                foreach (KeyValuePair<string, GamepadDevice> pair in next)
                {
                    if (!_snapshot.ContainsKey(pair.Key))
                        added.Add(pair.Value);
                }
                foreach (KeyValuePair<string, GamepadDevice> pair in _snapshot)
                {
                    if (!next.ContainsKey(pair.Key))
                        removed.Add(pair.Value);
                }

                _snapshot.Clear();
                foreach (KeyValuePair<string, GamepadDevice> pair in next)
                    _snapshot[pair.Key] = pair.Value;

                _lastScanText = DateTime.Now.ToString("HH:mm:ss") + ", устройств: " + _snapshot.Count;
            }

            foreach (GamepadDevice device in added)
                Logger.Info("Обнаружен геймпад: " + device.DisplayName + " [" + device.InstanceGuid + "]");
            foreach (GamepadDevice device in removed)
                Logger.Info("Геймпад отключён: " + device.DisplayName + " [" + device.InstanceGuid + "]");

            if (added.Count > 0 || removed.Count > 0)
                RaiseDevicesChanged();

            if (!allowTrigger || added.Count == 0)
                return;

            AppSettings settings = _getSettings();
            if (settings == null)
                return;

            DateTime now = DateTime.Now;
            List<GamepadDevice> eligible = new List<GamepadDevice>();
            foreach (GamepadDevice device in added)
            {
                if (IsIgnored(device, settings))
                {
                    Logger.Info("Геймпад в списке игнорируемых, пропускаю: " + device.DisplayName);
                    continue;
                }

                string key = GetKey(device);
                DateTime last;
                if (_lastTrigger.TryGetValue(key, out last) && (now - last).TotalSeconds < 10)
                {
                    Logger.Info("Повторное событие для устройства в течение 10 секунд, пропускаю: " + device.DisplayName);
                    continue;
                }
                _lastTrigger[key] = now;
                eligible.Add(device);
            }

            if (eligible.Count > 0)
            {
                _startupTimer.Stop();
                RaiseArrived(eligible, false, false);
            }
        }

        private void MarkTriggered(List<GamepadDevice> devices)
        {
            DateTime now = DateTime.Now;
            foreach (GamepadDevice device in devices)
                _lastTrigger[GetKey(device)] = now;
        }

        private void RaiseArrived(List<GamepadDevice> devices, bool simulated, bool startup)
        {
            EventHandler<GamepadEventArgs> handler = GamepadArrived;
            if (handler != null)
                handler(this, new GamepadEventArgs(devices, simulated, startup));
        }

        private void RaiseDevicesChanged()
        {
            EventHandler handler = DevicesChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static string GetKey(GamepadDevice device)
        {
            if (device == null)
                return "";
            if (!string.IsNullOrEmpty(device.InstanceGuid))
                return device.InstanceGuid;
            return (device.ProductName ?? "") + "|" + (device.InstanceName ?? "");
        }

        private static string DescribeDevices(List<GamepadDevice> devices)
        {
            if (devices == null || devices.Count == 0)
                return "нет";
            List<string> names = new List<string>();
            foreach (GamepadDevice device in devices)
                names.Add(device.DisplayName);
            return string.Join(", ", names.ToArray());
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _pollTimer.Stop();
            _debounceTimer.Stop();
            _startupTimer.Stop();
            _pollTimer.Dispose();
            _debounceTimer.Dispose();
            _startupTimer.Dispose();

            if (_notificationForm != null)
            {
                try
                {
                    _notificationForm.Close();
                    _notificationForm.Dispose();
                }
                catch
                {
                }
                _notificationForm = null;
            }
        }

        private sealed class DeviceNotificationForm : Form
        {
            private const int WM_DEVICECHANGE = 0x0219;
            private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
            private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
            private const int WS_EX_NOACTIVATE = 0x08000000;

            private readonly GamepadWatcher _owner;
            private IntPtr _hidNotification;
            private IntPtr _usbNotification;

            public DeviceNotificationForm(GamepadWatcher owner)
            {
                _owner = owner;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
                Opacity = 0;
                Show();
                RegisterNotifications();
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= WS_EX_NOACTIVATE;
                    return parameters;
                }
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WM_DEVICECHANGE)
                    _owner.ScheduleScan();
                base.WndProc(ref message);
            }

            private void RegisterNotifications()
            {
                _hidNotification = Register(new Guid("4D1E55B2-F16F-11CF-88CB-001111000030"));
                _usbNotification = Register(new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED"));
            }

            private IntPtr Register(Guid classGuid)
            {
                try
                {
                    DEV_BROADCAST_DEVICEINTERFACE filter = new DEV_BROADCAST_DEVICEINTERFACE();
                    filter.dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
                    filter.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
                    filter.dbcc_reserved = 0;
                    filter.dbcc_classguid = classGuid;
                    filter.dbcc_name = "";
                    return RegisterDeviceNotification(Handle, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось зарегистрировать уведомления об устройствах: " + ex.Message);
                    return IntPtr.Zero;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (_hidNotification != IntPtr.Zero)
                    {
                        UnregisterDeviceNotification(_hidNotification);
                        _hidNotification = IntPtr.Zero;
                    }
                    if (_usbNotification != IntPtr.Zero)
                    {
                        UnregisterDeviceNotification(_usbNotification);
                        _usbNotification = IntPtr.Zero;
                    }
                }
                base.Dispose(disposing);
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct DEV_BROADCAST_DEVICEINTERFACE
            {
                public int dbcc_size;
                public int dbcc_devicetype;
                public int dbcc_reserved;
                public Guid dbcc_classguid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
                public string dbcc_name;
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr RegisterDeviceNotification(
                IntPtr hRecipient,
                ref DEV_BROADCAST_DEVICEINTERFACE notificationFilter,
                int flags);

            [DllImport("user32.dll")]
            private static extern bool UnregisterDeviceNotification(IntPtr handle);
        }
    }
}
