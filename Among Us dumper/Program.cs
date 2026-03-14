using AmongUsDumper.Analysis;
using AmongUsDumper.Cli;
using AmongUsDumper.Discovery;
using AmongUsDumper.Generation;
using AmongUsDumper.Logging;

namespace AmongUsDumper;

internal static class Program
{
    private static int Main(string[] args)
    {
        Options options;

        try
        {
            options = Options.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.HelpText);

            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Options.HelpText);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
            return 0;
        }

        using var logger = new AppLogger(options.Verbosity, options.NoLogFile ? null : "amongus-dumper.log");

        try
        {
            var gamePaths = GameLocator.Resolve(options, logger);

            logger.Info($"game directory: {gamePaths.GameDirectory}");
            logger.Info($"game assembly: {gamePaths.GameAssemblyPath}");
            logger.Info($"metadata file: {gamePaths.MetadataPath}");
            logger.Info($"unity version: {gamePaths.UnityVersion}");

            var analyzer = new AmongUsAnalyzer(logger);
            var result = analyzer.Analyze(gamePaths);

            var writer = new DumperOutputWriter(logger);
            writer.WriteAll(result, options);

            logger.Info("analysis completed successfully");

            return 0;
        }
        catch (Exception exception)
        {
            logger.Error(exception.ToString());
            return 1;
        }
    }
}
