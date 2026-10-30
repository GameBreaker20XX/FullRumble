using System.Security.Cryptography;
using FullRumble.Disc;
using Xunit;

namespace NascarRumbleRecomp.Tests;

public class DiscValidatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fullrumble-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Executable(int size = 3000) =>
        Enumerable.Range(0, size).Select(i => (byte)(i * 7 + 3)).ToArray();

    private static void AssertValid(FullRumble.Disc.DiscValidationResult result) =>
        Assert.True(result.Status == FullRumble.Disc.DiscStatus.Valid,
            result.Message + " | checks: " + string.Join(" ; ", result.Checks));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string WriteText(string name, string content) =>
        Write(name, System.Text.Encoding.ASCII.GetBytes(content));

    [Fact]
    public void CookedIso_Validates()
    {
        byte[] exe = Executable();
        string iso = Write("game.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        var result = DiscValidator.Validate(iso, Hash(exe));

        AssertValid(result);
        Assert.Equal("SLUS-01068", result.Serial);
        Assert.Equal(Hash(exe), result.Sha256);
    }

    [Fact]
    public void CueBinPair_ReadsSameSectorsAsCooked()
    {
        byte[] exe = Executable();
        byte[] cooked = SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe);
        string bin = Write("game.bin", SyntheticIso.WrapRaw(cooked));
        WriteText("game.cue", SyntheticIso.CueFor("game.bin"));

        var result = DiscValidator.Validate(Path.Combine(_root, "game.cue"), Hash(exe));

        AssertValid(result);
        string cookedPath = Write("game.iso", cooked);
        using var rawImage = DiscImage.Open(Path.Combine(_root, "game.cue"));
        using var cookedImage = DiscImage.Open(cookedPath);
        Assert.Equal(cookedImage.ReadSectors(16, 4), rawImage.ReadSectors(16, 4));
    }

    [Fact]
    public void BareRawBin_Validates()
    {
        byte[] exe = Executable();
        string bin = Write("bare.bin", SyntheticIso.WrapRaw(SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));

        var result = DiscValidator.Validate(bin, Hash(exe));

        AssertValid(result);
    }

    [Fact]
    public void Reframer_ExportsCookedIsoThatValidates()
    {
        byte[] exe = Executable();
        string bin = Write("reframe.bin", SyntheticIso.WrapRaw(SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        string cooked = Path.Combine(_root, "reframed.iso");
        using (var image = DiscImage.Open(bin))
            image.ExportCookedIso(cooked);

        var result = DiscValidator.Validate(cooked, Hash(exe));

        AssertValid(result);
    }

    [Fact]
    public void Archive_IsRejectedWithPlainMessage()
    {
        string zip = Write("game.zip", new byte[10]);

        var result = DiscValidator.Validate(zip);

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("Archives are not supported", result.Message);
    }

    [Fact]
    public void Chd_IsRejectedAsNotImplemented()
    {
        string chd = Write("game.chd", new byte[10]);

        var result = DiscValidator.Validate(chd);

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("not implemented", result.Message);
    }

    [Fact]
    public void OtherRegion_IsRejectedNamingTheSerial()
    {
        byte[] exe = Executable();
        string iso = Write("pal.iso", SyntheticIso.BuildCooked("RUMBLE", "SLES-55555", exe));

        var result = DiscValidator.Validate(iso, Hash(exe));

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("SLES-55555", result.Message);
    }

    [Fact]
    public void ModifiedExecutable_IsRejectedByHash()
    {
        byte[] exe = Executable();
        string iso = Write("mod.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        var result = DiscValidator.Validate(iso, Hash(Executable(3001)));

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("checksum", result.Message);
    }

    [Fact]
    public void TruncatedImage_IsRejected()
    {
        byte[] exe = Executable(6000);
        byte[] cooked = SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe);
        byte[] raw = SyntheticIso.WrapRaw(cooked);
        Array.Resize(ref raw, raw.Length - 5 * 2352 - 100);
        string bin = Write("truncated.bin", raw);

        var result = DiscValidator.Validate(bin, Hash(exe));

        Assert.Equal(DiscStatus.Invalid, result.Status);
    }

    [Fact]
    public void MultiFileCue_WithDataInOneTrack_Validates()
    {
        byte[] exe = Executable();
        Write("audio.bin", new byte[75 * 2352]);
        string dataBin = Write("data.bin", SyntheticIso.WrapRaw(SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        WriteText("multi.cue", SyntheticIso.AudioPlusDataCue("audio.bin", "data.bin"));

        var result = DiscValidator.Validate(Path.Combine(_root, "multi.cue"), Hash(exe));

        AssertValid(result);
    }

    [Fact]
    public void Cue_WithGameDataInTwoTracks_IsRejected()
    {
        byte[] exe = Executable();
        Write("split.bin", SyntheticIso.WrapRaw(SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe)));
        WriteText("split.cue", SyntheticIso.TwoDataTracksCue("split.bin"));

        var result = DiscValidator.Validate(Path.Combine(_root, "split.cue"), Hash(exe));

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("single track", result.Message);
    }

    [Fact]
    public void FolderWithOneImageInsideSubfolder_Validates()
    {
        byte[] exe = Executable();
        Write("dumps/game.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        var result = DiscValidator.Validate(_root, Hash(exe));

        AssertValid(result);
    }

    [Fact]
    public void FolderWithSeveralImages_AsksForChoice()
    {
        byte[] exe = Executable();
        Write("a.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));
        Write("sub/b.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        var result = DiscValidator.Validate(_root, Hash(exe));

        Assert.Equal(DiscStatus.NeedsChoice, result.Status);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void FolderWithoutImages_IsInvalid()
    {
        var result = DiscValidator.Validate(_root);

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("No .cue, .bin or .iso found", result.Message);
    }

    [Fact]
    public void ExtractFile_ReturnsExecutableBytes()
    {
        byte[] exe = Executable();
        string iso = Write("extract.iso", SyntheticIso.BuildCooked("RUMBLE", "SLUS-01068", exe));

        byte[] extracted = DiscValidator.ExtractFile(iso, "SLUS_010.68");

        Assert.Equal(exe, extracted);
    }

    [Fact]
    public void MissingCueBin_IsRejectedAsSeparatePair()
    {
        WriteText("lonely.cue", SyntheticIso.CueFor("missing.bin"));

        var result = DiscValidator.Validate(Path.Combine(_root, "lonely.cue"));

        Assert.Equal(DiscStatus.Invalid, result.Status);
        Assert.Contains("together", result.Message);
    }
}
