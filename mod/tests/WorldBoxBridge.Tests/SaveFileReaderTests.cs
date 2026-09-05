using System;
using System.IO;
using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using Xunit;

namespace WorldBoxBridge.Tests;

public class SaveFileReaderTests
{
    // ─── The file entry point: what it refuses before it opens anything ───────
    //
    // These are the tests that matter most. open(2) on a FIFO with no writer blocks and nothing
    // in net462 interrupts it, so a check that runs after the open is a check that never runs.

    [Fact]
    public void Reads_a_file_whole()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        WithTempFile(payload, path => SaveFileReader.ReadBounded(path).Should().Equal(payload));
    }

    [Fact]
    public void Refuses_a_zero_length_file_because_that_is_what_a_pipe_looks_like()
    {
        // A FIFO, /dev/zero, /dev/urandom and a /proc entry all report zero bytes on this
        // runtime, and File.GetAttributes answers Normal for every one of them, so the length is
        // the only signal available before the open. A zero-byte regular file is not a loadable
        // save either, so refusing the whole class costs nothing real.
        WithTempFile(
            Array.Empty<byte>(),
            path =>
            {
                Action act = () => SaveFileReader.ReadBounded(path);
                act.Should().Throw<ArgumentException>().WithMessage("*reports zero bytes*");
            }
        );
    }

    [Fact]
    public void Refuses_a_file_over_the_ceiling_without_opening_it()
    {
        WithTempFile(
            new byte[513],
            path =>
            {
                Action act = () => SaveFileReader.ReadBounded(path, maxBytes: 512);
                act.Should().Throw<ArgumentException>().WithMessage("*513 bytes*512 byte ceiling*");
            }
        );
    }

    [Fact]
    public void Accepts_a_file_exactly_at_the_ceiling()
    {
        WithTempFile(
            new byte[512],
            path => SaveFileReader.ReadBounded(path, 512).Should().HaveCount(512)
        );
    }

    // ─── The counted path: a stream that will not say how long it is ──────────

    [Fact]
    public void Reads_a_length_less_stream_whole_when_it_stays_under_the_ceiling()
    {
        var payload = new byte[] { 9, 8, 7 };
        SaveFileReader
            .ReadBounded(new FakeStream(payload, canSeek: false), maxBytes: 512, label: "pipe")
            .Should()
            .Equal(payload);
    }

    [Fact]
    public void Accepts_a_length_less_stream_of_exactly_the_ceiling()
    {
        // Pins `total > maxBytes` against being tightened to `>=`, which no other test would
        // notice: the over-ceiling test below would stay green either way.
        SaveFileReader
            .ReadBounded(new FakeStream(new byte[512], canSeek: false), 512, "pipe")
            .Should()
            .HaveCount(512);
    }

    [Fact]
    public void Stops_a_length_less_stream_one_byte_past_the_ceiling()
    {
        Action act = () =>
            SaveFileReader.ReadBounded(new FakeStream(new byte[513], canSeek: false), 512, "pipe");
        act.Should().Throw<ArgumentException>().WithMessage("*without*reporting a size*");
    }

    // ─── Short reads, which every real pipe and network mount produces ────────

    [Fact]
    public void Reassembles_a_stream_that_delivers_a_few_bytes_at_a_time()
    {
        // Read is free to return fewer bytes than asked for. Without the loop in ReadExactly
        // this returns a mostly-zero array, and every other test in this file still passes.
        var payload = new byte[5000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }
        var stream = new FakeStream(payload, canSeek: true, maxPerRead: 7);

        SaveFileReader.ReadBounded(stream, 1 << 20, "chunked").Should().Equal(payload);
    }

    [Fact]
    public void Reports_a_stream_that_ends_before_its_declared_length_as_retryable()
    {
        // Declares 100, delivers 40. The caller's argument was fine, so this must not be
        // BAD_ARGS: something is writing the file.
        var stream = new FakeStream(new byte[40], canSeek: true, declaredLength: 100);

        Action act = () => SaveFileReader.ReadBounded(stream, 1 << 20, "truncated");

        act.Should().Throw<SaveFileChangedException>().WithMessage("*ended after 40 of 100 bytes*");
    }

    [Fact]
    public void Truncates_a_stream_that_declared_less_than_it_delivers()
    {
        // Documented behaviour: the declared length wins, which is what keeps the allocation
        // bounded by the number that was checked against the ceiling.
        var stream = new FakeStream(new byte[200], canSeek: true, declaredLength: 100);

        SaveFileReader.ReadBounded(stream, 1 << 20, "grew").Should().HaveCount(100);
    }

    // ─── DeclaredLength's fallbacks, none of which a real file reaches ────────

    [Fact]
    public void A_seekable_stream_reporting_zero_is_still_read_whole()
    {
        // The case the `length > 0 ? length : -1` ternary exists for. Delete it and this test
        // fails; nothing else in the file would.
        var payload = new byte[] { 4, 5, 6 };
        var stream = new FakeStream(payload, canSeek: true, declaredLength: 0);

        SaveFileReader.ReadBounded(stream, 512, "proc-entry").Should().Equal(payload);
    }

    [Theory]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    public void A_length_getter_that_throws_falls_through_to_the_counted_path(Type thrown)
    {
        var payload = new byte[] { 1, 2 };
        var stream = new FakeStream(payload, canSeek: true, lengthThrows: thrown);

        SaveFileReader.ReadBounded(stream, 512, "device").Should().Equal(payload);
    }

    // ─── Argument guards ─────────────────────────────────────────────────────

    [Fact]
    public void Rejects_a_null_stream()
    {
        Action act = () => SaveFileReader.ReadBounded((Stream)null!, 1, "x");
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_non_positive_ceiling(long maxBytes)
    {
        Action act = () => SaveFileReader.ReadBounded(new MemoryStream(), maxBytes, "x");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rejects_a_ceiling_above_what_a_byte_array_can_hold()
    {
        // Left unchecked, `(int)declared` on a stream longer than 2 GiB casts negative and
        // `new byte[negative]` throws OverflowException, which is neither ArgumentException nor
        // IOException and reaches the HTTP layer as 500 GAME_CRASH.
        Action act = () =>
            SaveFileReader.ReadBounded(new MemoryStream(), (long)int.MaxValue + 1, "huge");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void WithTempFile(byte[] content, Action<string> assert)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "wb-save-" + Guid.NewGuid().ToString("N") + ".wbox"
        );
        File.WriteAllBytes(path, content);
        try
        {
            assert(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A readable stream whose seekability, reported length, per-call read size and Length-getter
    /// behaviour are all dialled in by the test. Written by hand rather than opened with mkfifo:
    /// a FIFO with no writer blocks on open, which would hang the test run instead of failing it.
    /// </summary>
    private sealed class FakeStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly bool _canSeek;
        private readonly long? _declaredLength;
        private readonly int _maxPerRead;
        private readonly Type? _lengthThrows;

        public FakeStream(
            byte[] content,
            bool canSeek,
            long? declaredLength = null,
            int maxPerRead = int.MaxValue,
            Type? lengthThrows = null
        )
        {
            _inner = new MemoryStream(content);
            _canSeek = canSeek;
            _declaredLength = declaredLength;
            _maxPerRead = maxPerRead;
            _lengthThrows = lengthThrows;
        }

        public int ReadCalls { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => _canSeek;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                if (_lengthThrows == typeof(NotSupportedException))
                {
                    throw new NotSupportedException("no length here");
                }
                if (_lengthThrows == typeof(IOException))
                {
                    throw new IOException("the mount is gone");
                }
                if (!_canSeek)
                {
                    throw new NotSupportedException();
                }
                return _declaredLength ?? _inner.Length;
            }
        }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            return _inner.Read(buffer, offset, Math.Min(count, _maxPerRead));
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
