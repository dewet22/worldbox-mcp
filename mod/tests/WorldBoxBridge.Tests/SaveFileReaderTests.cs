using System;
using System.IO;
using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using Xunit;

namespace WorldBoxBridge.Tests;

public class SaveFileReaderTests
{
    // ─── The seekable path: a real file, or anything that reports a length ────

    [Fact]
    public void Reads_a_file_whole()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        WithTempFile(payload, path => SaveFileReader.ReadBounded(path).Should().Equal(payload));
    }

    [Fact]
    public void Reads_an_empty_file_as_an_empty_array()
    {
        // Zero length goes down the counted path, because a FIFO reports zero too. The answer
        // has to come out the same either way.
        WithTempFile(
            Array.Empty<byte>(),
            path => SaveFileReader.ReadBounded(path).Should().BeEmpty()
        );
    }

    [Fact]
    public void Refuses_a_file_over_the_ceiling_without_reading_it()
    {
        WithTempFile(
            new byte[1024],
            path =>
            {
                Action act = () => SaveFileReader.ReadBounded(path, maxBytes: 512);
                act.Should()
                    .Throw<ArgumentException>()
                    .WithMessage("*1024 bytes*512 byte ceiling*");
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

    // ─── The counted path: a stream that cannot say how long it is ────────────
    //
    // This is the FIFO and character-device case, and the reason the ceiling is enforced on a
    // running total rather than on a declared length alone.

    [Fact]
    public void Reads_a_length_less_stream_whole_when_it_stays_under_the_ceiling()
    {
        var payload = new byte[] { 9, 8, 7 };
        SaveFileReader
            .ReadBounded(new LengthLessStream(payload), maxBytes: 512, label: "pipe")
            .Should()
            .Equal(payload);
    }

    [Fact]
    public void Stops_a_length_less_stream_that_runs_past_the_ceiling()
    {
        Action act = () =>
            SaveFileReader.ReadBounded(new LengthLessStream(new byte[4096]), 512, "pipe");
        act.Should().Throw<ArgumentException>().WithMessage("*without*reporting a size*");
    }

    [Fact]
    public void Rejects_a_non_positive_ceiling()
    {
        Action act = () => SaveFileReader.ReadBounded(new MemoryStream(), 0, "x");
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
    /// A readable stream that refuses to report a length, the way a Unix FIFO does. Written by
    /// hand rather than opened with mkfifo: a FIFO with no writer blocks on open, which would
    /// hang the test run instead of failing it.
    /// </summary>
    private sealed class LengthLessStream : Stream
    {
        private readonly MemoryStream _inner;

        public LengthLessStream(byte[] content) => _inner = new MemoryStream(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
