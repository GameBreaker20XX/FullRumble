using System.Text;

namespace NascarRumbleRecomp.Tests;

/// <summary>Builds tiny synthetic ISO9660 images (cooked, then optionally wrapped raw) for tests.</summary>
public static class SyntheticIso
{
    public const int Sector = 2048;
    public const int RawSector = 2352;

    public static byte[] BuildCooked(string volumeId, string serial, byte[] executable)
    {
        byte[] systemCnf = Encoding.ASCII.GetBytes($"BOOT = cdrom:\\{serial.Replace('-', '_')};1\r\nTCB = 4\r\nEVENT = 10\r\nSTACK = 801fff00\r\n");
        int cnfSectors = (systemCnf.Length + Sector - 1) / Sector;
        int exeSectors = (executable.Length + Sector - 1) / Sector;

        long rootLba = 17;
        long cnfLba = 18;
        long exeLba = cnfLba + cnfSectors;
        long totalSectors = exeLba + exeSectors;

        var image = new byte[totalSectors * Sector];

        // Primary volume descriptor
        image[16 * Sector] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(image, 16 * Sector + 1);
        image[16 * Sector + 6] = 1;
        WritePadded(image, 16 * Sector + 40, volumeId, 32);
        WriteDirectoryRecord(image, 16 * Sector + 156, rootLba, 2048, "\0", directory: true);

        // Root directory: self, parent, SYSTEM.CNF;1, SLUS_010.68;1
        int offset = (int)rootLba * Sector;
        offset += WriteDirectoryRecord(image, offset, rootLba, 2048, "\0", directory: true);
        offset += WriteDirectoryRecord(image, offset, rootLba, 2048, "\u0001", directory: true);
        offset += WriteDirectoryRecord(image, offset, cnfLba, systemCnf.Length, "SYSTEM.CNF;1", directory: false);
        offset += WriteDirectoryRecord(image, offset, exeLba, executable.Length, "SLUS_010.68;1", directory: false);

        systemCnf.CopyTo(image, (int)cnfLba * Sector);
        executable.CopyTo(image, (int)exeLba * Sector);
        return image;
    }

    public static byte[] WrapRaw(byte[] cooked)
    {
        int sectors = cooked.Length / Sector;
        var raw = new byte[sectors * RawSector];
        for (int i = 0; i < sectors; i++)
        {
            int destination = i * RawSector;
            raw[destination] = 0x00;
            for (int b = 1; b <= 10; b++)
                raw[destination + b] = 0xFF;
            raw[destination + 11] = 0x00;
            int msf = i + 150;
            raw[destination + 12] = (byte)(msf / 4500);
            raw[destination + 13] = (byte)(msf / 75 % 60);
            raw[destination + 14] = (byte)(msf % 75);
            raw[destination + 15] = 2;
            raw[destination + 18] = 0x18;
            Array.Copy(cooked, i * Sector, raw, destination + 24, Sector);
        }

        return raw;
    }

    /// <summary>Two seconds of lead-in style sectors, as a dump that carries a pregap would have.</summary>
    public static byte[] Pregap(int frames)
    {
        var raw = new byte[frames * RawSector];
        for (int i = 0; i < frames; i++)
        {
            int destination = i * RawSector;
            raw[destination] = 0x00;
            for (int b = 1; b <= 10; b++)
                raw[destination + b] = 0xFF;
            raw[destination + 11] = 0x00;
            raw[destination + 15] = 2;
            raw[destination + 18] = 0x18;
        }

        return raw;
    }

    public static string CueFor(string binName) =>
        $"FILE \"{binName}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n";

    public static string AudioPlusDataCue(string audioBin, string dataBin) =>
        $"FILE \"{audioBin}\" BINARY\r\n  TRACK 01 AUDIO\r\n    INDEX 01 00:00:00\r\n" +
        $"FILE \"{dataBin}\" BINARY\r\n  TRACK 02 MODE2/2352\r\n    INDEX 01 00:00:00\r\n";

    public static string TwoDataTracksCue(string binName) =>
        $"FILE \"{binName}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
        $"  TRACK 02 MODE2/2352\r\n    INDEX 01 00:10:00\r\n";

    private static int WriteDirectoryRecord(byte[] image, int offset, long lba, long size, string name, bool directory)
    {
        byte[] nameBytes = name.Length <= 1 ? new[] { (byte)name[0] } : Encoding.ASCII.GetBytes(name);
        int length = 33 + nameBytes.Length;
        if (length % 2 == 1)
            length++;

        image[offset] = (byte)length;
        WriteBothEndian(image, offset + 2, lba);
        WriteBothEndian(image, offset + 10, size);
        image[offset + 25] = (byte)(directory ? 2 : 0);
        WriteBothEndian(image, offset + 28, 1);
        image[offset + 32] = (byte)nameBytes.Length;
        nameBytes.CopyTo(image, offset + 33);
        return length;
    }

    private static void WriteBothEndian(byte[] image, int offset, long value)
    {
        image[offset] = (byte)(value & 0xFF);
        image[offset + 1] = (byte)(value >> 8 & 0xFF);
        image[offset + 2] = (byte)(value >> 16 & 0xFF);
        image[offset + 3] = (byte)(value >> 24 & 0xFF);
        image[offset + 4] = (byte)(value >> 24 & 0xFF);
        image[offset + 5] = (byte)(value >> 16 & 0xFF);
        image[offset + 6] = (byte)(value >> 8 & 0xFF);
        image[offset + 7] = (byte)(value & 0xFF);
    }

    private static void WritePadded(byte[] image, int offset, string value, int width)
    {
        var bytes = Encoding.ASCII.GetBytes(value.PadRight(width, ' '));
        Array.Copy(bytes, 0, image, offset, width);
    }
}
