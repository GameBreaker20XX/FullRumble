using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FullRumble.Updater;

public static class UpdateService
{
    public static async Task<ReleaseMetadata> CheckForLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpClient client = Program.CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync(
            "https://api.github.com/repos/GameBreaker20XX/FullRumble/releases/latest",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReleaseMetadataParser.ParseLatestRelease(json);
    }

    public static async Task PrepareUpdateForExitAsync(
        ReleaseMetadata release,
        string installDirectory,
        int gameProcessId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string installRoot = Path.GetFullPath(installDirectory);
        string gamePath = Path.Combine(installRoot, "FullRumble.exe");
        string updateHelper = Path.Combine(installRoot, "FullRumbleUpdateHelper.exe");
        if (!File.Exists(gamePath))
            throw new FileNotFoundException("The install folder does not contain FullRumble.exe.", gamePath);
        if (!File.Exists(updateHelper))
            throw new FileNotFoundException("The install folder does not contain the update helper.", updateHelper);
        if (gameProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(gameProcessId));

        Version installed = Program.ReadInstalledVersion(gamePath);
        if (release.Version <= installed)
            throw new InvalidOperationException("The selected release is not newer than the installed version.");

        string workDirectory = Path.Combine(Path.GetTempPath(), $"FullRumble-update-{Guid.NewGuid():N}");
        string stageDirectory = Path.Combine(workDirectory, "stage");
        Directory.CreateDirectory(stageDirectory);
        bool helperStarted = false;

        try
        {
            using HttpClient client = Program.CreateHttpClient();
            string archivePath = Path.Combine(workDirectory, "FullRumble-Windows.zip");

            progress?.Report("Downloading release checksum...");
            string checksums = await client.GetStringAsync(release.ChecksumsUri, cancellationToken);
            string expectedHash = ReleaseMetadataParser.ParseSha256(checksums, "FullRumble-Windows.zip");

            progress?.Report("Downloading the Windows package...");
            await Program.DownloadArchiveAsync(client, release.ArchiveUri, archivePath, cancellationToken);
            string actualHash;
            await using (FileStream archiveStream = File.OpenRead(archivePath))
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken))
                    .ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expectedHash),
                    Encoding.ASCII.GetBytes(actualHash)))
                throw new InvalidDataException("The downloaded package does not match the SHA256SUMS release checksum.");

            progress?.Report("Checksum verified. Validating package...");
            Program.ExtractPackage(archivePath, stageDirectory);

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FullRumble");
            Directory.CreateDirectory(appData);
            string helperPath = Path.Combine(appData, "UpdaterApply.exe");
            File.Copy(updateHelper, helperPath, overwrite: true);

            progress?.Report("Update is ready. Close Full Rumble to finish installing it.");
            using Process helper = Process.Start(new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appData,
                ArgumentList =
                {
                    "--apply",
                    stageDirectory,
                    installRoot,
                    workDirectory,
                    gameProcessId.ToString()
                }
            }) ?? throw new InvalidOperationException("Could not start the background update helper.");
            helperStarted = true;
        }
        finally
        {
            if (!helperStarted && Directory.Exists(workDirectory))
                Directory.Delete(workDirectory, recursive: true);
        }
    }
}
