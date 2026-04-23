using System.Text.Json;

namespace MdPost.Services;

public sealed class MdPostConfig
{
    public string DefaultBackend { get; set; } = "rentry";

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".md-post", "config.json");

    public static MdPostConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new MdPostConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<MdPostConfig>(json) ?? new MdPostConfig();
    }

    public static void Save(MdPostConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(ConfigPath, json);
    }
}
