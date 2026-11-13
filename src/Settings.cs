using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace GamepadLauncherStarter
{
    public enum LaunchMode
    {
        Normal = 0,
        Minimized = 1
    }

    public enum PartialLaunchMode
    {
        LaunchMissing = 0,
        DoNothingIfAnyRunning = 1
    }

    [XmlRoot("GamepadLauncherStarterSettings")]
    public class AppSettings
    {
        public bool Enabled = true;
        public bool TrayOnly;
        public bool ShowNotifications = true;
        public bool LaunchOnStartIfConnected = true;
        public bool SkipWhenFullscreenGame = true;
        public PartialLaunchMode PartialMode = PartialLaunchMode.LaunchMissing;
        public bool StartWithWindows = true;
        public bool ManageSteamAutostart = true;
        public bool SteamAutostartChecked;
        public string SteamAutostartBackup = "";
        public List<AppEntry> Apps = new List<AppEntry>();
        public List<IgnoredDevice> IgnoredDevices = new List<IgnoredDevice>();

        public static AppSettings CreateDefault()
        {
            AppSettings settings = new AppSettings();
            settings.Apps.Add(AppCatalog.CreateSteam());
            settings.Apps.Add(AppCatalog.CreateEpic());
            settings.Apps.Add(AppCatalog.CreateDiscord());
            AppCatalog.AutodetectPaths(settings.Apps);
            return settings;
        }

        public AppEntry FindApp(string id)
        {
            if (id == null)
                return null;
            foreach (AppEntry entry in Apps)
            {
                if (entry != null && string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }
    }

    public class AppEntry
    {
        [XmlAttribute("id")]
        public string Id = "";
        public string Name = "";
        public bool Enabled;
        public string ExecutablePath = "";
        public string Arguments = "";
        public string MinimizedArguments = "";
        public string ProcessName = "";
        public LaunchMode LaunchMode = LaunchMode.Normal;
        public bool ShowWindowIfRunning = true;
        public bool BuiltIn;

        public AppEntry Clone()
        {
            return (AppEntry)MemberwiseClone();
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? "Приложение" : Name;
        }
    }

    public class IgnoredDevice
    {
        [XmlAttribute("guid")]
        public string Guid = "";
        public string Name = "";
    }

    public static class SettingsStore
    {
        private static readonly object Sync = new object();

        public static AppSettings Load()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(AppPaths.SettingsFile))
                        return AppSettings.CreateDefault();

                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (FileStream stream = File.OpenRead(AppPaths.SettingsFile))
                    {
                        AppSettings settings = (AppSettings)serializer.Deserialize(stream);
                        Normalize(settings);
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Не удалось прочитать настройки, использую значения по умолчанию", ex);
                    try
                    {
                        string bad = AppPaths.SettingsFile + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(AppPaths.SettingsFile, bad, true);
                        Logger.Warn("Повреждённый файл настроек сохранён: " + bad);
                    }
                    catch
                    {
                    }
                    return AppSettings.CreateDefault();
                }
            }
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null)
                return;
            lock (Sync)
            {
                try
                {
                    AppPaths.EnsureDataDirectory();
                    Normalize(settings);
                    string temp = AppPaths.SettingsFile + ".tmp";
                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        serializer.Serialize(stream, settings);
                    }
                    if (File.Exists(AppPaths.SettingsFile))
                    {
                        try
                        {
                            File.Replace(temp, AppPaths.SettingsFile, null);
                        }
                        catch
                        {
                            File.Delete(AppPaths.SettingsFile);
                            File.Move(temp, AppPaths.SettingsFile);
                        }
                    }
                    else
                    {
                        File.Move(temp, AppPaths.SettingsFile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Не удалось сохранить настройки", ex);
                }
            }
        }

        public static AppSettings Clone(AppSettings settings)
        {
            if (settings == null)
                return AppSettings.CreateDefault();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.Serialize(stream, settings);
                    stream.Position = 0;
                    AppSettings copy = (AppSettings)serializer.Deserialize(stream);
                    Normalize(copy);
                    return copy;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось скопировать настройки", ex);
                return AppSettings.CreateDefault();
            }
        }

        public static void Normalize(AppSettings settings)
        {
            if (settings == null)
                return;
            if (settings.Apps == null)
                settings.Apps = new List<AppEntry>();
            if (settings.IgnoredDevices == null)
                settings.IgnoredDevices = new List<IgnoredDevice>();
            if (settings.SteamAutostartBackup == null)
                settings.SteamAutostartBackup = "";

            for (int i = settings.Apps.Count - 1; i >= 0; i--)
            {
                if (settings.Apps[i] == null)
                {
                    settings.Apps.RemoveAt(i);
                    continue;
                }
                AppEntry entry = settings.Apps[i];
                if (entry.Id == null)
                    entry.Id = "";
                if (entry.Name == null)
                    entry.Name = "";
                if (entry.ExecutablePath == null)
                    entry.ExecutablePath = "";
                if (entry.Arguments == null)
                    entry.Arguments = "";
                if (entry.MinimizedArguments == null)
                    entry.MinimizedArguments = "";
                if (entry.ProcessName == null)
                    entry.ProcessName = "";
                if (string.IsNullOrEmpty(entry.ProcessName) && !string.IsNullOrEmpty(entry.ExecutablePath))
                {
                    try
                    {
                        entry.ProcessName = Path.GetFileNameWithoutExtension(entry.ExecutablePath);
                    }
                    catch
                    {
                    }
                }
            }

            for (int i = settings.IgnoredDevices.Count - 1; i >= 0; i--)
            {
                if (settings.IgnoredDevices[i] == null || string.IsNullOrEmpty(settings.IgnoredDevices[i].Guid))
                    settings.IgnoredDevices.RemoveAt(i);
                else if (settings.IgnoredDevices[i].Name == null)
                    settings.IgnoredDevices[i].Name = "";
            }
        }
    }
}
