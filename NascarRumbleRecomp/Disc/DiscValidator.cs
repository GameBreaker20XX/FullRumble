using System.Security.Cryptography;

namespace FullRumble.Disc;

public enum DiscStatus
{
    NotAdded,
    Checking,
    Valid,
    Invalid,
    NeedsChoice,
}

public sealed record DiscValidationResult(
    DiscStatus Status,
    string Message,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Candidates,
    string? Serial,
    string? Sha256);

public static class DiscValidator
{
    public const string ExpectedSerial = "SLUS-01068";
    public const string ExpectedExecutable = "SLUS_010.68";

    /// <summary>SHA-256 of SLUS_010.68 measured on a real USA/Canada dump on 2026-10-29;
    /// matches the value previously published by the community ModSDK project.</summary>
    public const string ExpectedSha256 = "e90e3c7e4cf286a7a0a5e827b3a404bfe8407b15f8b2fd54536d426682b20f75";

    public static DiscValidationResult Validate(string path, string? expectedSha256 = null)
    {
        string expectedHash = expectedSha256 ?? ExpectedSha256;
        var checks = new List<string>();
        try
        {
            if (Directory.Exists(path))
            {
                var candidates = DiscFinder.Scan(path);
                if (candidates.Count == 0)
                    return Fail(checks, "No .cue, .bin or .iso found in that folder (or one level inside it).", candidates);
                if (candidates.Count > 1)
                    return new DiscValidationResult(DiscStatus.NeedsChoice,
                        "Several disc images found in that folder. Pick the one that is your NASCAR Rumble dump.",
                        checks, candidates, null, null);
                path = candidates[0];
                checks.Add($"folder scan: single image found ({Path.GetFileName(path)})");
            }

            using var image = DiscImage.Open(path);
            checks.Add($"container: {image.Format} ({Path.GetFileName(image.SourcePath)})");

            var iso = new Iso9660(image);
            checks.Add($"iso9660 volume: '{iso.VolumeId}'");

            var systemCnf = iso.Find("SYSTEM.CNF");
            if (systemCnf is null)
                return Fail(checks, "SYSTEM.CNF is missing: this is not a PlayStation game disc.", new List<string>());

            string cnf = System.Text.Encoding.ASCII.GetString(iso.ReadFile(systemCnf));
            string? boot = cnf.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("BOOT", StringComparison.OrdinalIgnoreCase));
            string serial = ParseSerial(boot);
            checks.Add($"SYSTEM.CNF boots: {serial}");

            if (!string.Equals(serial, ExpectedSerial, StringComparison.OrdinalIgnoreCase))
                return Fail(checks,
                    $"This disc is {serial}, not the USA/Canada release ({ExpectedSerial}). Other regions and other games are not supported.",
                    new List<string>());

            var executable = iso.Find(ExpectedExecutable);
            if (executable is null)
                return Fail(checks, $"{ExpectedExecutable} is missing from the disc.", new List<string>());

            byte[] bytes = iso.ReadFile(executable);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            checks.Add($"{ExpectedExecutable}: {bytes.Length} bytes, sha256 {hash}");

            if (hash != expectedHash)
                return Fail(checks,
                    $"The executable's checksum does not match the pinned USA/Canada dump. The dump is modified, truncated, or a different revision.",
                    new List<string>());

            return new DiscValidationResult(DiscStatus.Valid,
                $"Valid {ExpectedSerial} disc (volume '{iso.VolumeId}').", checks, new List<string>(), serial, hash);
        }
        catch (DiscValidationException error)
        {
            return Fail(checks, error.Message, new List<string>());
        }
        catch (IOException error)
        {
            return Fail(checks, $"Could not read the file: {error.Message}", new List<string>());
        }
    }

    public static byte[] ExtractFile(string imagePath, string discPath)
    {
        using var image = DiscImage.Open(imagePath);
        var iso = new Iso9660(image);
        var entry = iso.Find(discPath)
            ?? throw new DiscValidationException($"'{discPath}' was not found on the disc.");
        return iso.ReadFile(entry);
    }

    private static string ParseSerial(string? bootLine)
    {
        if (bootLine is null)
            return "unknown (no BOOT line)";
        int equals = bootLine.IndexOf('=');
        if (equals < 0)
            return "unknown (malformed BOOT line)";
        string value = bootLine[(equals + 1)..].Trim();
        int slash = value.LastIndexOf('\\');
        string file = slash < 0 ? value : value[(slash + 1)..];
        int semicolon = file.IndexOf(';');
        if (semicolon >= 0)
            file = file[..semicolon];
        file = file.Replace("_", "-").Replace(".", "");
        return file.Length == 0 ? "unknown (empty BOOT value)" : file;
    }

    private static DiscValidationResult Fail(List<string> checks, string message, IReadOnlyList<string> candidates) =>
        new(DiscStatus.Invalid, message, checks, candidates, null, null);
}

public static class DiscFinder
{
    private static readonly string[] Extensions = { "*.cue", "*.bin", "*.iso" };

    /// <summary>Scans a folder and one level inside it; a bare .bin next to its .cue is not listed twice.</summary>
    public static List<string> Scan(string folder)
    {
        var found = new List<string>();
        foreach (var extension in Extensions)
        {
            found.AddRange(Directory.EnumerateFiles(folder, extension, SearchOption.TopDirectoryOnly));
            foreach (var sub in Directory.EnumerateDirectories(folder))
                found.AddRange(Directory.EnumerateFiles(sub, extension, SearchOption.TopDirectoryOnly));
        }

        var cues = found
            .Where(path => Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".bin"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return found
            .Where(path => !(Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase) && cues.Contains(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
