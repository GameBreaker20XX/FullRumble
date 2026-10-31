using System.Security.Cryptography;
using FullRumble.Disc;
using Xunit;

namespace NascarRumbleRecomp.Tests;

public class DiscLibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fullrumble-library-").FullName;
    private readonly string _target;

    public DiscLibraryTests() => _target = Path.Combine(_root, "game", "disc");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Executable() =>
        Enumerable.Range(0, 3000).Select(i => (byte)(i * 7 + 3)).ToArray();

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertValid(DiscValidationResult result) =>
        Assert.True(result.Status == DiscStatus.Valid,
            result.Message + " | checks: " + string.Join(" ; ", result.Checks));

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string WriteText(string name, string content) =>
        Write(name, System.Text.Encoding.ASCII.GetBytes(content));

    private string CookedBinWithPregap(out byte[] executable)
    {
        executable = Executable();
        byte[] cooked = SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", executable);
        return Write("game.bin", SyntheticIso.Pregap(150).Concat(SyntheticIso.WrapRaw(cooked)).ToArray());
    }

    [Fact]
    public void LoneRawBin_WithPregap_Validates()
    {
        string bin = CookedBinWithPregap(out byte[] exe);

        AssertValid(DiscValidator.Validate(bin, Hash(exe)));
    }

    [Fact]
    public void LoneRawBin_IsStagedAsACueSheetThatStartsOnTheDataTrack()
    {
        string bin = CookedBinWithPregap(out byte[] exe);

        string staged = DiscLibrary.EnsureStaged(bin, _target);

        Assert.Equal(Path.Combine(_target, "game.cue"), staged);
        string[] lines = File.ReadAllLines(staged);
        Assert.Contains("FILE \"game.bin\" BINARY", lines);
        Assert.Contains(lines, line => line.Trim() == "TRACK 01 MODE2/2352");
        Assert.Contains(lines, line => line.Trim() == "INDEX 01 00:02:00");
        AssertValid(DiscValidator.Validate(staged, Hash(exe)));
    }

    [Fact]
    public void CookedIso_IsStagedAsAMode1CueSheet()
    {
        byte[] exe = Executable();
        string iso = Write("game.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        string staged = DiscLibrary.EnsureStaged(iso, _target);

        string cue = File.ReadAllText(staged);
        Assert.Contains("MODE1/2048", cue);
        Assert.Contains("game.iso", cue);
        AssertValid(DiscValidator.Validate(staged, Hash(exe)));
    }

    [Fact]
    public void CueAndBinAreCopiedTogetherAndTheDumpIsLeftAlone()
    {
        byte[] exe = Executable();
        string bin = Write("dump/game.bin", SyntheticIso.WrapRaw(
            SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        string cue = WriteText("dump/game.cue", SyntheticIso.CueFor("game.bin"));

        string staged = DiscLibrary.EnsureStaged(cue, _target);

        Assert.Equal(Path.Combine(_target, "game.cue"), staged);
        AssertValid(DiscValidator.Validate(staged, Hash(exe)));
        Assert.Equal(new FileInfo(bin).Length, new FileInfo(Path.Combine(_target, "game.bin")).Length);
        Assert.True(File.Exists(cue));
        Assert.Equal(new FileInfo(cue).Length, new FileInfo(Path.Combine(_target, "game.cue")).Length);
    }

    [Fact]
    public void FolderInput_StagesTheOneImageInsideIt()
    {
        byte[] exe = Executable();
        Write("dump/game.bin", SyntheticIso.WrapRaw(
            SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        WriteText("dump/game.cue", SyntheticIso.CueFor("game.bin"));

        string staged = DiscLibrary.EnsureStaged(Path.Combine(_root, "dump"), _target);

        AssertValid(DiscValidator.Validate(staged, Hash(exe)));
    }

    [Fact]
    public void SecondRun_ReusesTheCopyInsteadOfCopyingAgain()
    {
        byte[] exe = Executable();
        string bin = Write("dump/game.bin", SyntheticIso.WrapRaw(
            SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        string cue = WriteText("dump/game.cue", SyntheticIso.CueFor("game.bin"));

        string staged = DiscLibrary.EnsureStaged(cue, _target);
        var again = new List<string>();
        string second = DiscLibrary.EnsureStaged(staged, _target, again.Add);

        Assert.Equal(staged, second);
        Assert.DoesNotContain(again, line => line.Contains("copying"));
    }

    [Fact]
    public void MissingBinNextToTheCue_KeepsTheOriginalPathAndNeverThrows()
    {
        string cue = WriteText("broken/game.cue", SyntheticIso.CueFor("game.bin"));

        string staged = DiscLibrary.EnsureStaged(cue, _target);

        Assert.Equal(cue, staged);
    }
}
