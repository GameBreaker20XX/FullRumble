using System.Text.Json;
using RecompOne.Runtime.Config;
using Xunit;

namespace NascarRumbleRecomp.Tests;

public sealed class InputConfigTests
{
    [Fact]
    public void MouseSteeringIsOptInAndControllerLayoutDefaultsToAutomatic()
    {
        var config = new GameConfig();

        Assert.False(config.MouseSteeringEnabled);
        Assert.Equal(0.02f, config.MouseSteeringSensitivity);
        Assert.Equal(0.08f, config.MouseSteeringDeadzone);
        Assert.Equal("Auto", config.PadLayout);
        Assert.Equal("Auto", config.Pad2Layout);
        Assert.Empty(config.Mouse.Cross);
        Assert.Empty(config.Mouse2.Cross);
    }

    [Fact]
    public void MouseBindingsAndInputPreferencesSurviveConfigSerialization()
    {
        var config = new GameConfig
        {
            Mouse = new MouseBindings { Cross = [0], Down = [101] },
            Mouse2 = new MouseBindings { Start = [100] },
            PadLayout = "PlayStation",
            Pad2Layout = "Nintendo Switch",
            MouseSteeringEnabled = true,
            MouseSteeringSensitivity = 0.035f,
            MouseSteeringDeadzone = 0.12f,
        };

        string json = JsonSerializer.Serialize(config);
        var restored = Assert.IsType<GameConfig>(JsonSerializer.Deserialize<GameConfig>(json));

        Assert.Equal(new[] { 0 }, restored.Mouse.Cross);
        Assert.Equal(new[] { 101 }, restored.Mouse.Down);
        Assert.Equal(new[] { 100 }, restored.Mouse2.Start);
        Assert.Equal("PlayStation", restored.PadLayout);
        Assert.Equal("Nintendo Switch", restored.Pad2Layout);
        Assert.True(restored.MouseSteeringEnabled);
        Assert.Equal(0.035f, restored.MouseSteeringSensitivity);
        Assert.Equal(0.12f, restored.MouseSteeringDeadzone);
    }
}
