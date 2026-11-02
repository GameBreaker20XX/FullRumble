using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Config;
using RecompOne.Runtime.Cdrom;

namespace FullRumble;

internal static class RecompilerRunner
{
    public static void Run(string configPath)
    {
        configPath = Path.GetFullPath(configPath);
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"config not found: {configPath}", configPath);

        var config = ConfigLoader.Load(configPath);
        string configDir = Path.GetDirectoryName(configPath)!;
        string? ResolvePath(string? path) =>
            path == null ? null : Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(configDir, path));

        config.Elf = ResolvePath(config.Elf);
        config.Map = ResolvePath(config.Map);
        config.FuncMap = ResolvePath(config.FuncMap);
        foreach (var overlay in config.Overlays)
        {
            overlay.Elf = ResolvePath(overlay.Elf);
            overlay.Map = ResolvePath(overlay.Map);
            overlay.FuncMap = ResolvePath(overlay.FuncMap);
        }

        string cuePath = Path.GetFullPath(Path.Combine(configDir, config.Cue));
        if (!File.Exists(cuePath))
            throw new FileNotFoundException($"disc file not found: {cuePath}", cuePath);

        string outputPath = Path.GetFullPath(Path.Combine(configDir, config.Game.Output));
        Directory.CreateDirectory(outputPath);

        using var fileSystem = CueFs.Open(cuePath);
        OverlayWriter.Write(config, fileSystem, outputPath);
    }
}
