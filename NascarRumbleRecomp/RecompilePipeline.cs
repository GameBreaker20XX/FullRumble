using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FullRumble.Config;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace FullRumble;

internal static class RecompilePipeline
{
    private const int MaxPasses = 6;
    private static readonly Regex DispatcherTarget =
        new(@"Dispatcher\.Call\(c, m, 0x([0-9A-Fa-f]+)u\)", RegexOptions.Compiled);
    private static readonly object BuildLock = new();
    private const string ImplicitUsingsSource = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    public static bool HasCurrentLibrary()
    {
        if (!File.Exists(UserConfig.GameLibraryPath) || !File.Exists(UserConfig.BuildVersionPath))
            return false;

        return string.Equals(
            File.ReadAllText(UserConfig.BuildVersionPath).Trim(),
            ProductVersion,
            StringComparison.Ordinal);
    }

    public static void Build(string discCuePath, Action<string>? log = null)
    {
        lock (BuildLock)
        {
            if (HasCurrentLibrary())
                return;
            BuildCore(discCuePath, log);
        }
    }

    private static void BuildCore(string discCuePath, Action<string>? log)
    {
        if (!File.Exists(discCuePath) ||
            !Path.GetExtension(discCuePath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recompilation requires a staged .cue file.");

        string support = Path.Combine(AppContext.BaseDirectory, "recompile");
        string configSource = Path.Combine(support, "config", "nascar.json");
        string functionMapSource = Path.Combine(support, "config", "nascar_funcmap.json");
        string sourceDirectory = Path.Combine(support, "src");
        RequireFile(configSource);
        RequireFile(functionMapSource);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Recompilation support sources are missing: {sourceDirectory}");

        Directory.CreateDirectory(UserConfig.BuildDirectory);
        string workDirectory = Path.Combine(UserConfig.BuildDirectory, "work");
        string outputDirectory = Path.Combine(workDirectory, "Recompiled");
        string functionMapPath = Path.Combine(workDirectory, "nascar_funcmap.build.json");
        string configPath = Path.Combine(workDirectory, "nascar.build.json");

        Directory.CreateDirectory(workDirectory);
        File.Copy(functionMapSource, functionMapPath, overwrite: true);
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        JsonObject config = JsonNode.Parse(File.ReadAllText(configSource))?.AsObject()
            ?? throw new InvalidDataException("The bundled NASCAR recompilation config is invalid.");
        config["cue"] = Path.GetFullPath(discCuePath);
        config["funcMap"] = functionMapPath;
        config["game"]!["output"] = outputDirectory;
        File.WriteAllText(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        for (int pass = 1; pass <= MaxPasses; pass++)
        {
            log?.Invoke($"Recompilation pass {pass} of {MaxPasses}...");
            RecompilerRunner.Run(configPath);
            if (!AugmentFunctionMap(functionMapPath, Path.Combine(outputDirectory, "main.cs")))
                break;

            if (pass == MaxPasses)
                throw new InvalidOperationException(
                    $"The function map did not converge after {MaxPasses} recompilation passes.");
        }

        log?.Invoke("Compiling the game library...");
        string temporaryLibrary = UserConfig.GameLibraryPath + ".tmp";
        CompileLibrary(sourceDirectory, outputDirectory, temporaryLibrary, log);
        File.Move(temporaryLibrary, UserConfig.GameLibraryPath, overwrite: true);
        File.WriteAllText(UserConfig.BuildVersionPath, ProductVersion);

        log?.Invoke("Game library built successfully.");
    }

    internal static bool AugmentFunctionMap(string mapPath, string generatedPath)
    {
        JsonObject document = JsonNode.Parse(File.ReadAllText(mapPath))?.AsObject()
            ?? throw new InvalidDataException($"Invalid function map: {mapPath}");
        JsonArray functions = document["functions"]?.AsArray()
            ?? throw new InvalidDataException($"The function map has no functions array: {mapPath}");

        var known = new HashSet<uint>();
        var ranges = new List<(uint Start, int Size)>();
        foreach (JsonNode? item in functions)
        {
            JsonObject function = item?.AsObject()
                ?? throw new InvalidDataException("A function map entry is not an object.");
            uint address = ParseAddress(function["address"], "function address");
            int size = ParseSize(function["size"]);
            known.Add(address);
            ranges.Add((address, size));
        }

        var calls = new HashSet<uint>();
        foreach (Match match in DispatcherTarget.Matches(File.ReadAllText(generatedPath)))
            calls.Add(uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        uint[] missing = calls.Except(known).Order().ToArray();
        if (missing.Length == 0)
            return false;

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        var additions = new List<JsonObject>(missing.Length);
        foreach (uint target in missing)
        {
            var owners = ranges
                .Where(range => range.Start < target && target < (ulong)range.Start + (uint)range.Size)
                .OrderBy(range => range.Size)
                .ToArray();
            uint end;
            if (owners.Length > 0)
            {
                var owner = owners[0];
                end = checked(owner.Start + (uint)owner.Size);
            }
            else
            {
                var next = ranges.FirstOrDefault(range => range.Start > target);
                if (next == default)
                    throw new InvalidDataException($"0x{target:x8}: no upper bound in the function map.");
                end = next.Start;
            }

            uint size = end - target;
            if (target % 4 != 0 || size == 0 || size % 4 != 0 || size > int.MaxValue)
                throw new InvalidDataException($"0x{target:x8}: invalid auxiliary range of {size} bytes.");

            additions.Add(new JsonObject
            {
                ["address"] = target.ToString("x8", CultureInfo.InvariantCulture),
                ["name"] = $"RECOMP_LABEL_{target:X8}",
                ["size"] = (int)size,
            });
        }

        var sorted = functions
            .Select(node => node?.AsObject() ?? throw new InvalidDataException("Invalid function map entry."))
            .Concat(additions)
            .OrderBy(function => ParseAddress(function["address"], "function address"))
            .Select(function => (JsonNode?)function.DeepClone());
        document["functions"] = new JsonArray(sorted.ToArray());
        File.WriteAllText(mapPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    private static uint ParseAddress(JsonNode? value, string description)
    {
        string text = value?.GetValue<string>()
            ?? throw new InvalidDataException($"Missing {description} in function map.");
        return uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static int ParseSize(JsonNode? value)
    {
        if (value is null)
            throw new InvalidDataException("Missing function size in function map.");
        return value.GetValue<int>();
    }

    private static void CompileLibrary(
        string sourceDirectory,
        string generatedDirectory,
        string destination,
        Action<string>? log)
    {
        string[] sourcePaths = Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(generatedDirectory, "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!sourcePaths.Any(path => Path.GetFileName(path).Equals("main.cs", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException("The recompiler did not produce main.cs.", generatedDirectory);

        string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
            throw new InvalidOperationException("The .NET runtime did not expose its trusted assembly list.");

        var references = trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(RecompOne.Runtime.Runtime).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTrees = sourcePaths.Select(path =>
                CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path), Encoding.UTF8), parseOptions, path))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsingsSource, parseOptions, "FullRumble.ImplicitUsings.g.cs"));
        var compilation = CSharpCompilation.Create(
            "NascarRumbleRecompLib",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var stream = File.Create(destination);
        var result = compilation.Emit(stream);
        foreach (Diagnostic diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            log?.Invoke(diagnostic.ToString());
        if (!result.Success)
            throw new InvalidOperationException("The generated game library failed to compile; see the setup log.");

        int warningCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        if (warningCount > 0)
            log?.Invoke($"Compiler emitted {warningCount} warning(s); the game library was still built.");
    }

    private static string ProductVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Recompilation support file is missing: {path}", path);
    }

}
