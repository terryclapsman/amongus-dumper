using System.Reflection.PortableExecutable;
using System.Text;

namespace AmongUsDumper.PE;

internal static class PeExportReader
{
    public static SortedDictionary<string, ulong> ReadExports(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var peReader = new PEReader(stream);

        var headers = peReader.PEHeaders;
        var peHeader = headers.PEHeader;

        if (peHeader is null || peHeader.ExportTableDirectory.RelativeVirtualAddress == 0)
        {
            return new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        }

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        var exportOffset = RvaToFileOffset(headers, unchecked((uint)peHeader.ExportTableDirectory.RelativeVirtualAddress));
        stream.Position = exportOffset;

        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var numberOfFunctions = reader.ReadUInt32();
        var numberOfNames = reader.ReadUInt32();
        var addressOfFunctions = reader.ReadUInt32();
        var addressOfNames = reader.ReadUInt32();
        var addressOfNameOrdinals = reader.ReadUInt32();

        var functionRvas = ReadUInt32Array(reader, headers, addressOfFunctions, checked((int)numberOfFunctions));
        var nameRvas = ReadUInt32Array(reader, headers, addressOfNames, checked((int)numberOfNames));
        var ordinals = ReadUInt16Array(reader, headers, addressOfNameOrdinals, checked((int)numberOfNames));

        var exports = new SortedDictionary<string, ulong>(StringComparer.Ordinal);

        for (var index = 0; index < numberOfNames; index++)
        {
            var name = ReadNullTerminatedAscii(reader, headers, nameRvas[index]);
            var ordinal = ordinals[index];

            if (ordinal >= functionRvas.Length)
            {
                continue;
            }

            exports[name] = functionRvas[ordinal];
        }

        return exports;
    }

    private static uint[] ReadUInt32Array(BinaryReader reader, PEHeaders headers, uint rva, int count)
    {
        var offset = RvaToFileOffset(headers, rva);
        reader.BaseStream.Position = offset;

        var values = new uint[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = reader.ReadUInt32();
        }

        return values;
    }

    private static ushort[] ReadUInt16Array(BinaryReader reader, PEHeaders headers, uint rva, int count)
    {
        var offset = RvaToFileOffset(headers, rva);
        reader.BaseStream.Position = offset;

        var values = new ushort[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = reader.ReadUInt16();
        }

        return values;
    }

    private static string ReadNullTerminatedAscii(BinaryReader reader, PEHeaders headers, uint rva)
    {
        var offset = RvaToFileOffset(headers, rva);
        reader.BaseStream.Position = offset;

        using var buffer = new MemoryStream();

        while (true)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                break;
            }

            buffer.WriteByte(value);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static long RvaToFileOffset(PEHeaders headers, uint rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            var start = section.VirtualAddress;
            var end = start + Math.Max(section.VirtualSize, section.SizeOfRawData);

            if (rva < start || rva >= end)
            {
                continue;
            }

            return section.PointerToRawData + (rva - start);
        }

        throw new InvalidOperationException($"RVA 0x{rva:X} is outside of section ranges.");
    }
}
