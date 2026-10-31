using System.Text;

namespace FullRumble.Disc;

/// <summary>
/// Keeps an accepted disc inside a folder the game owns, so every later launch boots straight in
/// without the player picking anything again. The player's own dump folder is only ever read from:
/// nothing there is moved, renamed or deleted.
/// </summary>
public static class DiscLibrary
{
    private const string FolderName = "disc";
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    private static string? _folder;

    /// <summary>The game's disc folder, next to the executable when that is writable.</summary>
    public static string Folder => _folder ??= ResolveFolder();

    /// <summary>
    /// Copies <paramref name="source"/> and, for a .cue, the image files it points at into the disc
    /// folder, then returns the .cue to boot from that copy. Falls back to <paramref name="source"/>
    /// if staging is impossible, so a full or locked disk never stops the game from starting.
    /// </summary>
    public static string EnsureStaged(string source, string? folder = null, Action<string>? log = null)
    {
        void Say(string line) => log?.Invoke(line);
        try
        {
            string image = Path.GetFullPath(ResolveImage(source));
            string target = Path.GetFullPath(folder ?? Folder);
            bool isCue = Path.GetExtension(image).Equals(".cue", IgnoreCase);
            if (isCue && SameFolder(image, target))
                return image;

            Directory.CreateDirectory(target);
            List<CueTrack> tracks;
            if (isCue)
            {
                tracks = CueSheet.Parse(File.ReadAllText(image)).AllTracks;
                string origin = Path.GetDirectoryName(image)!;
                CueTrack? data = tracks.Find(t => t.IsData);
                if (!File.Exists(Path.Combine(origin, data?.File ?? "")))
                    throw new DiscValidationException(
                        $"'{Path.GetFileName(data?.File ?? "the .bin")}' is missing next to the .cue.");
                foreach (string referenced in tracks.Select(t => Path.Combine(origin, t.File))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(referenced))
                    {
                        Say($"left out, not found next to the .cue: {Path.GetFileName(referenced)}");
                        continue;
                    }

                    Copy(referenced, Path.Combine(target, Path.GetFileName(referenced)), Say);
                }
            }
            else
            {
                Copy(image, Path.Combine(target, Path.GetFileName(image)), Say);
                tracks = TracksForLoneImage(image);
            }

            string cuePath = Path.Combine(target, Path.GetFileNameWithoutExtension(image) + ".cue");
            string cue = RenderCue(tracks);
            if (!File.Exists(cuePath) || File.ReadAllText(cuePath) != cue)
            {
                File.WriteAllText(cuePath, cue, Encoding.ASCII);
                Say($"cue sheet written: {cuePath}");
            }

            return cuePath;
        }
        catch (Exception error)
        {
            Say($"disc was not copied into the game folder ({error.Message}); using it where it is");
            return source;
        }
    }

    private static string ResolveFolder()
    {
        string nextToGame = Path.Combine(AppContext.BaseDirectory, FolderName);
        return CanWrite(nextToGame)
            ? nextToGame
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FullRumble", FolderName);
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, "write-probe.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SameFolder(string file, string folder) =>
        string.Equals(Path.GetDirectoryName(file), folder, IgnoreCase);

    /// <summary>A folder counts as the single image inside it, matching what validation accepted.</summary>
    private static string ResolveImage(string source)
    {
        if (!Directory.Exists(source))
            return source;

        List<string> found = DiscFinder.Scan(source);
        return found.Count == 1
            ? found[0]
            : throw new DiscValidationException(
                $"That folder holds {found.Count} disc images, so there is no single disc to copy.");
    }

    private static void Copy(string source, string destination, Action<string> say)
    {
        var origin = new FileInfo(source);
        var existing = new FileInfo(destination);
        if (existing.Exists && existing.Length == origin.Length)
        {
            say($"already in the game folder: {existing.Name}");
            return;
        }

        say($"copying {origin.Name} ({origin.Length / (1024 * 1024)} MB) into the game folder");
        // Via a temporary name so an interrupted copy is never mistaken for a finished one.
        string partial = destination + ".part";
        using (var input = File.OpenRead(source))
        using (var output = File.Create(partial))
            input.CopyTo(output, 1 << 20);
        File.Move(partial, destination, overwrite: true);
    }

    /// <summary>Describes a bare .bin or .iso as the one-track cue sheet the runtime can read.</summary>
    private static List<CueTrack> TracksForLoneImage(string imagePath)
    {
        using var image = DiscImage.Open(imagePath);
        string mode = image.Format switch
        {
            DiscFormat.Cooked => "MODE1/2048",
            DiscFormat.RawMode1 => "MODE1/2352",
            _ => "MODE2/2352",
        };
        long frames = image.Format == DiscFormat.Cooked ? 0 : image.DataTrackStartFrame;
        return [new CueTrack(Path.GetFileName(imagePath), mode, frames, IsData: true)];
    }

    private static string RenderCue(List<CueTrack> tracks)
    {
        var cue = new StringBuilder();
        string? currentFile = null;
        int number = 0;
        foreach (CueTrack track in tracks)
        {
            string file = Path.GetFileName(track.File);
            if (!string.Equals(file, currentFile, IgnoreCase))
            {
                cue.Append("FILE \"").Append(file).Append("\" BINARY\r\n");
                currentFile = file;
            }

            cue.Append("  TRACK ").Append((++number).ToString("00")).Append(' ')
                .Append(track.Mode).Append("\r\n");
            cue.Append("    INDEX 01 ").Append(ToMsf(track.IndexFrames)).Append("\r\n");
        }

        return cue.ToString();
    }

    private static string ToMsf(long frames) =>
        $"{frames / 4500:00}:{frames / 75 % 60:00}:{frames % 75:00}";
}
