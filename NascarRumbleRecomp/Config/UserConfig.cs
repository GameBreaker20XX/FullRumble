using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullRumble.Config;

public sealed class UserConfig
{
    [JsonPropertyName("discPath")]
    public string? DiscPath { get; set; }

    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FullRumble");

    public static string FilePath => Path.Combine(Directory, "config.json");

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
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
