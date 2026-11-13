using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace GamepadLauncherStarter
{
    public static class AutostartManager
    {
        public const string OurValueName = "GamepadLauncherStarter";
        public const string SteamValueName = "Steam";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string GetOurCommand()
        {
            return "\"" + AppInfo.ExePath + "\" --tray";
        }

        public static string ReadRunValue(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                        return null;
                    object value = key.GetValue(name);
                    return value == null ? null : value.ToString();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать автозапуск (" + name + "): " + ex.Message);
                return null;
            }
        }

        public static void ApplyOurAutostart(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    string desired = GetOurCommand();
                    string current = ReadRunValue(OurValueName);
                    if (!string.Equals(current, desired, StringComparison.Ordinal))
                    {
                        SetRunValue(OurValueName, desired);
                        Logger.Info("Автозапуск программы включён: " + desired);
                    }
                }
                else
                {
                    if (ReadRunValue(OurValueName) != null)
                    {
                        DeleteRunValue(OurValueName);
                        Logger.Info("Автозапуск программы отключён.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось изменить автозапуск программы", ex);
            }
        }

        public static void ManageSteamAutostart(AppSettings settings)
        {
            if (settings == null)
                return;
            try
            {
                string current = ReadRunValue(SteamValueName);
                if (!string.IsNullOrEmpty(current))
                {
                    if (current.IndexOf("steam.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (string.IsNullOrEmpty(settings.SteamAutostartBackup))
                        {
                            settings.SteamAutostartBackup = current;
                            WriteSteamBackupFile(current);
                            Logger.Info("Автозапуск Steam сохранён в резервную копию: " + current);
                        }
                        DeleteRunValue(SteamValueName);
                        Logger.Info("Запись Steam удалена из автозапуска Windows.");
                    }
                    else
                    {
                        Logger.Warn("Запись Steam в автозапуске не похожа на Steam, не изменяю: " + current);
                    }
                }
                else
                {
                    if (!settings.SteamAutostartChecked)
                        Logger.Info("Запись Steam в автозапуске не найдена.");
                }
                settings.SteamAutostartChecked = true;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось обработать автозапуск Steam", ex);
            }
        }

        public static bool RestoreSteamAutostart(AppSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.SteamAutostartBackup))
            {
                DeleteSteamBackupFile();
                return false;
            }
            try
            {
                string current = ReadRunValue(SteamValueName);
                if (!string.IsNullOrEmpty(current))
                {
                    Logger.Info("Steam уже присутствует в автозапуске — восстановление не требуется.");
                    settings.SteamAutostartBackup = "";
                    DeleteSteamBackupFile();
                    return false;
                }

                SetRunValue(SteamValueName, settings.SteamAutostartBackup);
                Logger.Info("Автозапуск Steam восстановлен: " + settings.SteamAutostartBackup);
                settings.SteamAutostartBackup = "";
                DeleteSteamBackupFile();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось восстановить автозапуск Steam", ex);
                return false;
            }
        }

        public static void SyncSteamBackupFile(AppSettings settings)
        {
            if (settings == null)
                return;
            try
            {
                if (!string.IsNullOrEmpty(settings.SteamAutostartBackup))
                {
                    WriteSteamBackupFile(settings.SteamAutostartBackup);
                    return;
                }
                string fromFile = ReadSteamBackupFile();
                if (!string.IsNullOrEmpty(fromFile))
                    settings.SteamAutostartBackup = fromFile;
            }
            catch
            {
            }
        }

        public static string ReadSteamBackupFile()
        {
            try
            {
                if (!File.Exists(AppPaths.SteamBackupFile))
                    return "";
                return File.ReadAllText(AppPaths.SteamBackupFile, Encoding.UTF8).Trim();
            }
            catch
            {
                return "";
            }
        }

        private static void WriteSteamBackupFile(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    DeleteSteamBackupFile();
                    return;
                }
                AppPaths.EnsureDataDirectory();
                File.WriteAllText(AppPaths.SteamBackupFile, value, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось сохранить резервную копию автозапуска Steam: " + ex.Message);
            }
        }

        private static void DeleteSteamBackupFile()
        {
            try
            {
                if (File.Exists(AppPaths.SteamBackupFile))
                    File.Delete(AppPaths.SteamBackupFile);
            }
            catch
            {
            }
        }

        private static void SetRunValue(string name, string value)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    using (RegistryKey created = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        if (created == null)
                            throw new InvalidOperationException("Не удалось открыть раздел автозапуска.");
                        created.SetValue(name, value, RegistryValueKind.String);
                    }
                    return;
                }
                key.SetValue(name, value, RegistryValueKind.String);
            }
        }

        private static void DeleteRunValue(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key != null)
                    key.DeleteValue(name, false);
            }
        }
    }
}
