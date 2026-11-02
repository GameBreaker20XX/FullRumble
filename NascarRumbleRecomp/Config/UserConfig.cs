using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullRumble.Config;

public sealed class UserConfig
{
    [JsonPropertyName("discPath")]
    public string? DiscPath { get; set; }

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FullRumble");

    public static string FilePath => Path.Combine(DataDirectory, "config.json");
    public static string BuildDirectory => Path.Combine(DataDirectory, "build");
    public static string GameLibraryPath => Path.Combine(BuildDirectory, "NascarRumbleRecompLib.dll");
    public static string BuildVersionPath => Path.Combine(BuildDirectory, "library.version");

    public static UserConfig Load()
    {
        if (!File.Exists(FilePath))
            return new UserConfig();
        try
        {
            return JsonSerializer.Deserialize<UserConfig>(File.ReadAllText(FilePath)) ?? new UserConfig();
        }
        catch (JsonException)
        {
            return new UserConfig();
        }
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
