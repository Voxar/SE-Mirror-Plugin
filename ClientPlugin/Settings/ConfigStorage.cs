using System;
using System.IO;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Settings;

public static class ConfigStorage
{
    private static readonly string ConfigFileName = string.Concat(Plugin.Name, ".cfg");
    private static string ConfigFilePath => Path.Combine(MyFileSystem.UserDataPath, "Storage", ConfigFileName);

    public static void Save(Config config)
    {
        var path = ConfigFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var text = File.CreateText(path))
            new XmlSerializer(typeof(Config)).Serialize(text, config);
    }

    public static Config Load()
    {
        // Entire body wrapped: resolving ConfigFilePath touches
        // MyFileSystem.UserDataPath, which can throw NullReference /
        // ArgumentNull if accessed during static-init timing windows
        // before SE's filesystem is ready. An unhandled throw here
        // becomes a TypeInitializationException on Config.Current and
        // prevents the plugin from composing at all.
        try
        {
            var path = ConfigFilePath;
            if (!File.Exists(path)) return new Config();

            var xmlSerializer = new XmlSerializer(typeof(Config));
            using (var streamReader = File.OpenText(path))
                return (Config)xmlSerializer.Deserialize(streamReader) ?? new Config();
        }
        catch (Exception ex)
        {
            MyLog.Default.Warning($"{ConfigFileName}: Failed to read config file: {ex.Message}");
            return new Config();
        }
    }
        
}