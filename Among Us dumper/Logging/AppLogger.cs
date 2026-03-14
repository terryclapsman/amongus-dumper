using System.Text;

namespace AmongUsDumper.Logging;

internal sealed class AppLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly int _verbosity;
    private readonly StreamWriter? _fileWriter;

    public AppLogger(int verbosity, string? logFilePath)
    {
        _verbosity = verbosity;

        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            _fileWriter = new StreamWriter(File.Open(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true,
            };
        }
    }

    public void Info(string message) => Write("INFO", message, ConsoleColor.White, 0);

    public void Debug(string message) => Write("DEBUG", message, ConsoleColor.DarkGray, 1);

    public void Trace(string message) => Write("TRACE", message, ConsoleColor.Gray, 2);

    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow, 0);

    public void Error(string message) => Write("ERROR", message, ConsoleColor.Red, 0);

    private void Write(string level, string message, ConsoleColor color, int minimumVerbosity)
    {
        if (_verbosity < minimumVerbosity)
        {
            return;
        }

        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        lock (_sync)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            if (level == "ERROR")
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }

            Console.ForegroundColor = originalColor;
            _fileWriter?.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _fileWriter?.Dispose();
    }
}
