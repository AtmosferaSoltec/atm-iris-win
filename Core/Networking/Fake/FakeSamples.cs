using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Iris.Core.Networking.Fake;

/// <summary>
/// Sample files for the fake storage, generated in code (no image or audio libraries): gradient PNGs and a WAV tone.
/// Output is deterministic, so the size announced in the media row always matches the bytes served.
/// </summary>
public static class FakeSamples
{
    /// <summary>A diagonal two-color gradient, 8-bit RGB, as a PNG.</summary>
    public static byte[] GradientPng(int width, int height, (byte R, byte G, byte B) from, (byte R, byte G, byte B) to)
    {
        var raw = new byte[(((width * 3) + 1) * height)];
        var i = 0;
        for (var y = 0; y < height; y++)
        {
            raw[i++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var t = ((double)x / width + (double)y / height) / 2;
                raw[i++] = Lerp(from.R, to.R, t);
                raw[i++] = Lerp(from.G, to.G, t);
                raw[i++] = Lerp(from.B, to.B, t);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // color type: RGB
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A mono 16-bit PCM sine tone with short fades, as a WAV.</summary>
    public static byte[] ToneWav(double seconds, double frequency, int sampleRate = 22050)
    {
        var samples = (int)(seconds * sampleRate);
        var data = new byte[samples * 2];
        for (var n = 0; n < samples; n++)
        {
            var fade = Math.Min(1.0, Math.Min(n, samples - n) / (sampleRate * 0.05));
            var value = (short)(Math.Sin(2 * Math.PI * frequency * n / sampleRate) * 0.3 * short.MaxValue * fade);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(n * 2), value);
        }

        using var wav = new MemoryStream();
        using var writer = new BinaryWriter(wav);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        return wav.ToArray();
    }

    /// <summary>A stable color pair per media id, so the same id always produces the same picture.</summary>
    public static (byte R, byte G, byte B) ColorFor(Guid id, int which)
    {
        var b = id.ToByteArray();
        return which == 0
            ? ((byte)(20 + (b[0] % 60)), (byte)(20 + (b[1] % 60)), (byte)(60 + (b[2] % 120)))
            : ((byte)(120 + (b[3] % 120)), (byte)(60 + (b[4] % 100)), (byte)(40 + (b[5] % 100)));
    }

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
