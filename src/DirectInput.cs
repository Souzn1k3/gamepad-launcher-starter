using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GamepadLauncherStarter
{
    public sealed class GamepadDevice
    {
        public string InstanceGuid = "";
        public string ProductGuid = "";
        public string InstanceName = "";
        public string ProductName = "";
        public uint DeviceType;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(ProductName))
                    return ProductName;
                if (!string.IsNullOrEmpty(InstanceName))
                    return InstanceName;
                return "Неизвестный геймпад";
            }
        }

        public GamepadDevice Clone()
        {
            return (GamepadDevice)MemberwiseClone();
        }
    }

    public static class DirectInputEnumerator
    {
        private const int DirectInputVersion = 0x0800;
        private const uint DI8DEVCLASS_GAMECTRL = 4;
        private const uint DIEDFL_ATTACHEDONLY = 0x00000001;

        private static string _lastError = "";

        public static string LastError
        {
            get { return _lastError; }
        }

        public static List<GamepadDevice> GetConnectedGamepads()
        {
            _lastError = "";
            List<GamepadDevice> devices = new List<GamepadDevice>();
            IDirectInput8 directInput = null;
            try
            {
                Guid iid = typeof(IDirectInput8).GUID;
                IntPtr hInstance = GetModuleHandle(null);
                int hr = DirectInput8Create(hInstance, DirectInputVersion, ref iid, out directInput, IntPtr.Zero);
                if (hr != 0 || directInput == null)
                {
                    _lastError = "DirectInput8Create вернул HRESULT 0x" + hr.ToString("X8");
                    Logger.Error(_lastError);
                    return devices;
                }

                DIEnumDevicesCallback callback = delegate(IntPtr instancePtr, IntPtr referencePtr)
                {
                    try
                    {
                        DIDEVICEINSTANCE instance = (DIDEVICEINSTANCE)Marshal.PtrToStructure(instancePtr, typeof(DIDEVICEINSTANCE));
                        GamepadDevice device = new GamepadDevice();
                        device.InstanceGuid = instance.guidInstance.ToString("D");
                        device.ProductGuid = instance.guidProduct.ToString("D");
                        device.InstanceName = instance.tszInstanceName == null ? "" : instance.tszInstanceName;
                        device.ProductName = instance.tszProductName == null ? "" : instance.tszProductName;
                        device.DeviceType = (uint)instance.dwDevType;
                        devices.Add(device);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Ошибка чтения информации об устройстве DirectInput", ex);
                    }
                    return true;
                };

                IntPtr callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
                int enumResult = directInput.EnumDevices(DI8DEVCLASS_GAMECTRL, callbackPointer, IntPtr.Zero, DIEDFL_ATTACHEDONLY);
                GC.KeepAlive(callback);
                if (enumResult != 0)
                {
                    _lastError = "IDirectInput8.EnumDevices вернул HRESULT 0x" + enumResult.ToString("X8");
                    Logger.Error(_lastError);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Logger.Error("Ошибка перечисления DirectInput", ex);
            }
            finally
            {
                if (directInput != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(directInput);
                    }
                    catch
                    {
                    }
                }
            }
            return devices;
        }

        [ComImport]
        [Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirectInput8
        {
            [PreserveSig]
            int CreateDevice(ref Guid rguid, out IntPtr device, IntPtr outer);

            [PreserveSig]
            int EnumDevices(uint dwDevType, IntPtr callback, IntPtr pvRef, uint dwFlags);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DIDEVICEINSTANCE
        {
            public int dwSize;
            public Guid guidInstance;
            public Guid guidProduct;
            public int dwDevType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string tszInstanceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string tszProductName;
            public Guid guidFFDriver;
            public ushort wUsagePage;
            public ushort wUsage;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate bool DIEnumDevicesCallback(IntPtr instancePtr, IntPtr referencePtr);

        [DllImport("dinput8.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int DirectInput8Create(
            IntPtr hinst,
            int dwVersion,
            ref Guid riidltf,
            [MarshalAs(UnmanagedType.Interface)] out IDirectInput8 ppvOut,
            IntPtr punkOuter);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
