using System.Text.Json.Serialization;

namespace AmongUsDumper.Analysis;

internal sealed class AnalysisResult
{
    public required SortedDictionary<string, SortedDictionary<string, ulong>> Offsets { get; init; }

    public required SortedDictionary<string, ModuleSchema> Schemas { get; init; }

    public required InfoFile Info { get; init; }
}

internal sealed class ModuleSchema
{
    public required SortedDictionary<string, ClassSchema> Classes { get; init; }

    public required SortedDictionary<string, EnumSchema> Enums { get; init; }
}

internal sealed class ClassSchema
{
    public required string Name { get; init; }

    public required string FullName { get; init; }

    public required string Namespace { get; init; }

    public string? Parent { get; init; }

    public int Size { get; init; }

    public required SortedDictionary<string, FieldSchema> Fields { get; init; }

    public required SortedDictionary<string, FieldSchema> StaticFields { get; init; }

    public required SortedDictionary<string, MethodSchema> Methods { get; init; }
}

internal sealed class FieldSchema
{
    public required string Name { get; init; }

    public required string TypeName { get; init; }

    public int Offset { get; init; }
}

internal sealed class MethodSchema
{
    public required string Name { get; init; }

    public required string Signature { get; init; }

    public required string ReturnType { get; init; }

    public ulong Rva { get; init; }

    public bool IsStatic { get; init; }
}

internal sealed class EnumSchema
{
    public required string Name { get; init; }

    public required string FullName { get; init; }

    public required string Namespace { get; init; }

    public required string UnderlyingType { get; init; }

    public int Alignment { get; init; }

    public int Size { get; init; }

    public required SortedDictionary<string, long> Members { get; init; }
}

internal sealed class InfoFile
{
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("game")]
    public required string Game { get; init; }

    [JsonPropertyName("game_version")]
    public string? GameVersion { get; init; }

    [JsonPropertyName("unity_version")]
    public required string UnityVersion { get; init; }

    [JsonPropertyName("metadata_version")]
    public float MetadataVersion { get; init; }

    [JsonPropertyName("native_modules")]
    public int NativeModules { get; init; }

    [JsonPropertyName("managed_modules")]
    public int ManagedModules { get; init; }

    [JsonPropertyName("class_count")]
    public int ClassCount { get; init; }

    [JsonPropertyName("enum_count")]
    public int EnumCount { get; init; }
}
