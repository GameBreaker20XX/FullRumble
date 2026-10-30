using System.Globalization;
using System.Text.RegularExpressions;

namespace FullRumble.Disc;

public enum DiscFormat
{
    RawMode1,
    RawMode2,
    Cooked,
}

/// <summary>Uniform 2048-byte sector access over .cue+.bin, bare raw .bin and cooked .iso.</summary>
public sealed class DiscImage : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _sectorSize;
    private readonly int _dataOffset;
    private long _firstSectorOffset;

    public DiscFormat Format { get; }
    public string SourcePath { get; }
    public string? VolumeLabelHint { get; init; }
    public long SectorCount { get; private set; }

    private DiscImage(FileStream stream, DiscFormat format, int sectorSize, int dataOffset, string sourcePath)
    {
        _stream = stream;
        Format = format;
        _sectorSize = sectorSize;
        _dataOffset = dataOffset;
        SourcePath = sourcePath;
        SectorCount = stream.Length / sectorSize;
    }

    public static DiscImage Open(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".zip" or ".7z" or ".rar":
                throw new DiscValidationException(
                    "Archives are not supported. Extract the disc image inside first, then point FullRumble at the .cue, .bin or .iso.");
            case ".chd" or ".pbp":
                throw new DiscValidationException(
                    $"{extension.Trim('.').ToUpperInvariant()} images are not implemented. Convert your dump to .bin/.cue or .iso first.");
            case ".cue":
                return OpenCue(path);
            case ".bin" or ".iso" or ".img":
                return OpenImage(path, cuePath: null);
            default:
                throw new DiscValidationException(
                    $"Unsupported file type '{extension}'. Provide a .cue with its .bin, a bare raw .bin, or a cooked .iso.");
        }
    }

    private static DiscImage OpenCue(string cuePath)
    {
        var cue = CueSheet.Parse(File.ReadAllText(cuePath));
        if (cue.DataTracks.Count == 0)
            throw new DiscValidationException("The .cue lists no data track, so it cannot hold the game.");
        if (cue.DataTracks.Count > 1)
            throw new DiscValidationException(
                "The game's data is spread over more than one track in this .cue. Only dumps whose data sits in a single track are supported.");

        var track = cue.DataTracks[0];
        string binPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cuePath))!, track.File);
        if (!File.Exists(binPath))
            throw new DiscValidationException(
                $"The .cue and its .bin must stay together: missing '{track.File}' next to the .cue.");

        var image = OpenImage(binPath, cuePath);
        return image.WithTrackOffset(track.IndexFrames);
    }

    private DiscImage WithTrackOffset(long frames)
    {
        _firstSectorOffset = frames;
        SectorCount = _stream.Length / _sectorSize - frames;
        return this;
    }

    public static DiscImage OpenImage(string path, string? cuePath)
    {
        var stream = File.OpenRead(path);
        try
        {
            long length = stream.Length;
            if (length == 0)
                throw new DiscValidationException($"'{Path.GetFileName(path)}' is empty.");

            var probe = new byte[2352];
            int read = stream.Read(probe, 0, probe.Length);
            bool hasSync = read >= 16 && probe[0] == 0x00 && probe[1] == 0xFF && probe[11] == 0x00;

            if (length % 2352 == 0 && hasSync)
            {
                int mode = probe[15];
                int dataOffset = mode switch
                {
                    1 => 16,
                    2 => 24,
                    _ => throw new DiscValidationException(
                        $"Unrecognised sector mode {mode} in '{Path.GetFileName(path)}'."),
                };
                var format = mode == 1 ? DiscFormat.RawMode1 : DiscFormat.RawMode2;
                return new DiscImage(stream, format, 2352, dataOffset, path);
            }

            if (length % 2048 == 0 && LooksCooked(stream))
                return new DiscImage(stream, DiscFormat.Cooked, 2048, 0, path);

            throw new DiscValidationException(
                $"'{Path.GetFileName(path)}' is neither a raw 2352-byte-sector image nor a cooked 2048-byte ISO. The dump may be truncated or in another format.");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool LooksCooked(FileStream stream)
    {
        var buffer = new byte[5];
        stream.Seek(16 * 2048 + 1, SeekOrigin.Begin);
        return stream.Read(buffer, 0, 5) == 5 && System.Text.Encoding.ASCII.GetString(buffer) == "CD001";
    }

    public byte[] ReadSectors(long lba, int count)
    {
        long absolute = lba + _firstSectorOffset;
        if (absolute < 0 || absolute + count > _stream.Length / _sectorSize)
            throw new DiscValidationException(
                $"The image ended while reading sector {lba}: the dump is truncated.");

        var result = new byte[count * 2048];
        var sector = new byte[_sectorSize];
        for (int i = 0; i < count; i++)
        {
            _stream.Seek((absolute + i) * _sectorSize, SeekOrigin.Begin);
            int read = _stream.Read(sector, 0, _sectorSize);
            if (read < _sectorSize)
                throw new DiscValidationException("The image ended mid-sector: the dump is truncated.");
            Array.Copy(sector, _dataOffset, result, i * 2048, 2048);
        }

        return result;
    }

    /// <summary>Reframes any supported input into a cooked 2048-byte ISO on disk.</summary>
    public void ExportCookedIso(string destination)
    {
        using var output = File.Create(destination);
        var buffer = new byte[2048];
        for (long lba = 0; lba < SectorCount; lba++)
        {
            ReadSectors(lba, 1).CopyTo(buffer, 0);
            output.Write(buffer, 0, 2048);
        }
    }

    public void Dispose() => _stream.Dispose();
}

public sealed record CueTrack(string File, string Mode, long IndexFrames, bool IsData);

public sealed class CueSheet
{
    public List<CueTrack> DataTracks { get; } = new();
    public List<CueTrack> AllTracks { get; } = new();

    public static CueSheet Parse(string text)
    {
        var sheet = new CueSheet();
        string? currentFile = null;
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            var fileMatch = Regex.Match(line, "^FILE\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (fileMatch.Success)
            {
                currentFile = fileMatch.Groups[1].Value;
                continue;
            }

            var trackMatch = Regex.Match(line, "^TRACK\\s+(\\d+)\\s+(\\S+)", RegexOptions.IgnoreCase);
            if (trackMatch.Success && currentFile is not null)
            {
                sheet._pendingTrack = (currentFile, trackMatch.Groups[2].Value.ToUpperInvariant());
                continue;
            }

            var indexMatch = Regex.Match(line, "^INDEX\\s+01\\s+(\\d+):(\\d+):(\\d+)", RegexOptions.IgnoreCase);
            if (indexMatch.Success && sheet._pendingTrack is { } pending)
            {
                long frames = int.Parse(indexMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 4500
                    + int.Parse(indexMatch.Groups[2].Value, CultureInfo.InvariantCulture) * 75
                    + int.Parse(indexMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                bool isData = pending.Mode.StartsWith("MODE", StringComparison.OrdinalIgnoreCase);
                var track = new CueTrack(pending.File, pending.Mode, frames, isData);
                sheet.AllTracks.Add(track);
                if (isData)
                    sheet.DataTracks.Add(track);
                sheet._pendingTrack = null;
            }
        }

        return sheet;
    }

    private (string File, string Mode)? _pendingTrack;
}

public class DiscValidationException : Exception
{
    public DiscValidationException(string message) : base(message)
    {
    }
}
