using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullRumble.Updater;

public sealed record ReleaseMetadata(
    string Name,
    string Tag,
    Version Version,
    Uri ArchiveUri,
    Uri ChecksumsUri);

public static partial class ReleaseMetadataParser
{
    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public static Version ParseVersionTag(string tag)
    {
        string value = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Version.TryParse(value, out Version? version) ||
            version.Major < 0 || version.Minor < 0 || version.Build < 0 || version.Revision < 0)
            throw new InvalidDataException($"Release tag '{tag}' is not a valid version tag.");
        return version;
    }

    public static ReleaseMetadata ParseLatestRelease(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string name = RequiredString(root, "name");
        string tag = RequiredString(root, "tag_name");
        Version version = ParseVersionTag(tag);
        JsonElement assets = root.GetProperty("assets");
        if (assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The latest release has no assets list.");

        Uri archive = FindAsset(assets, "FullRumble-Windows.zip");
        Uri checksums = FindAsset(assets, "SHA256SUMS");
        return new ReleaseMetadata(name, tag, version, archive, checksums);
    }

    public static string ParseSha256(string checksums, string assetName)
    {
        string? found = null;
        foreach (string line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || !Sha256Pattern().IsMatch(fields[0]))
                continue;

            string name = fields[1].TrimStart('*').Trim();
            if (!string.Equals(name, assetName, StringComparison.Ordinal))
                continue;
            if (found is not null)
                throw new InvalidDataException($"The checksum file contains multiple entries for {assetName}.");
            found = fields[0].ToLowerInvariant();
        }

        return found ?? throw new InvalidDataException($"No valid SHA-256 entry was found for {assetName}.");
    }

    public static string GetSafeRelativePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || archivePath.Contains('\0'))
            throw new InvalidDataException("The release archive contains an empty or invalid path.");

        string normalized = archivePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"The release archive contains a rooted path: {archivePath}");

        string[] segments = normalized.TrimEnd('/').Split('/');
        if (segments.Length == 0 || segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                segment.EndsWith('.') ||
                segment.EndsWith(' ') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.Contains(':') ||
                IsReservedWindowsName(segment)))
            throw new InvalidDataException($"The release archive contains an unsafe path: {archivePath}");

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static bool IsDiscPath(string relativePath) =>
        string.Equals(relativePath.Replace('\\', '/').Split('/', 2)[0], "disc",
            StringComparison.OrdinalIgnoreCase);

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"The latest release is missing '{property}'.");
        return value.GetString()!;
    }

    private static Uri FindAsset(JsonElement assets, string assetName)
    {
        Uri? result = null;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out JsonElement name) ||
                name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), assetName, StringComparison.Ordinal))
                continue;

            string url = RequiredString(asset, "browser_download_url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The release asset URL for {assetName} is not a trusted HTTPS URL.");
            if (result is not null)
                throw new InvalidDataException($"The latest release contains duplicate {assetName} assets.");
            result = uri;
        }

        return result ?? throw new InvalidDataException($"The latest release is missing {assetName}.");
    }

    private static bool IsReservedWindowsName(string segment)
    {
        string name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(name, @"^(COM|LPT)[1-9]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
