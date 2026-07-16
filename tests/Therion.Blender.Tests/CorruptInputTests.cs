// BA-B2 done-when: "parsers green incl. corrupt-input tests" — truncated, bit-flipped
// and hostile inputs must fail cleanly with CaveFileFormatException (or parse, when
// the damage happens to leave a well-formed file), never hang, crash with another
// exception type, or allocate from attacker-controlled counts.

using System.Buffers.Binary;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class CorruptInputTests
{
    private static byte[] SyntheticLox() => LoxWriter.Write(LoxReaderTests.BuildSyntheticModel());

    [Fact]
    public void Lox_EveryTruncation_ParsesOrThrowsCleanly()
    {
        var full = SyntheticLox();
        for (int length = 0; length < full.Length; length++)
        {
            var slice = full[..length];
            try
            {
                LoxReader.Read(slice); // chunk-aligned prefixes are legitimately valid
            }
            catch (CaveFileFormatException)
            {
                // the expected failure mode
            }
        }
    }

    [Fact]
    public void Survex3d_EveryTruncation_ThrowsCleanly()
    {
        var full = Build3dTortureFile();
        // Sanity: the untruncated file parses.
        Survex3dReader.Read(full);
        for (int length = 0; length < full.Length; length++)
        {
            var slice = full[..length];
            Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(slice));
        }
    }

    [Fact]
    public void Lox_SingleBitFlips_ParseOrThrowCleanly()
    {
        var full = SyntheticLox();
        for (int index = 0; index < full.Length; index++)
        {
            for (int bit = 0; bit < 8; bit += 3)
            {
                var mutated = (byte[])full.Clone();
                mutated[index] ^= (byte)(1 << bit);
                try
                {
                    LoxReader.Read(mutated);
                }
                catch (CaveFileFormatException)
                {
                }
            }
        }
    }

    [Fact]
    public void Survex3d_SingleBitFlips_ParseOrThrowCleanly()
    {
        var full = Build3dTortureFile();
        for (int index = 0; index < full.Length; index++)
        {
            for (int bit = 0; bit < 8; bit += 3)
            {
                var mutated = (byte[])full.Clone();
                mutated[index] ^= (byte)(1 << bit);
                try
                {
                    Survex3dReader.Read(mutated);
                }
                catch (CaveFileFormatException)
                {
                }
            }
        }
    }

    [Fact]
    public void RandomGarbage_ParsesOrThrowsCleanly()
    {
        var random = new Random(20260711); // fixed seed — deterministic CI
        for (int round = 0; round < 200; round++)
        {
            var garbage = new byte[random.Next(0, 512)];
            random.NextBytes(garbage);
            try { LoxReader.Read(garbage); } catch (CaveFileFormatException) { }
            try { Survex3dReader.Read(garbage); } catch (CaveFileFormatException) { }
            try { CaveModelReader.Read(garbage, "x.bin"); } catch (CaveFileFormatException) { }
        }
    }

    [Fact]
    public void Lox_HostileRecordCount_IsRejectedBeforeAllocating()
    {
        // A station chunk claiming 4 billion records in 0 bytes must throw
        // immediately (the reader bounds recCount by the bytes present).
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2);                    // station chunk
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xFFFFFFFF); // recCount
        Assert.Throws<CaveFileFormatException>(() => LoxReader.Read(bytes));
    }

    [Fact]
    public void Lox_HeapReferenceEscapingHeap_IsRejected()
    {
        // Craft a survey chunk whose name pointer reaches beyond its data heap.
        var chunk = new byte[16 + 24 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, 1);                       // type: survey
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), 24);            // recSize
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), 1);             // recCount
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), 4);            // dataSize
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(16 + 4), 0);        // namePtr.position
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(16 + 8), 4096);     // namePtr.size > heap
        Assert.Throws<CaveFileFormatException>(() => LoxReader.Read(chunk));
    }

    [Fact]
    public void Survex3d_HostileLabelAppendCount_IsRejectedBeforeAllocating()
    {
        // LABEL item whose escaped append count claims ~2 GB — must be rejected
        // against the actual remaining byte count, not allocated.
        var bytes = Survex3dFixtureBuilder.V8("t")
            .Op(0x80)          // LABEL
            .Op(0x00)          // escaped patch form
            .Op(0x00)          // delete 0
            .Op(0xff).S32(int.MaxValue) // append: lies
            .Build();
        var ex = Assert.Throws<CaveFileFormatException>(() => Survex3dReader.Read(bytes));
        Assert.Contains("remain", ex.Message);
    }

    [Fact]
    public void FacadeDetection_RoutesBothRealFormats()
    {
        Assert.Equal(CaveSourceFormat.Lox,
            CaveModelReader.ReadFile(TestCorpus.AvCerbulLox()).SourceFormat);
        Assert.Equal(CaveSourceFormat.Survex3d,
            CaveModelReader.ReadFile(TestCorpus.AvCerbul3d()).SourceFormat);
    }

    [Fact]
    public void FacadeDetection_UsesExtensionWhenContentInconclusive_AndThrowsOtherwise()
    {
        // Content that matches neither magic: extension decides…
        var junk = new byte[] { 0xde, 0xad, 0xbe, 0xef };
        Assert.Equal(CaveSourceFormat.Survex3d, CaveModelReader.Detect(junk, "model.3D".ToLowerInvariant()));
        Assert.Equal(CaveSourceFormat.Lox, CaveModelReader.Detect(junk, "model.lox"));
        // …and with no extension it is unknown and Read refuses.
        Assert.Equal(CaveSourceFormat.Unknown, CaveModelReader.Detect(junk, "model.bin"));
        Assert.Throws<CaveFileFormatException>(() => CaveModelReader.Read(junk, "model.bin"));
    }

    /// <summary>A compact v8 file touching every construct: styles, dates, labels,
    /// lines, xsects, error info — so truncation/bit-flip sweeps cross them all.</summary>
    private static byte[] Build3dTortureFile() => Survex3dFixtureBuilder
        .V8("torture", "EPSG:31700", '.')
        .V8Style(0x00)
        .V8Label(0x04 | 0x10, 0, "cave.a", 100.25, 200.5, -30)
        .V8Label(0x01, 1, "b", 110, 210, -31)
        .V8SingleDate(45_000)
        .V8Move(100.25, 200.5, -30)
        .V8Line(0x00, (0, ""), 110, 210, -31)
        .V8Line(0x04, null, 112, 212, -29)
        .V8ErrorInfo(2, 15.5, 0.4, 0.3, 0.1)
        .V8XSect(0, "", 1, 2, 0.5, 3)
        .V8XSect(1, "a", 1, 2, 0.5, 3, last: true)
        .V8Stop()
        .Build();
}
