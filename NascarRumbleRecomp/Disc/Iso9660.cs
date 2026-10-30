using System.Text;

namespace FullRumble.Disc;

public sealed record IsoEntry(string Name, long Lba, long Size, bool IsDirectory);

/// <summary>Minimal ISO9660 reader: primary volume descriptor, directory walk, file read.</summary>
public sealed class Iso9660
{
    private readonly DiscImage _image;

    public string VolumeId { get; }
    private readonly IsoEntry _root;

    public Iso9660(DiscImage image)
    {
        _image = image;
        var pvd = image.ReadSectors(16, 1);
        if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
            throw new DiscValidationException(
                "No ISO9660 volume descriptor found at sector 16. This is not a PS1 game disc image, or the dump mode is wrong.");

        VolumeId = Encoding.ASCII.GetString(pvd, 40, 32).Trim();
        _root = new IsoEntry(
            "",
            BitConverter.ToUInt32(pvd, 156 + 2),
            BitConverter.ToUInt32(pvd, 156 + 10),
            true);
    }

    public List<IsoEntry> ReadDirectory(IsoEntry directory)
    {
        var entries = new List<IsoEntry>();
        int sectorCount = (int)((directory.Size + 2047) / 2048);
        var data = _image.ReadSectors(directory.Lba, sectorCount);
        int offset = 0;
        while (offset < directory.Size)
        {
            int length = data[offset];
            if (length == 0)
            {
                offset = (offset / 2048 + 1) * 2048;
                continue;
            }

            int nameLength = data[offset + 32];
            string name = Encoding.ASCII.GetString(data, offset + 33, nameLength);
            entries.Add(new IsoEntry(
                name,
                BitConverter.ToUInt32(data, offset + 2),
                BitConverter.ToUInt32(data, offset + 10),
                (data[offset + 25] & 2) != 0));
            offset += length;
        }

        return entries;
    }

    public IsoEntry? Find(string path)
    {
        var current = _root;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.IsDirectory)
                return null;
            var match = ReadDirectory(current).FirstOrDefault(entry =>
                entry.IsDirectory
                    ? string.Equals(entry.Name, segment, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(StripVersion(entry.Name), segment, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return null;
            current = match;
        }

        return current.IsDirectory ? null : current;
    }

    public List<IsoEntry> Root() => ReadDirectory(_root);

    public byte[] ReadFile(IsoEntry entry)
    {
        int sectors = (int)((entry.Size + 2047) / 2048);
        var data = _image.ReadSectors(entry.Lba, sectors);
        Array.Resize(ref data, (int)entry.Size);
        return data;
    }

    private static string StripVersion(string name)
    {
        int semicolon = name.IndexOf(';');
        return semicolon < 0 ? name : name[..semicolon];
    }
}
