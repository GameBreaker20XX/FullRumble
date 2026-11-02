using System.Text.Json.Nodes;
using FullRumble;
using Xunit;

namespace NascarRumbleRecomp.Tests;

public sealed class RecompilePipelineTests
{
    [Fact]
    public void AugmentFunctionMapAddsTargetsAndStopsWhenTheyAreKnown()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"fullrumble-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mapPath = Path.Combine(directory, "funcmap.json");
        string generatedPath = Path.Combine(directory, "main.cs");

        try
        {
            File.WriteAllText(mapPath,
                """{"functions":[{"address":"80001000","name":"first","size":32},{"address":"80001040","name":"second","size":32}]}""");
            File.WriteAllText(generatedPath,
                "Dispatcher.Call(c, m, 0x80001008u);\nDispatcher.Call(c, m, 0x80001028u);");

            Assert.True(RecompilePipeline.AugmentFunctionMap(mapPath, generatedPath));
            Assert.False(RecompilePipeline.AugmentFunctionMap(mapPath, generatedPath));

            JsonArray functions = JsonNode.Parse(File.ReadAllText(mapPath))!["functions"]!.AsArray();
            Assert.Equal(
                ["80001000", "80001008", "80001028", "80001040"],
                functions.Select(function => function!["address"]!.GetValue<string>()));
            Assert.Equal(24, functions[1]!["size"]!.GetValue<int>());
            Assert.Equal(24, functions[2]!["size"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AugmentFunctionMapRejectsTargetsWithoutAnUpperBound()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"fullrumble-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mapPath = Path.Combine(directory, "funcmap.json");
        string generatedPath = Path.Combine(directory, "main.cs");

        try
        {
            File.WriteAllText(mapPath,
                """{"functions":[{"address":"80001000","name":"last","size":16}]}""");
            File.WriteAllText(generatedPath, "Dispatcher.Call(c, m, 0x80002000u);");

            var error = Assert.Throws<InvalidDataException>(
                () => RecompilePipeline.AugmentFunctionMap(mapPath, generatedPath));
            Assert.Contains("no upper bound", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
