using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PrismaMusic;

internal static class FlacTagReader
{
    internal static Track Read(string path, long length, long stamp)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        Span<byte> header = stackalloc byte[4];
        if (length < 42) return null;
        file.ReadExactly(header);
        if (!header.SequenceEqual("fLaC"u8)) return null;
        file.ReadExactly(header);
        if ((header[0] & 127) != 0 || BlockLength(header) != 34) return null;
        Span<byte> streamInfo = stackalloc byte[34];
        file.ReadExactly(streamInfo);
        ulong audio = BinaryPrimitives.ReadUInt64BigEndian(streamInfo[10..]);
        int sampleRate = (int)(audio >> 44);
        if (sampleRate == 0) return null;
        int bits = (int)((audio >> 36) & 31) + 1;
        int channels = (int)((audio >> 41) & 7) + 1;
        double duration = (audio & 0xFFFFFFFFFUL) / (double)sampleRate;
        int seconds = (int)Math.Min(int.MaxValue, duration);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while ((header[0] & 128) == 0)
        {
            if (length - file.Position < header.Length) return null;
            file.ReadExactly(header);
            int size = BlockLength(header);
            if (size > length - file.Position) return null;
            if ((header[0] & 127) == 4)
            {
                // Larger comment blocks can contain legacy base64 artwork. Keep the full reader as a fallback.
                if (size > 1024 * 1024) return null;
                var data = new byte[size];
                file.ReadExactly(data);
                if (!ReadComments(data, tags)) return null;
            }
            else
            {
                // Artwork is loaded lazily by the gallery or player. Avoid fetching it over SMB during indexing.
                file.Seek(size, SeekOrigin.Current);
            }
        }

        if (file.Position >= length) return null;
        return new Track(path, Value("TITLE") ?? Path.GetFileNameWithoutExtension(path),
            Value("ARTIST", "ALBUMARTIST", "ALBUM ARTIST", "ALBUM_ARTIST") ?? "Artista desconocido",
            Value("ALBUM") ?? "Sin álbum", Value("ALBUMARTIST", "ALBUM ARTIST", "ALBUM_ARTIST") ?? "",
            Value("GENRE") ?? "", Number(Value("DATE", "YEAR", "CREATIONDATE"), '-'),
            Number(Value("DISCNUMBER", "DISC"), '/'), Number(Value("TRACKNUMBER", "TRACK"), '/'),
            seconds, "FLAC", sampleRate, bits, length, stamp,
            AudioMetadata.Kilobits(length - file.Position, duration), channels);

        string Value(params string[] keys)
        {
            foreach (string key in keys)
                if (tags.TryGetValue(key, out string value)) return value;
            return null;
        }
    }

    private static int BlockLength(ReadOnlySpan<byte> header) => (header[1] << 16) | (header[2] << 8) | header[3];

    private static uint Number(string value, char separator)
    {
        ReadOnlySpan<char> number = value.AsSpan();
        int end = number.IndexOf(separator);
        if (end >= 0) number = number[..end];
        return uint.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0;
    }

    private static bool ReadComments(ReadOnlySpan<byte> block, Dictionary<string, string> tags)
    {
        if (!TakeField(ref block, out _) || block.Length < sizeof(uint)) return false;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(block);
        block = block[sizeof(uint)..];
        for (uint i = 0; i < count; i++)
        {
            if (!TakeField(ref block, out var field)) return false;
            int separator = field.IndexOf((byte)'=');
            if (separator < 1) continue;
            string key = Encoding.ASCII.GetString(field[..separator]).ToUpperInvariant();
            if (key is not ("TITLE" or "ARTIST" or "ALBUM" or "ALBUMARTIST" or "ALBUM ARTIST" or "ALBUM_ARTIST"
                or "GENRE" or "DATE" or "YEAR" or "CREATIONDATE" or "TRACKNUMBER" or "TRACK" or "DISCNUMBER" or "DISC")) continue;
            string value = Encoding.UTF8.GetString(field[(separator + 1)..]).Trim();
            if (value.Length != 0) tags.TryAdd(key, value);
        }
        return true;
    }

    private static bool TakeField(ref ReadOnlySpan<byte> block, out ReadOnlySpan<byte> field)
    {
        field = default;
        if (block.Length < sizeof(uint)) return false;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(block);
        block = block[sizeof(uint)..];
        if (size > block.Length) return false;
        field = block[..(int)size];
        block = block[(int)size..];
        return true;
    }
}
