using System;
using System.IO;

namespace WorldBoxBridge.Commands.Control;

/// <summary>
/// Thrown when the file changed underneath the read. Distinct from
/// <see cref="ArgumentException"/> because the caller's argument was fine and retrying is the
/// right response, where a bad path should never be retried.
/// </summary>
public sealed class SaveFileChangedException : IOException
{
    public SaveFileChangedException(string message)
        : base(message) { }
}

/// <summary>
/// Reads a save file with a ceiling on how much of it is ever held in memory, and refuses
/// anything that is not a regular file before it opens it. Pure, no Unity dependency, so the
/// test project can link it.
/// </summary>
/// <remarks>
/// <para><c>load_world</c> accepts absolute paths by contract, so the file it is pointed at is
/// whatever the caller named. Two things follow, and both are handled before the file is
/// opened.</para>
/// <para>First, <c>open(2)</c> on a FIFO with no writer blocks, and net462 has nothing portable
/// that interrupts it. Opening first and asking questions afterwards would leak a thread, a file
/// descriptor and a socket for the life of the process, once per call. So the size is taken with
/// a <c>stat</c>, which does not block, and a file reporting zero bytes is refused: a FIFO, a
/// character device such as <c>/dev/zero</c>, and a <c>/proc</c> entry all report zero, and a
/// zero-byte regular file is not a loadable save either. That is measured behaviour on this
/// runtime, not an assumption; <c>File.GetAttributes</c> is no help, it answers
/// <c>Normal</c> for a FIFO and for <c>/dev/zero</c> alike.</para>
/// <para>Second, a very large file would allocate before it failed, and an
/// <c>OutOfMemoryException</c> under Mono takes the game down with it. The same <c>stat</c>
/// gives the ceiling something to compare against, so an oversized file is refused without
/// allocating for it.</para>
/// <para>What remains unbounded is time. A regular file on a dead network mount can still block
/// in the read. That is why the caller runs this off the Unity main thread: a stuck read then
/// costs one thread-pool thread and one request that never answers, instead of a game frozen
/// until the process is killed.</para>
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
    /// Reads <paramref name="path"/> whole, or throws <see cref="ArgumentException"/> if it is
    /// not a regular file or holds more than <paramref name="maxBytes"/>.
    /// </summary>
    /// <exception cref="SaveFileChangedException">
    /// The file's length changed between the <c>stat</c> and the end of the read, which means a
    /// write was in flight and the bytes read are a torn prefix. Retryable.
    /// </exception>
    public static byte[] ReadBounded(string path, long maxBytes = DefaultMaxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        // stat before open. Both checks below have to happen here rather than on the open
        // stream, because for the case they exist to catch, the open never returns.
        var declared = new FileInfo(path).Length;
        if (declared == 0)
        {
            throw new ArgumentException(
                $"'{path}' reports zero bytes, so it is not a save. A pipe, a character device "
                    + "and a /proc entry all look like this, and opening one can block until the "
                    + "game is killed."
            );
        }
        if (declared > maxBytes)
        {
            throw TooLarge(path, declared, maxBytes);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        var data = ReadBounded(stream, maxBytes, path);

        // The read runs on a pool thread now, so it can interleave with the game's own save on
        // the main thread, which FileShare.ReadWrite deliberately permits. A length that moved
        // means the bytes are a prefix of a file still being written, and handing a truncated
        // archive to loadMapFromBytes is worse than saying so.
        if (stream.Length != declared)
        {
            throw new SaveFileChangedException(
                $"'{path}' was {declared} bytes when the read started and {stream.Length} when it "
                    + "finished. Something is writing to it. Wait for the write to finish and retry."
            );
        }
        return data;
    }

    /// <summary>
    /// Stream overload, which is the whole of the reading logic and the part testable without a
    /// special file on disk.
    /// </summary>
    /// <remarks>
    /// Mirrors what <c>File.ReadAllBytes</c> does, minus the trust: a stream that reports a
    /// length is read into an exactly-sized array, and one that cannot report a length is read in
    /// chunks while the running total is checked against the ceiling.
    /// <para>
    /// The chunked branch bounds what it returns but not its transient footprint:
    /// <see cref="MemoryStream"/> doubles as it grows and <c>ToArray</c> copies once more, so a
    /// stream stopping just under the ceiling peaks at a small multiple of it. That is stated
    /// rather than fixed because the path branch above can no longer reach this branch, having
    /// refused every length-less file before opening it. It stays for callers of this overload.
    /// </para>
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
        if (maxBytes > int.MaxValue)
        {
            // The exact-size branch allocates a byte[], which is indexed by int. Left unchecked,
            // a ceiling above int.MaxValue turned a file the caller was allowed to read into a
            // negative cast and an OverflowException, which is neither ArgumentException nor
            // IOException and surfaced as 500 GAME_CRASH.
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes),
                $"maxBytes must be at most {int.MaxValue}, the largest byte[] this can return."
            );
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
            // Zero is treated as no answer rather than as an empty result: a seekable-looking
            // handle onto a FIFO or a /proc entry reports zero and still delivers bytes, so
            // trusting it would return an empty save for a stream that has plenty to give.
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
            // Read is allowed to return fewer bytes than asked for whenever it likes, and a pipe
            // or a network mount routinely does, so this loops rather than reading once.
            var read = stream.Read(data, offset, count - offset);
            if (read <= 0)
            {
                throw new SaveFileChangedException(
                    $"'{label}' ended after {offset} of {count} bytes. It was truncated between "
                        + "the two reads. Wait for whatever is writing it to finish and retry."
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
