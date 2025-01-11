// Taken from https://github.com/13xforever/wgs-exporter
namespace lib.remnant2.analyzer.SaveLocation;

public sealed record WgsContainersIndex(
    string Title,
    ulong Timestamp,
    string TitleId,
    WgsContainerEntry[] Containers)
{
    public static WgsContainersIndex Read(Stream stream)
    {
        using BinaryReader reader = new(stream, BinaryReaderExtensions.Utf16, true);
        _ = reader.ReadInt32(); // unk1
        int entryCount = reader.ReadInt32();
        int tmp = reader.ReadInt32(); // unk2
        if (tmp != 0) throw new InvalidDataException("Expected unk2 to be 0");

        string title = reader.ReadUtf16String();
        ulong ts = reader.ReadUInt64(); // unk3
        _ = reader.ReadInt32(); // unk4 = 3?
        string titleId = reader.ReadUtf16String();
        _ = reader.ReadInt32(); // unk5 = 0x80000000 ?
        WgsContainerEntry[] entries = new WgsContainerEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            tmp = reader.ReadInt32(); // unk1
            if (tmp != 0) throw new InvalidDataException($"Expected unk1 in container entry #{i} to be 0");

            string cloudFilename = reader.ReadUtf16String();
            string localFilename = reader.ReadUtf16String();
            string rev = reader.ReadUtf16String();
            byte id = reader.ReadByte();
            tmp = reader.ReadInt32(); // unk2
            if (tmp != 1) throw new InvalidDataException($"Expected unk2 in container entry #{i} to be 1");

            Guid wgsFolder = reader.ReadGuid();
            ulong cts = reader.ReadUInt64(); // unk3
            long tmp64 = reader.ReadInt64(); // unk4
            if (tmp64 != 0L) throw new InvalidDataException($"Expected unk4 in container entry #{i} to be 0");

            int size = reader.ReadInt32();

            entries[i] = new(localFilename, rev, id, wgsFolder, cts, size);
        }

        tmp = reader.ReadInt32(); // unk6
        if (tmp != 0) throw new InvalidDataException("Expected unk6 to be 0");

        return new(title, ts, titleId, entries);
    }
}
