using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Codefinity.EventEmitter.Sample.Infrastructure;

/// <summary>
/// One line per log entry: the thread that logged it, the short category name, and the message.
/// The thread id is what shows whether a listener ran inline or on a background worker.
/// </summary>
internal sealed class ShortConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "short";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var category = logEntry.Category[(logEntry.Category.LastIndexOf('.') + 1)..];
        if (category == "EventDispatcherHostedService")
        {
            category = "EventEmitter";
        }

        var level = logEntry.LogLevel switch
        {
            LogLevel.Warning => "WARN: ",
            LogLevel.Error or LogLevel.Critical => "ERROR: ",
            _ => "",
        };

        textWriter.Write($"  [thread {Environment.CurrentManagedThreadId,2}] {category,-20} {level}");
        textWriter.Write(logEntry.Formatter(logEntry.State, logEntry.Exception));

        if (logEntry.Exception is { } exception)
        {
            textWriter.Write($" -> {exception.GetType().Name}: {exception.Message}");
        }

        textWriter.WriteLine();
    }
}
