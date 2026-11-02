using System.IO.Compression;
using FullRumble.Updater;
using Xunit;

namespace NascarRumbleRecomp.Tests;

public sealed class UpdaterTests
{
    [Theory]
    [InlineData("v0.2.0.0", 0, 2, 0, 0)]
    [InlineData("0.1.0.0", 0, 1, 0, 0)]
    public void ReleaseTagParsesFourPartVersion(
        string tag, int major, int minor, int build, int revision)
    {
        Assert.Equal(new Version(major, minor, build, revision),
            ReleaseMetadataParser.ParseVersionTag(tag));
    }

    [Theory]
    [InlineData("Race 1.1")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0.0/../../escape")]
    public void InvalidReleaseTagIsRejected(string tag)
    {
        Assert.Throws<InvalidDataException>(() => ReleaseMetadataParser.ParseVersionTag(tag));
    }

    [Fact]
    public void ChecksumParserSelectsTheNamedAsset()
    {
        const string expected = "699bc5bc863ca32828bb48851c93a0cc1a4518fd0ad10540cc91f611342f0b07";
        string checksums = $"{expected}  SHA256SUMS\n{expected}  FullRumble-Windows.zip\n";

        Assert.Equal(expected, ReleaseMetadataParser.ParseSha256(checksums, "FullRumble-Windows.zip"));
    }

    [Fact]
    public void ChecksumParserRejectsMissingMalformedAndDuplicateEntries()
    {
        Assert.Throws<InvalidDataException>(() =>
            ReleaseMetadataParser.ParseSha256("abc  FullRumble-Windows.zip", "FullRumble-Windows.zip"));

        const string hash = "699bc5bc863ca32828bb48851c93a0cc1a4518fd0ad10540cc91f611342f0b07";
        Assert.Throws<InvalidDataException>(() =>
            ReleaseMetadataParser.ParseSha256($"{hash} FullRumble-Windows.zip\n{hash} FullRumble-Windows.zip",
                "FullRumble-Windows.zip"));
    }

    [Theory]
    [InlineData("FullRumble.exe")]
    [InlineData("recompile/config/nascar.json")]
    [InlineData("lib/net10.0/SomeRuntime.dll")]
    public void SafePackagePathsAreAccepted(string path)
    {
        Assert.Equal(path.Replace('/', Path.DirectorySeparatorChar),
            ReleaseMetadataParser.GetSafeRelativePath(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("folder/../outside.txt")]
    [InlineData("NUL.txt")]
    public void UnsafePackagePathsAreRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => ReleaseMetadataParser.GetSafeRelativePath(path));
    }

    [Fact]
    public void DiscFolderIsExcludedFromPackageUpdates()
    {
        Assert.True(ReleaseMetadataParser.IsDiscPath("disc/own-dump.cue"));
        Assert.True(ReleaseMetadataParser.IsDiscPath("DISC/own-dump.bin"));
        Assert.False(ReleaseMetadataParser.IsDiscPath("recompile/disc-layout.json"));
        Assert.False(ReleaseMetadataParser.IsDiscPath("FullRumble.exe"));
    }

    [Fact]
    public void UpdatePackagePreservesTheExistingDiscFolder()
    {
        string work = CreateTempDirectory();
        try
        {
            string stage = Path.Combine(work, "stage");
            string install = Path.Combine(work, "install");
            string backup = Path.Combine(work, "backup");
            Directory.CreateDirectory(Path.Combine(stage, "disc"));
            Directory.CreateDirectory(Path.Combine(install, "disc"));
            File.WriteAllText(Path.Combine(stage, "FullRumble.exe"), "new app");
            File.WriteAllText(Path.Combine(stage, "FullRumbleUpdateHelper.exe"), "new helper");
            File.WriteAllText(Path.Combine(stage, "disc", "packaged.cue"), "package disc");
            File.WriteAllText(Path.Combine(install, "FullRumble.exe"), "old app");
            File.WriteAllText(Path.Combine(install, "disc", "my-own-disc.cue"), "my own disc");

            FullRumble.Updater.Program.ApplyPackageFiles(stage, install, backup);

            Assert.Equal("new app", File.ReadAllText(Path.Combine(install, "FullRumble.exe")));
            Assert.Equal("new helper", File.ReadAllText(Path.Combine(install, "FullRumbleUpdateHelper.exe")));
            Assert.False(File.Exists(Path.Combine(install, "disc", "packaged.cue")));
            Assert.Equal("my own disc", File.ReadAllText(Path.Combine(install, "disc", "my-own-disc.cue")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void ArchiveExtractionRejectsPathTraversal()
    {
        string work = CreateTempDirectory();
        try
        {
            string archivePath = Path.Combine(work, "malicious.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("../outside.txt").Open()))
                writer.Write("outside");

            Assert.Throws<InvalidDataException>(() =>
                FullRumble.Updater.Program.ExtractPackage(archivePath, Path.Combine(work, "stage")));
            Assert.False(File.Exists(Path.Combine(work, "outside.txt")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void ArchiveWithoutUpdateHelperIsRejected()
    {
        string work = CreateTempDirectory();
        try
        {
            string archivePath = Path.Combine(work, "incomplete.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (StreamWriter writer = new(archive.CreateEntry("FullRumble.exe").Open()))
                writer.Write("app");

            Assert.Throws<InvalidDataException>(() =>
                FullRumble.Updater.Program.ExtractPackage(archivePath, Path.Combine(work, "stage")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void ArchiveExtractsTheGameAndBackgroundUpdateHelper()
    {
        string work = CreateTempDirectory();
        try
        {
            string archivePath = Path.Combine(work, "release.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (StreamWriter game = new(archive.CreateEntry("FullRumble.exe").Open()))
                    game.Write("app");
                using (StreamWriter helper = new(archive.CreateEntry("FullRumbleUpdateHelper.exe").Open()))
                    helper.Write("helper");
            }

            string stage = Path.Combine(work, "stage");
            FullRumble.Updater.Program.ExtractPackage(archivePath, stage);

            Assert.Equal("app", File.ReadAllText(Path.Combine(stage, "FullRumble.exe")));
            Assert.Equal("helper", File.ReadAllText(Path.Combine(stage, "FullRumbleUpdateHelper.exe")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"FullRumble-updater-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
