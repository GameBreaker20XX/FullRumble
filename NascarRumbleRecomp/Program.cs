using System.Reflection;
using FullRumble;
using FullRumble.Config;
using FullRumble.Disc;

string version = Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3) ?? "0.0.0";

if (args.Length > 0 && args[0] == "--version")
{
    Console.WriteLine($"Full Rumble {version}");
    return 0;
}

if (args.Length == 2 && (args[0] == "--set-disc" || args[0] == "--install"))
{
    string path = Path.GetFullPath(args[1]);
    if (!File.Exists(path) && !Directory.Exists(path))
    {
        Console.Error.WriteLine($"File or folder not found: {path}");
        return 2;
    }

    var config = UserConfig.Load();
    config.DiscPath = path;
    config.Save();
    Console.WriteLine($"Disc path saved to {UserConfig.FilePath}");
    return 0;
}

if (args.Length == 2 && args[0] == "--check-disc")
{
    var result = DiscValidator.Validate(Path.GetFullPath(args[1]));
    foreach (string check in result.Checks)
        Console.WriteLine($"  [ok] {check}");
    Console.WriteLine(result.Status == DiscStatus.Valid ? $"VALID: {result.Message}" : $"INVALID: {result.Message}");
    if (result.Status == DiscStatus.NeedsChoice)
        foreach (string candidate in result.Candidates)
            Console.WriteLine($"  candidate: {candidate}");
    return result.Status == DiscStatus.Valid ? 0 : 1;
}

if (args.Length == 2 && args[0] == "--build-library")
{
    try
    {
        string source = Path.GetFullPath(args[1]);
        var validation = DiscValidator.Validate(source);
        foreach (string check in validation.Checks)
            Console.WriteLine($"  [ok] {check}");
        if (validation.Status != DiscStatus.Valid)
        {
            Console.Error.WriteLine($"INVALID: {validation.Message}");
            foreach (string candidate in validation.Candidates)
                Console.Error.WriteLine($"  candidate: {candidate}");
            return 1;
        }

        string staged = DiscLibrary.EnsureStaged(source, log: Console.WriteLine);
        var config = UserConfig.Load();
        config.DiscPath = staged;
        config.Save();
        RecompilePipeline.Build(staged, Console.WriteLine);
        Console.WriteLine($"Game library ready: {UserConfig.GameLibraryPath}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"Could not build the game library: {error}");
        return 1;
    }
}

if (args.Length == 4 && args[0] == "--extract")
{
    try
    {
        byte[] bytes = DiscValidator.ExtractFile(Path.GetFullPath(args[1]), args[2]);
        File.WriteAllBytes(args[3], bytes);
        Console.WriteLine($"Wrote {bytes.Length} bytes to {args[3]}");
        return 0;
    }
    catch (DiscValidationException error)
    {
        Console.Error.WriteLine(error.Message);
        return 1;
    }
}

if (args.Length > 0)
{
    Console.Error.WriteLine("usage: Full Rumble [--version | --set-disc PATH | --check-disc PATH | --build-library PATH | --extract IMAGE DISCPATH OUT]");
    Console.Error.WriteLine("      --set-disc (alias --install) saves the disc path the setup screen would save.");
    return 2;
}

var loaded = UserConfig.Load();
string? configured = loaded.DiscPath;
DiscValidationResult? previous = null;
string? boot = null;

if (configured is not null)
{
    boot = configured;
    if (Directory.Exists(configured))
    {
        var found = DiscFinder.Scan(configured);
        if (found.Count == 1)
            boot = found[0];
    }

    previous = DiscValidator.Validate(boot);
    if (previous.Status == DiscStatus.Valid && RecompilePipeline.HasCurrentLibrary())
    {
        // Boot from the copy the game keeps for itself, so a lone .bin or .iso becomes the .cue
        // the runtime reads and the player is never asked for the disc again.
        string staged = DiscLibrary.EnsureStaged(boot);
        if (!string.Equals(staged, boot, StringComparison.OrdinalIgnoreCase))
        {
            loaded.DiscPath = staged;
            loaded.Save();
        }

        return GameBoot.Run(staged);
    }

    if (previous.Status == DiscStatus.Valid)
    {
        GameBoot.HideConsole();
        string? built = SetupUi.Run(configured, previous, autoBuild: true);
        return built is null ? 0 : GameBoot.Run(built);
    }
}

GameBoot.HideConsole();
string? chosen = SetupUi.Run(configured, previous);
return chosen is null ? 0 : GameBoot.Run(chosen);
