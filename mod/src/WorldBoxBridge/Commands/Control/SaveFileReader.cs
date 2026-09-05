using System;
using System.IO;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Reads a save file with a ceiling on how much of it is ever held in memory. Pure, no Unity
/// dependency, so the test project can link it.
/// </summary>
/// <remarks>
/// <para><c>load_world</c> accepts absolute paths by contract, so the file it is pointed at is
/// whatever the caller named. <c>File.ReadAllBytes</c> on a multi-gigabyte file allocates the
/// whole of it, and an <c>OutOfMemoryException</c> under Mono takes the game down with it, so
/// the read refuses up front instead of allocating first and failing later.</para>
/// <para>What it does <em>not</em> bound is time. A FIFO or a character device blocks inside the
/// read syscall and net462 has nothing portable that interrupts one. That is why the caller runs
/// this off the Unity main thread: a stuck read then costs one thread-pool thread and one request
/// that never answers, instead of a game frozen until the process is killed.</para>
/// </remarks>
public static class SaveFileReader
{
    /// <summary>
    /// 256 MiB. Not a format limit, real WorldBox saves are orders of magnitude smaller. It is
    /// the line past which the thing being read is not a save and saying so beats allocating it.
    /// </summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;

    private const int ChunkSize = 81920;

    /// <summary>
    /// Reads <paramref name="path"/> whole, or throws <see cref="ArgumentException"/> if it holds
    /// more than <paramref name="maxBytes"/>.
    /// </summary>
    /// <remarks>
    /// Opened with <see cref="FileShare.ReadWrite"/> so a save the game itself still has open is
    /// readable, which is the ordinary case right after <c>save_world</c>.
    /// </remarks>
    public static byte[] ReadBounded(string path, long maxBytes = DefaultMaxBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        return ReadBounded(stream, maxBytes, path);
    }

    /// <summary>
    /// Stream overload, which is the whole of the logic and the part that is testable without a
    /// special file on disk.
    /// </summary>
    /// <remarks>
    /// Mirrors what <c>File.ReadAllBytes</c> does, minus the trust: a seekable stream is read into
    /// an exactly-sized array, and anything that cannot report a length (a pipe, a device) is read
    /// in chunks while the running total is checked against the ceiling. A seekable file that grows
    /// mid-read is truncated to the length it declared, which is the same answer
    /// <c>File.ReadAllBytes</c> gives for a file of that length and the only one that keeps the
    /// allocation bounded.
    /// </remarks>
    public static byte[] ReadBounded(Stream stream, long maxBytes, string label)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        var declared = DeclaredLength(stream);
        if (declared > maxBytes)
        {
            throw TooLarge(label, declared, maxBytes);
        }
        if (declared >= 0)
        {
            return ReadExactly(stream, (int)declared, label);
        }

        // No usable length: read forward and stop the moment the total crosses the ceiling, so a
        // stream that never ends costs one chunk of memory rather than all of it.
        using var sink = new MemoryStream();
        var buffer = new byte[ChunkSize];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw TooLarge(label, declared: -1, maxBytes);
            }
            sink.Write(buffer, 0, read);
        }
        return sink.ToArray();
    }

    /// <summary>Length in bytes, or -1 when the stream cannot answer.</summary>
    private static long DeclaredLength(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return -1;
        }
        try
        {
            var length = stream.Length;
            // A Unix FIFO or /proc entry can be seekable-looking and report zero. Treat zero as
            // "no answer" so it goes down the counted path instead of returning an empty save.
            return length > 0 ? length : -1;
        }
        catch (NotSupportedException)
        {
            return -1;
        }
        catch (IOException)
        {
            return -1;
        }
    }

    private static byte[] ReadExactly(Stream stream, int count, string label)
    {
        var data = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(data, offset, count - offset);
            if (read <= 0)
            {
                throw new ArgumentException(
                    $"'{label}' ended after {offset} of {count} bytes. The file is being written "
                        + "to, or it was truncated between the two reads."
                );
            }
            offset += read;
        }
        return data;
    }

    private static ArgumentException TooLarge(string label, long declared, long maxBytes) =>
        new(
            declared >= 0
                ? $"'{label}' is {declared} bytes, over the {maxBytes} byte ceiling for a save. "
                    + "Point load_world at a save file, not at an arbitrary path."
                : $"'{label}' produced more than the {maxBytes} byte ceiling for a save without "
                    + "reporting a size. Point load_world at a save file, not at a pipe or a device."
        );
}
