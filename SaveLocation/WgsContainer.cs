// Taken from https://github.com/13xforever/wgs-exporter
namespace lib.remnant2.analyzer.SaveLocation;

public sealed record WgsContainer(WgsBlob[] Blobs)
{
    public static WgsContainer Read(Stream stream)
    {
        using BinaryReader reader = new BinaryReader(stream, BinaryReaderExtensions.Utf16, true);
        int tmp = reader.ReadInt32(); // unk1
        if (tmp != 4)
            throw new InvalidDataException("Expected unk1 to be 4");

        int count = reader.ReadInt32();
        WgsBlob[] entries = new WgsBlob[count];
        Span<byte> buf = stackalloc byte[64 * 2];
        for (int i = 0; i < count; i++)
        {
            stream.ReadExactly(buf);
            string name = BinaryReaderExtensions.Utf16.GetString(buf).TrimEnd('\0');
            reader.ReadGuid();
            Guid guid2 = reader.ReadGuid();

            entries[i] = new(name, guid2);
        }
        return new(entries);
    }
}
