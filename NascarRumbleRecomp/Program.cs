using System.Reflection;
using FullRumble.Config;

string version = Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3) ?? "0.0.0";

if (args.Length > 0 && args[0] == "--version")
{
    Console.WriteLine($"FullRumble {version}");
    return 0;
}

if (args.Length == 2 && args[0] == "--set-disc")
{
    string path = Path.GetFullPath(args[1]);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 2;
    }

    var config = UserConfig.Load();
    config.DiscPath = path;
    config.Save();
    Console.WriteLine($"Disc path saved to {UserConfig.FilePath}");
    return 0;
}

if (args.Length > 0)
{
    Console.Error.WriteLine("usage: FullRumble [--version | --set-disc PATH]");
    return 2;
}

Console.WriteLine($"FullRumble {version} — static recompilation of NASCAR Rumble (PlayStation, SLUS-01068).");
Console.WriteLine("This project does not include any game assets.");

var loaded = UserConfig.Load();
if (loaded.DiscPath is { } discPath)
    Console.WriteLine($"Disc configured: {discPath}");
else
    Console.WriteLine("Disc: not configured. See docs/DUMPING.md to prepare a dump from a disc you own.");

return 0;
