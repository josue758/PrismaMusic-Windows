using System;
using System.Buffers.Binary;
using System.IO;

namespace PrismaMusic;

internal static class Mp4AudioMetadata
{
    internal readonly record struct Info(string Codec, int SampleRate, int BitDepth, int Channels, int BitrateKbps);
    private readonly record struct Box(long Start, long End);

    internal static Info Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024);
        var movie = Find(file, 0, file.Length, "moov"u8);
        Span<byte> type = stackalloc byte[4];
        Span<byte> entry = stackalloc byte[28];
        Span<byte> cookie = stackalloc byte[24];
        for (long next = movie.Start; next < movie.End;)
        {
            var track = Find(file, next, movie.End, "trak"u8);
            if (track.End == 0) break;
            next = track.End;
            var media = Child(file, track, "mdia"u8);
            var handler = Child(file, media, "hdlr"u8);
            if (!ReadAt(file, handler, 8, type) || !type.SequenceEqual("soun"u8)) continue;

            var table = Child(file, Child(file, media, "minf"u8), "stbl"u8);
            var descriptions = Child(file, table, "stsd"u8);
            var alac = Find(file, descriptions.Start + 8, descriptions.End, "alac"u8);
            int bitrate = AudioMetadata.Kilobits(SampleBytes(file, Child(file, table, "stsz"u8)),
                Duration(file, Child(file, media, "mdhd"u8)));
            if (alac.End == 0)
                return new(null, 0, 0, 0, bitrate);

            if (!ReadAt(file, alac, 0, entry)) return new("ALAC", 0, 0, 0, bitrate);
            int version = BinaryPrimitives.ReadUInt16BigEndian(entry[8..]);
            long children = alac.Start + entry.Length + (version == 1 ? 16 : version == 2 ? 36 : 0);
            var config = Find(file, children, alac.End, "alac"u8);
            if (config.End == 0)
            {
                var wave = Find(file, children, alac.End, "wave"u8);
                config = Child(file, wave, "alac"u8);
            }
            if (!ReadAt(file, config, 4, cookie)) return new("ALAC", 0, 0, 0, bitrate);
            int declared = (int)Math.Round(BinaryPrimitives.ReadUInt32BigEndian(cookie[16..]) / 1000d);
            int rate = (int)BinaryPrimitives.ReadUInt32BigEndian(cookie[20..]);
            return new("ALAC", rate, cookie[5], cookie[9], bitrate > 0 ? bitrate : declared);
        }
        return default;
    }

    private static double Duration(Stream file, Box header)
    {
        Span<byte> data = stackalloc byte[32];
        if (!ReadAt(file, header, 0, data[..20])) return 0;
        if (data[0] == 0)
        {
            uint scale = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
            uint duration = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
            return scale == 0 || duration == uint.MaxValue ? 0 : duration / (double)scale;
        }
        if (data[0] != 1 || !ReadAt(file, header, 0, data)) return 0;
        uint timeScale = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        ulong ticks = BinaryPrimitives.ReadUInt64BigEndian(data[24..]);
        return timeScale == 0 || ticks == ulong.MaxValue ? 0 : ticks / (double)timeScale;
    }

    private static long SampleBytes(Stream file, Box sizes)
    {
        Span<byte> header = stackalloc byte[12];
        if (!ReadAt(file, sizes, 0, header)) return 0;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        uint count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        if (size > 0)
        {
            ulong fixedTotal = (ulong)size * count;
            return fixedTotal <= (ulong)file.Length ? (long)fixedTotal : 0;
        }
        if (count > (sizes.End - file.Position) / 4) return 0;
        long total = 0;
        Span<byte> data = stackalloc byte[4096];
        while (count > 0)
        {
            int bytes = (int)Math.Min(count, (uint)(data.Length / 4)) * 4;
            if (!file.TryReadExactly(data[..bytes])) return 0;
            for (int offset = 0; offset < bytes; offset += 4)
                total += BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            count -= (uint)(bytes / 4);
        }
        return total <= file.Length ? total : 0;
    }

    private static Box Child(Stream file, Box parent, ReadOnlySpan<byte> name) => Find(file, parent.Start, parent.End, name);

    private static Box Find(Stream file, long start, long end, ReadOnlySpan<byte> name)
    {
        uint type = BinaryPrimitives.ReadUInt32BigEndian(name);
        Span<byte> header = stackalloc byte[8];
        while (end - start >= 8)
        {
            file.Position = start;
            if (!file.TryReadExactly(header)) break;
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint currentType = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if (size == 1)
            {
                if (end - start < 16 || !file.TryReadExactly(header)) break;
                size = BinaryPrimitives.ReadUInt64BigEndian(header);
            }
            else if (size == 0) size = (ulong)(end - start);
            if (size < (ulong)(file.Position - start) || size > (ulong)(end - start)) break;
            if (currentType == type) return new(file.Position, start + (long)size);
            start += (long)size;
        }
        return default;
    }

    private static bool ReadAt(Stream file, Box box, int offset, Span<byte> destination)
    {
        if (box.End - box.Start < offset + destination.Length) return false;
        file.Position = box.Start + offset;
        return file.TryReadExactly(destination);
    }

    private static bool TryReadExactly(this Stream file, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            int count = file.Read(destination);
            if (count == 0) return false;
            destination = destination[count..];
        }
        return true;
    }
}
