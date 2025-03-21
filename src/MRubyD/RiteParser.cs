using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MRubyD;

public class RiteParseException(string message) : Exception(message)
{
    public static void ThrowBinaryLengthIsTooShort() => throw new RiteParseException("Binary size is too short.");
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct RiteBinaryHeader
{
    public static readonly int Size = Unsafe.SizeOf<RiteBinaryHeader>();

    public fixed byte BinaryIdentifier[4];
    public fixed byte MajorVersion[2];
    public fixed byte MinorVersion[2];
    public fixed byte BinarySize[4];
    public fixed byte CompilerName[4];
    public fixed byte CompilerVersion[4];
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct RiteSectionHeader
{
    public static readonly int Size = Unsafe.SizeOf<RiteSectionHeader>();

    public fixed byte SectionIdentifier[4];
    public fixed byte SectionSize[4];
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct RiteSectionIrepHeader
{
    public static readonly int Size = Unsafe.SizeOf<RiteSectionIrepHeader>();

    public fixed byte SectionIdentifier[4];
    public fixed byte SectionSize[4];
    public fixed byte RiteVersion[4];
}

public class RiteParser(MRubyState state)
{
    static readonly byte[] MajorVersion = "03"u8.ToArray();
    static readonly byte[] MinorVersionLower = "00"u8.ToArray();

    public unsafe Irep Parse(ReadOnlySpan<byte> bin)
    {
        ReadBinaryHeader(ref bin, out var binSize);
        binSize -= (uint)RiteBinaryHeader.Size;

        var result = default(Irep?);
        while (binSize > 0)
        {
            var sectionHeader = Unsafe.ReadUnaligned<RiteSectionHeader>(in bin[0]);
            var sectionIdentifier = new ReadOnlySpan<byte>(sectionHeader.SectionIdentifier, 4);
            var sectionSize = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(sectionHeader.SectionSize, 4));

            if (sectionIdentifier.SequenceEqual("IREP"u8))
            {
                ReadSectionIrep(bin, out result);
            }
            else if (sectionIdentifier.SequenceEqual("DBG\0"u8))
            {
                ReadSectionDebug(bin, result!);
            }
            else if (sectionIdentifier.SequenceEqual("LVAR"u8))
            {
                ReadSectionLocalVariables(bin, result!);
            }

            bin = bin[(int)sectionSize..];
            binSize -= sectionSize;
        }
        return result!;
    }

    unsafe void ReadBinaryHeader(ref ReadOnlySpan<byte> bin, out uint binSize)
    {
        if (bin.Length < RiteBinaryHeader.Size)
        {
            RiteParseException.ThrowBinaryLengthIsTooShort();
        }

        var binaryHeader = Unsafe.ReadUnaligned<RiteBinaryHeader>(in bin[0]);
        var binaryIdentifer = new ReadOnlySpan<byte>(binaryHeader.BinaryIdentifier, 4);
        if (!binaryIdentifer.SequenceEqual("RITE"u8))
        {
            throw new RiteParseException("Binary header is incorrect");
        }

        // if major version is different, they are incompatible.
        // if minor version is different, we can accept the older version
        var validVersion = binaryHeader.MajorVersion[0] == MajorVersion[0] &&
                           binaryHeader.MajorVersion[1] == MajorVersion[1] &&
                           binaryHeader.MinorVersion[0] <= MinorVersionLower[0] &&
                           binaryHeader.MinorVersion[1] <= MinorVersionLower[1];
        if (!validVersion)
        {
            var actualMajor = Encoding.ASCII.GetString(binaryHeader.MajorVersion, 2);
            var actualMinor = Encoding.ASCII.GetString(binaryHeader.MinorVersion, 2);
            throw new RiteParseException($"Incompatible RITE version. Expected=03.00 Actual={actualMajor}.{actualMinor}");
        }

        binSize = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(binaryHeader.BinarySize, 4));
        bin = bin[RiteBinaryHeader.Size..];
    }

    void ReadSectionIrep(ReadOnlySpan<byte> bin, out Irep irep)
    {
        if (bin.Length < RiteSectionIrepHeader.Size)
        {
            RiteParseException.ThrowBinaryLengthIsTooShort();
        }
        bin = bin[RiteSectionIrepHeader.Size..];

        ReadIrepRecordRecursive(ref bin, out irep);
    }

    void ReadSectionLocalVariables(ReadOnlySpan<byte> bin, Irep irep)
    {
        bin = bin[RiteSectionHeader.Size..];

        var length = BinaryPrimitives.ReadUInt32BigEndian(bin);
        bin = bin[sizeof(uint)..];

        var symbols = new Symbol[length];
        for (var i = 0; i < length; i++)
        {
            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(bin);
            bin = bin[sizeof(ushort)..];

            symbols[i] = state.Intern(bin[..nameLength]);
            bin = bin[nameLength..];
        }
        ReadLocalVariablesRecursive(ref bin, symbols, irep);
    }

    void ReadSectionDebug(ReadOnlySpan<byte> bin, Irep irep)
    {
        // currently not supported
    }

    void ReadIrepRecordRecursive(ref ReadOnlySpan<byte> bin, out Irep irep)
    {
        ReadIRepRecordOne(ref bin, out irep);

        for (var i = 0; i < irep.Children.Length; i++)
        {
            ReadIrepRecordRecursive(ref bin, out irep.Children[i]);
        }
    }

    void ReadIRepRecordOne(ref ReadOnlySpan<byte> bin, out Irep irep)
    {
        // skip record size
        bin = bin[sizeof(uint)..];

        var localVariableCount = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        var registerVariableCount = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        var childCount = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        // Binary Data Section
        // ISEQ BLOCK (and CATCH HANDLER TABLE BLOCK)
        var catchHandlerCount = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        var dataLength = BinaryPrimitives.ReadUInt32BigEndian(bin);
        bin = bin[sizeof(uint)..];

        byte[] iseq = [];
        if (dataLength > 0)
        {
            if (dataLength > int.MaxValue)
            {
                throw new RiteParseException("irep record length is too long.");
            }
            // var dataLength = dataLength + 13 * catchHandlerCount;
            // if (dataLength > bin.Length)
            // {
            //     throw new RiteParseException("irep record length is too long.");
            // }

            iseq = new byte[dataLength];
            bin[..(int)dataLength].CopyTo(iseq);
            bin = bin[(int)dataLength..];
        }

        CatchHandler[] catchHandlers = [];
        if (catchHandlerCount > 0)
        {
            catchHandlers = new CatchHandler[catchHandlerCount];
            for (var i = 0; i < catchHandlerCount; i++)
            {
                var type = (CatchHandlerType)bin[0];
                bin = bin[1..];

                var begin = BinaryPrimitives.ReadUInt32BigEndian(bin);
                bin = bin[sizeof(uint)..];

                var end = BinaryPrimitives.ReadUInt32BigEndian(bin);
                bin = bin[sizeof(uint)..];

                var target = BinaryPrimitives.ReadUInt32BigEndian(bin);
                bin = bin[sizeof(uint)..];

                catchHandlers[i] = new CatchHandler(type, begin, end, target);
            }
        }

        // Pool Block
        var poolLength = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        MRubyValue[] poolingValues = [];
        if (poolLength > 0)
        {
            poolingValues = new MRubyValue[poolLength];
            for (var i = 0; i < poolLength; i++)
            {
                var t = bin[0];
                bin = bin[1..];
                switch (t)
                {
                    // Int32
                    case 1:
                    {
                        var x = BinaryPrimitives.ReadInt32BigEndian(bin);
                        bin = bin[sizeof(int)..];
                        poolingValues[i] = MRubyValue.From(x);
                        break;
                    }
                    // Int64
                    case 3:
                    {
                        var x = BinaryPrimitives.ReadInt64BigEndian(bin);
                        bin = bin[sizeof(long)..];
                        poolingValues[i] = MRubyValue.From(x);
                        break;
                    }
                    // BigInt
                    case 7:
                    {
                        throw new NotSupportedException();
                        break;
                    }
                    // Float
                    case 5:
                    {
                        var x = BinaryPrimitives.ReadDoubleBigEndian(bin);
                        bin = bin[sizeof(double)..];
                        poolingValues[i] = MRubyValue.From(x);
                        break;
                    }
                    // String
                    case 0:
                    {
                        var length = BinaryPrimitives.ReadUInt16BigEndian(bin);
                        bin = bin[sizeof(ushort)..];
                        var str = state.NewString(bin[..length]);
                        bin = bin[(length + 1)..]; // skip \0
                        poolingValues[i] = MRubyValue.From(str);
                        break;
                    }
                    default:
                        throw new NotSupportedException($"pool value type `{t}` is not supported.");
                }
            }
        }

        // Syms Block
        var symsLength = BinaryPrimitives.ReadUInt16BigEndian(bin);
        bin = bin[sizeof(ushort)..];

        Symbol[] symbols = [];
        if (symsLength > 0)
        {
            if (symsLength > bin.Length)
            {
                throw new RiteParseException("symbol count is too long.");
            }
            symbols = new Symbol[symsLength];
            for (var i = 0; i < symsLength; i++)
            {
                var symbolNameLength = BinaryPrimitives.ReadUInt16BigEndian(bin);
                bin = bin[sizeof(ushort)..];

                if (symbolNameLength == 0xFFFF)
                {
                    symbols[i] = Symbol.Empty;
                    continue;
                }
                if (bin.Length < symbolNameLength)
                {
                    throw new RiteParseException("symbol length is too long.");
                }

                symbols[i] = state.Intern(bin[..symbolNameLength]);
                bin = bin[(symbolNameLength + 1)..]; // Skip \0
            }
        }

        irep = new Irep
        {
            LocalVariables = new Symbol[localVariableCount],
            Children = new Irep[childCount],
            RegisterVariableCount = registerVariableCount,
            Symbols = symbols,
            PoolValues = poolingValues,
            Sequence = iseq,
            CatchHandlers = catchHandlers,
        };
    }

    bool ReadLocalVariablesRecursive(ref ReadOnlySpan<byte> bin, Symbol[] symbols, Irep irep)
    {
        for (var i = 0; i < irep.LocalVariables.Length; i++)
        {
            var symbolIndex = BinaryPrimitives.ReadUInt16BigEndian(bin);
            bin = bin[sizeof(ushort)..];

            if (symbolIndex > symbols.Length - 1)
            {
                return false;
            }

            irep.LocalVariables[i] = symbolIndex == 0xFFFF ? Symbol.Empty : symbols[symbolIndex];
        }

        foreach (var child in irep.Children)
        {
            if (!ReadLocalVariablesRecursive(ref bin, symbols, child))
            {
                return false;
            }
        }
        return true;
    }
}
