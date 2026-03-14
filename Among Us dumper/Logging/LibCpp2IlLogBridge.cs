using LibCpp2IL.Logging;

namespace AmongUsDumper.Logging;

internal sealed class LibCpp2IlLogBridge : LogWriter
{
    private readonly AppLogger _logger;

    public LibCpp2IlLogBridge(AppLogger logger)
    {
        _logger = logger;
    }

    public override void Info(string message) => _logger.Debug(message.TrimEnd());

    public override void Warn(string message) => _logger.Warn(message.TrimEnd());

    public override void Error(string message) => _logger.Error(message.TrimEnd());

    public override void Verbose(string message) => _logger.Trace(message.TrimEnd());
}
