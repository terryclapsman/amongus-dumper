using System.Diagnostics;
using System.Reflection;
using AmongUsDumper.Discovery;
using AmongUsDumper.Logging;
using AmongUsDumper.PE;
using LibCpp2IL;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;

namespace AmongUsDumper.Analysis;

internal sealed class AmongUsAnalyzer
{
    private readonly AppLogger _logger;

    public AmongUsAnalyzer(AppLogger logger)
    {
        _logger = logger;
    }

    public AnalysisResult Analyze(GamePaths gamePaths)
    {
        LibCpp2IlMain.Reset();
        LibLogger.Writer = new LibCpp2IlLogBridge(_logger);
        LibLogger.ShowVerbose = false;
        LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = false;
        LibCpp2IlMain.Settings.DisableMethodPointerMapping = false;
        LibCpp2IlMain.Settings.DisableGlobalResolving = false;

        var unityVersion = ParseUnityVersion(gamePaths.UnityVersion);

        _logger.Info("loading IL2CPP metadata and binary");

        if (!LibCpp2IlMain.LoadFromFile(gamePaths.GameAssemblyPath, gamePaths.MetadataPath, unityVersion))
        {
            throw new InvalidOperationException("LibCpp2IL failed to initialize.");
        }

        if (LibCpp2IlMain.Binary is null || LibCpp2IlMain.TheMetadata is null)
        {
            throw new InvalidOperationException("LibCpp2IL did not expose binary or metadata objects.");
        }

        var offsets = BuildOffsets(gamePaths);
        var schemas = BuildSchemas(LibCpp2IlMain.TheMetadata, LibCpp2IlMain.Binary);
        var info = BuildInfo(gamePaths, offsets, schemas);

        return new AnalysisResult
        {
            Offsets = offsets,
            Schemas = schemas,
            Info = info,
        };
    }

    private SortedDictionary<string, SortedDictionary<string, ulong>> BuildOffsets(GamePaths gamePaths)
    {
        var result = new SortedDictionary<string, SortedDictionary<string, ulong>>(StringComparer.OrdinalIgnoreCase);

        foreach (var modulePath in EnumerateNativeModules(gamePaths))
        {
            if (!File.Exists(modulePath))
            {
                continue;
            }

            var exports = PeExportReader.ReadExports(modulePath);
            if (exports.Count == 0)
            {
                continue;
            }

            result[Path.GetFileName(modulePath)] = new SortedDictionary<string, ulong>(exports, StringComparer.Ordinal);
            _logger.Debug($"found {exports.Count} exported offset(s) in {Path.GetFileName(modulePath)}");
        }

        return result;
    }

    private IEnumerable<string> EnumerateNativeModules(GamePaths gamePaths)
    {
        yield return gamePaths.ExecutablePath;
        yield return gamePaths.GameAssemblyPath;

        var unityPlayer = Path.Combine(gamePaths.GameDirectory, "UnityPlayer.dll");
        if (File.Exists(unityPlayer))
        {
            yield return unityPlayer;
        }
    }

    private SortedDictionary<string, ModuleSchema> BuildSchemas(Il2CppMetadata metadata, Il2CppBinary binary)
    {
        var result = new SortedDictionary<string, ModuleSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in metadata.imageDefinitions)
        {
            var imageName = image.Name;
            if (string.IsNullOrWhiteSpace(imageName))
            {
                continue;
            }

            var classes = new SortedDictionary<string, ClassSchema>(StringComparer.Ordinal);
            var enums = new SortedDictionary<string, EnumSchema>(StringComparer.Ordinal);
            var types = image.Types ?? Array.Empty<Il2CppTypeDefinition>();

            foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var fullName = GetTypeFullName(type);

                if (type.IsEnumType)
                {
                    enums[fullName] = BuildEnumSchema(type);
                }
                else
                {
                    classes[fullName] = BuildClassSchema(type, binary);
                }
            }

            if (classes.Count == 0 && enums.Count == 0)
            {
                continue;
            }

            result[imageName] = new ModuleSchema
            {
                Classes = classes,
                Enums = enums,
            };

            _logger.Debug($"module {imageName}: {classes.Count} classes, {enums.Count} enums");
        }

        return result;
    }

    private ClassSchema BuildClassSchema(Il2CppTypeDefinition type, Il2CppBinary binary)
    {
        var fields = new SortedDictionary<string, FieldSchema>(StringComparer.Ordinal);
        var staticFields = new SortedDictionary<string, FieldSchema>(StringComparer.Ordinal);
        var methods = new SortedDictionary<string, MethodSchema>(StringComparer.Ordinal);

        var typeFields = type.Fields ?? Array.Empty<Il2CppFieldDefinition>();
        var fieldAttributes = type.FieldAttributes ?? Array.Empty<FieldAttributes>();

        for (var index = 0; index < typeFields.Length; index++)
        {
            var field = typeFields[index];
            var attributes = index < fieldAttributes.Length ? fieldAttributes[index] : System.Reflection.FieldAttributes.PrivateScope;
            var isStatic = attributes.HasFlag(System.Reflection.FieldAttributes.Static);
            var offset = binary.GetFieldOffsetFromIndex(type.TypeIndex, index, field.FieldIndex, type.IsValueType, isStatic);

            if (offset < 0)
            {
                continue;
            }

            var schema = new FieldSchema
            {
                Name = field.Name ?? $"field_{index}",
                TypeName = NormalizeTypeName(field.FieldType?.ToString() ?? "unknown"),
                Offset = offset,
            };

            var target = isStatic ? staticFields : fields;
            target[schema.Name] = schema;
        }

        var usedMethodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in type.Methods ?? Array.Empty<Il2CppMethodDefinition>())
        {
            if (method.MethodPointer == 0 || method.Rva == 0)
            {
                continue;
            }

            var methodName = CreateUniqueMethodName(method, usedMethodNames);
            methods[methodName] = new MethodSchema
            {
                Name = method.Name ?? "Method",
                Signature = NormalizeSignature(method.HumanReadableSignature ?? methodName),
                ReturnType = NormalizeTypeName(method.ReturnType?.ToString() ?? "void"),
                Rva = method.Rva,
                IsStatic = method.IsStatic,
            };
        }

        return new ClassSchema
        {
            Name = type.Name ?? "UnknownType",
            FullName = GetTypeFullName(type),
            Namespace = type.Namespace ?? string.Empty,
            Parent = NormalizeTypeName(type.BaseType?.ToString()),
            Size = type.Size,
            Fields = fields,
            StaticFields = staticFields,
            Methods = methods,
        };
    }

    private EnumSchema BuildEnumSchema(Il2CppTypeDefinition type)
    {
        var members = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var underlyingType = "int";

        foreach (var field in type.Fields ?? Array.Empty<Il2CppFieldDefinition>())
        {
            if (string.Equals(field.Name, "value__", StringComparison.Ordinal))
            {
                underlyingType = NormalizeTypeName(field.FieldType?.ToString() ?? "int");
                continue;
            }

            var rawValue = field.DefaultValue?.Value;
            if (rawValue is null)
            {
                continue;
            }

            members[field.Name ?? "Member"] = ConvertToInt64(rawValue);
        }

        var size = GetPrimitiveTypeSize(underlyingType);

        return new EnumSchema
        {
            Name = type.Name ?? "UnknownEnum",
            FullName = GetTypeFullName(type),
            Namespace = type.Namespace ?? string.Empty,
            UnderlyingType = underlyingType,
            Alignment = size,
            Size = size,
            Members = members,
        };
    }

    private InfoFile BuildInfo(
        GamePaths gamePaths,
        SortedDictionary<string, SortedDictionary<string, ulong>> offsets,
        SortedDictionary<string, ModuleSchema> schemas)
    {
        var (classCount, enumCount) = schemas.Values.Aggregate(
            (classes: 0, enums: 0),
            (accumulator, schema) =>
            (
                accumulator.classes + schema.Classes.Count,
                accumulator.enums + schema.Enums.Count
            ));

        return new InfoFile
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Game = "Among Us",
            GameVersion = FileVersionInfo.GetVersionInfo(gamePaths.ExecutablePath).FileVersion,
            UnityVersion = gamePaths.UnityVersion,
            MetadataVersion = LibCpp2IlMain.MetadataVersion,
            NativeModules = offsets.Count,
            ManagedModules = schemas.Count,
            ClassCount = classCount,
            EnumCount = enumCount,
        };
    }

    private static int[] ParseUnityVersion(string unityVersion)
    {
        var numericPart = new string(
            unityVersion
                .TakeWhile(character => char.IsAsciiDigit(character) || character == '.')
                .ToArray());

        var parts = numericPart
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        if (parts.Length < 3)
        {
            throw new FormatException($"Invalid Unity version: {unityVersion}");
        }

        return new[] { parts[0], parts[1], parts[2] };
    }

    private static string GetTypeFullName(Il2CppTypeDefinition type)
    {
        return type.FullName ?? type.Name ?? "UnknownType";
    }

    private static string NormalizeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "unknown";
        }

        return typeName
            .Replace("System.Boolean", "bool", StringComparison.Ordinal)
            .Replace("System.Byte", "byte", StringComparison.Ordinal)
            .Replace("System.SByte", "sbyte", StringComparison.Ordinal)
            .Replace("System.Int16", "short", StringComparison.Ordinal)
            .Replace("System.UInt16", "ushort", StringComparison.Ordinal)
            .Replace("System.Int32", "int", StringComparison.Ordinal)
            .Replace("System.UInt32", "uint", StringComparison.Ordinal)
            .Replace("System.Int64", "long", StringComparison.Ordinal)
            .Replace("System.UInt64", "ulong", StringComparison.Ordinal)
            .Replace("System.Single", "float", StringComparison.Ordinal)
            .Replace("System.Double", "double", StringComparison.Ordinal)
            .Replace("System.String", "string", StringComparison.Ordinal);
    }

    private static string NormalizeSignature(string signature)
    {
        return NormalizeTypeName(signature).Replace("::", ".", StringComparison.Ordinal);
    }

    private static string CreateUniqueMethodName(Il2CppMethodDefinition method, HashSet<string> usedMethodNames)
    {
        var baseName = SanitizeIdentifier(method.Name ?? "Method");
        if (usedMethodNames.Add(baseName))
        {
            return baseName;
        }

        var parameterSuffix = string.Join(
            "_",
            (method.Parameters ?? Array.Empty<LibCpp2IL.Reflection.Il2CppParameterReflectionData>())
                .Select(parameter => SanitizeIdentifier(NormalizeTypeName(parameter.Type?.ToString() ?? "arg"))));

        var tokenField = typeof(Il2CppMethodDefinition).GetField("token")!;
        var token = (uint)(tokenField.GetValue(method) ?? 0u);
        var candidate = string.IsNullOrWhiteSpace(parameterSuffix) ? $"{baseName}_{token:X8}" : $"{baseName}__{parameterSuffix}";

        if (usedMethodNames.Add(candidate))
        {
            return candidate;
        }

        var withToken = $"{candidate}_{token:X8}";
        usedMethodNames.Add(withToken);

        return withToken;
    }

    private static string SanitizeIdentifier(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();

        var sanitized = new string(characters).Trim('_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "value";
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private static int GetPrimitiveTypeSize(string typeName)
    {
        return typeName switch
        {
            "bool" or "byte" or "sbyte" => 1,
            "short" or "ushort" or "char" => 2,
            "int" or "uint" or "float" => 4,
            "long" or "ulong" or "double" => 8,
            _ => 4,
        };
    }

    private static long ConvertToInt64(object value)
    {
        return value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            int intValue => intValue,
            uint uintValue => uintValue,
            long longValue => longValue,
            ulong ulongValue => unchecked((long)ulongValue),
            _ => Convert.ToInt64(value),
        };
    }
}
