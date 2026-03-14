using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AmongUsDumper.Cli;
using AmongUsDumper.Logging;

namespace AmongUsDumper.Discovery;

internal static class GameLocator
{
    private static readonly Regex UnityVersionPattern = new(
        @"20\d{2}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static GamePaths Resolve(Options options, AppLogger logger)
    {
        var gameDirectory = ResolveGameDirectory(options, logger);

        var executablePath = ResolvePath(
            options.ExecutablePath,
            gameDirectory is null ? null : Path.Combine(gameDirectory, "Among Us.exe"),
            "Among Us.exe");

        var finalGameDirectory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Unable to determine game directory.");

        var gameAssemblyPath = ResolvePath(
            options.GameAssemblyPath,
            Path.Combine(finalGameDirectory, "GameAssembly.dll"),
            "GameAssembly.dll");

        var metadataPath = ResolvePath(
            options.MetadataPath,
            Path.Combine(finalGameDirectory, "Among Us_Data", "il2cpp_data", "Metadata", "global-metadata.dat"),
            "global-metadata.dat");

        var unityVersion = options.UnityVersion ?? DetectUnityVersion(finalGameDirectory, logger);

        return new GamePaths(finalGameDirectory, executablePath, gameAssemblyPath, metadataPath, unityVersion);
    }

    private static string? ResolveGameDirectory(Options options, AppLogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.GameDirectory))
        {
            var fullPath = Path.GetFullPath(options.GameDirectory);

            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Game directory was not found: {fullPath}");
            }

            return fullPath;
        }

        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            var fullPath = Path.GetFullPath(options.ExecutablePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Executable path was not found.", fullPath);
            }

            return Path.GetDirectoryName(fullPath);
        }

        var fromProcess = TryFindRunningGameDirectory(options.ProcessName, logger);
        if (fromProcess is not null)
        {
            return fromProcess;
        }

        var fromSteam = TryFindSteamInstallDirectory(logger);
        if (fromSteam is not null)
        {
            return fromSteam;
        }

        throw new InvalidOperationException(
            "Unable to locate Among Us automatically. Start the game or pass --game-dir / --exe-path.");
    }

    private static string ResolvePath(string? explicitPath, string? defaultPath, string description)
    {
        var candidate = explicitPath ?? defaultPath;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException($"Unable to resolve {description}.");
        }

        var fullPath = Path.GetFullPath(candidate);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description} was not found.", fullPath);
        }

        return fullPath;
    }

    private static string? TryFindRunningGameDirectory(string processName, AppLogger logger)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(processName);
        var candidates = Process.GetProcessesByName(normalizedName);

        foreach (var process in candidates)
        {
            try
            {
                var path = process.MainModule?.FileName;

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                logger.Debug($"resolved game directory from running process {process.ProcessName} ({process.Id})");
                return Path.GetDirectoryName(path);
            }
            catch
            {
                // Ignore inaccessible processes and continue searching.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static string? TryFindSteamInstallDirectory(AppLogger logger)
    {
        foreach (var library in EnumerateSteamLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", "Among Us");
            if (Directory.Exists(candidate))
            {
                logger.Debug($"resolved game directory from Steam library: {candidate}");
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in GetDefaultSteamRoots())
        {
            if (Directory.Exists(path) && seen.Add(path))
            {
                yield return path;
            }
        }

        foreach (var vdfPath in GetLibraryFolderFiles())
        {
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            var content = File.ReadAllText(vdfPath);
            var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var value = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(value) && seen.Add(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> GetDefaultSteamRoots()
    {
        yield return @"C:\Program Files (x86)\Steam";
        yield return @"C:\Program Files\Steam";
        yield return @"D:\SteamLibrary";
        yield return @"E:\SteamLibrary";
    }

    private static IEnumerable<string> GetLibraryFolderFiles()
    {
        foreach (var root in GetDefaultSteamRoots())
        {
            yield return Path.Combine(root, "steamapps", "libraryfolders.vdf");
        }
    }

    private static string DetectUnityVersion(string gameDirectory, AppLogger logger)
    {
        foreach (var candidate in GetUnityVersionCandidates(gameDirectory))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var version = TryExtractUnityVersion(candidate);
            if (!string.IsNullOrWhiteSpace(version))
            {
                logger.Debug($"detected Unity version {version} from {candidate}");
                return version;
            }
        }

        throw new InvalidOperationException(
            "Unable to detect Unity version automatically. Pass --unity-version, for example --unity-version 2021.3.28f1.");
    }

    private static IEnumerable<string> GetUnityVersionCandidates(string gameDirectory)
    {
        yield return Path.Combine(gameDirectory, "Among Us_Data", "globalgamemanagers");
        yield return Path.Combine(gameDirectory, "Among Us_Data", "globalgamemanagers.assets");
        yield return Path.Combine(gameDirectory, "Among Us_Data", "data.unity3d");
        yield return Path.Combine(gameDirectory, "Among Us_Data", "mainData");
        yield return Path.Combine(gameDirectory, "UnityPlayer.dll");
        yield return Path.Combine(gameDirectory, "GameAssembly.dll");
    }

    private static string? TryExtractUnityVersion(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var text = Encoding.ASCII.GetString(bytes);
        var matches = UnityVersionPattern.Matches(text);

        return matches
            .Select(match => match.Value)
            .FirstOrDefault(version => version.StartsWith("20", StringComparison.Ordinal));
    }
}

internal sealed record GamePaths(
    string GameDirectory,
    string ExecutablePath,
    string GameAssemblyPath,
    string MetadataPath,
    string UnityVersion);
