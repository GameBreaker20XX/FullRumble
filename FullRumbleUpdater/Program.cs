using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace FullRumble.Updater;

internal static class Program
{
    private const long MaxArchiveBytes = 1024L * 1024 * 1024;
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int ShowErrorMessage(nint window, string text, string caption, uint type);

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 5 && args[0] == "--apply")
                return ApplyUpdate(args[1], args[2], args[3], args[4]);
            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine(typeof(Program).Assembly.GetName().Version);
                return 0;
            }
            Console.Error.WriteLine("This is an internal update helper. Use Settings > Updates in Full Rumble.");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Updater failed: {error.Message}");
            if (args.Length > 0 && args[0] == "--apply")
                ShowErrorMessage(0, error.Message, "Full Rumble update failed", 0x10);
            return 1;
        }
    }

    internal static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FullRumbleUpdater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static Version ReadInstalledVersion(string gamePath)
    {
        string? value = FileVersionInfo.GetVersionInfo(gamePath).FileVersion;
        if (!Version.TryParse(value, out Version? version))
            throw new InvalidDataException($"Could not read the installed Full Rumble version from {gamePath}.");
        return version;
    }

    internal static async Task DownloadArchiveAsync(
        HttpClient client,
        Uri uri,
        string destination,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > MaxArchiveBytes)
            throw new InvalidDataException("The release package exceeds the updater's 1 GiB size limit.");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxArchiveBytes)
                throw new InvalidDataException("The release package exceeds the updater's 1 GiB size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    internal static void ExtractPackage(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        bool hasGame = false;
        bool hasUpdateHelper = false;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relative = ReleaseMetadataParser.GetSafeRelativePath(entry.FullName);
            if (!names.Add(relative))
                throw new InvalidDataException($"The release archive contains a duplicate path: {entry.FullName}");
            if (ReleaseMetadataParser.IsDiscPath(relative))
                continue;

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                string directory = ResolveWithinRoot(root, relative);
                Directory.CreateDirectory(directory);
                continue;
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException("The expanded release package exceeds the updater's 2 GiB limit.");
            if (entry.Length > 0 && (long)entry.CompressedLength * 1000 < entry.Length)
                throw new InvalidDataException($"The release archive contains an unusually compressed file: {entry.FullName}");

            string target = ResolveWithinRoot(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream input = entry.Open();
            using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);

            hasGame |= string.Equals(relative, "FullRumble.exe", StringComparison.OrdinalIgnoreCase);
            hasUpdateHelper |= string.Equals(relative, "FullRumbleUpdateHelper.exe", StringComparison.OrdinalIgnoreCase);
        }

        if (!hasGame || !hasUpdateHelper)
            throw new InvalidDataException("The release package is missing FullRumble.exe or FullRumbleUpdateHelper.exe.");
    }

    internal static string ResolveWithinRoot(string root, string relative)
    {
        string destination = Path.GetFullPath(Path.Combine(root, relative));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The release archive path escapes its extraction folder: {relative}");
        return destination;
    }

    private static int ApplyUpdate(string stageDirectory, string installDirectory, string workDirectory, string parentPid)
    {
        if (!int.TryParse(parentPid, out int processId) || processId <= 0)
            throw new InvalidDataException("The update helper received an invalid parent process id.");

        using (Process? parent = TryGetProcess(processId))
            parent?.WaitForExit();
        if (IsGameRunning())
            throw new InvalidOperationException("Full Rumble is running. Close it before applying this update.");

        string installRoot = Path.GetFullPath(installDirectory);
        string stageRoot = Path.GetFullPath(stageDirectory);
        if (!File.Exists(Path.Combine(stageRoot, "FullRumble.exe")) ||
            !File.Exists(Path.Combine(stageRoot, "FullRumbleUpdateHelper.exe")))
            throw new InvalidDataException("The staged update is incomplete.");

        Console.WriteLine("Installing verified update files...");
        ApplyPackageFiles(stageRoot, installRoot, Path.Combine(workDirectory, "backup"));

        string gamePath = Path.Combine(installRoot, "FullRumble.exe");
        using Process game = Process.Start(new ProcessStartInfo(gamePath)
        {
            UseShellExecute = true,
            WorkingDirectory = installRoot
        }) ?? throw new InvalidOperationException("The update installed, but FullRumble.exe could not be restarted.");
        Console.WriteLine("Update installed. Full Rumble is starting.");
        TryDeleteDirectory(workDirectory);
        return 0;
    }

    internal static void ApplyPackageFiles(string stageRoot, string installRoot, string backupRoot)
    {
        string[] files = Directory.GetFiles(stageRoot, "*", SearchOption.AllDirectories);
        var applied = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (string source in files)
            {
                string relative = Path.GetRelativePath(stageRoot, source);
                if (ReleaseMetadataParser.IsDiscPath(relative))
                    continue;

                string destination = ResolveWithinRoot(installRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = ResolveWithinRoot(backupRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                }
                applied.Add((destination, backup));
                File.Copy(source, destination, overwrite: true);
            }
        }
        catch (Exception updateError)
        {
            var rollbackErrors = new List<Exception>();
            foreach ((string destination, string? backup) in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (backup is not null)
                        File.Copy(backup, destination, overwrite: true);
                    else if (File.Exists(destination))
                        File.Delete(destination);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count > 0)
                throw new AggregateException("Update failed and one or more files could not be restored.", [updateError, .. rollbackErrors]);
            throw new IOException("Update failed; changed files were rolled back.", updateError);
        }
    }

    private static bool IsGameRunning()
    {
        foreach (Process process in Process.GetProcessesByName("FullRumble"))
        {
            using (process)
                if (!process.HasExited)
                    return true;
        }
        return false;
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException error)
        {
            Console.Error.WriteLine($"Update finished, but temporary files remain at {path}: {error.Message}");
        }
        catch (UnauthorizedAccessException error)
        {
            Console.Error.WriteLine($"Update finished, but temporary files could not be removed from {path}: {error.Message}");
        }
    }
}
