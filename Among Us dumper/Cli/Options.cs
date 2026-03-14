using System.Globalization;

namespace AmongUsDumper.Cli;

internal sealed record Options(
    IReadOnlyList<string> FileTypes,
    int IndentSize,
    string OutputDirectory,
    string? GameDirectory,
    string? ExecutablePath,
    string? GameAssemblyPath,
    string? MetadataPath,
    string ProcessName,
    string? UnityVersion,
    int Verbosity,
    bool NoLogFile,
    bool ShowHelp,
    bool ShowVersion)
{
    public static string HelpText =>
        """
        Among Us dumper

        Usage:
          amongus-dumper [options]

        Options:
          -f, --file-types <types>       Comma-separated output types. Default: cs,hpp,json,rs,zig
          -i, --indent-size <size>       Indent size. Default: 4
          -o, --output <dir>             Output directory. Default: output next to the exe
          -g, --game-dir <dir>           Among Us installation directory
          -e, --exe-path <path>          Full path to Among Us.exe
          -a, --game-assembly <path>     Full path to GameAssembly.dll
          -m, --metadata <path>          Full path to global-metadata.dat
          -p, --process-name <name>      Running process name. Default: Among Us
          -u, --unity-version <ver>      Override Unity version, for example 2021.3.28f1
          -v                             Increase verbosity. Use -vv for extra logs
          --no-log-file                  Do not create amongus-dumper.log
          -h, --help                     Show help
          -V, --version                  Show version
        """;

    public static Options Parse(string[] args)
    {
        var fileTypes = new List<string> { "cs", "hpp", "json", "rs", "zig" };
        var indentSize = 4;
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
        string? gameDirectory = null;
        string? executablePath = null;
        string? gameAssemblyPath = null;
        string? metadataPath = null;
        var processName = "Among Us";
        string? unityVersion = null;
        var verbosity = 0;
        var noLogFile = false;
        var showHelp = false;
        var showVersion = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "-f":
                case "--file-types":
                    fileTypes = SplitCsv(ReadValue(args, ref index, argument));
                    break;
                case "-i":
                case "--indent-size":
                    indentSize = int.Parse(ReadValue(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "-o":
                case "--output":
                    outputDirectory = ReadValue(args, ref index, argument);
                    break;
                case "-g":
                case "--game-dir":
                    gameDirectory = ReadValue(args, ref index, argument);
                    break;
                case "-e":
                case "--exe-path":
                    executablePath = ReadValue(args, ref index, argument);
                    break;
                case "-a":
                case "--game-assembly":
                    gameAssemblyPath = ReadValue(args, ref index, argument);
                    break;
                case "-m":
                case "--metadata":
                    metadataPath = ReadValue(args, ref index, argument);
                    break;
                case "-p":
                case "--process-name":
                    processName = ReadValue(args, ref index, argument);
                    break;
                case "-u":
                case "--unity-version":
                    unityVersion = ReadValue(args, ref index, argument);
                    break;
                case "-v":
                    verbosity++;
                    break;
                case "--no-log-file":
                    noLogFile = true;
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "-V":
                case "--version":
                    showVersion = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (indentSize <= 0)
        {
            throw new ArgumentException("Indent size must be greater than zero.");
        }

        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs",
            "hpp",
            "json",
            "rs",
            "zig",
        };

        var normalizedFileTypes = fileTypes
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(type => type.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedFileTypes.Count == 0)
        {
            throw new ArgumentException("At least one file type must be specified.");
        }

        if (normalizedFileTypes.Any(type => !supportedTypes.Contains(type)))
        {
            var unsupported = normalizedFileTypes.Where(type => !supportedTypes.Contains(type));
            throw new ArgumentException($"Unsupported file type(s): {string.Join(", ", unsupported)}");
        }

        return new Options(
            normalizedFileTypes,
            indentSize,
            outputDirectory,
            gameDirectory,
            executablePath,
            gameAssemblyPath,
            metadataPath,
            processName,
            unityVersion,
            verbosity,
            noLogFile,
            showHelp,
            showVersion);
    }

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {argument}");
        }

        index++;
        return args[index];
    }

    private static List<string> SplitCsv(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
