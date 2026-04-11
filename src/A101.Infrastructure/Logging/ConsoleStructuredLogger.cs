using A101.Domain.Ports;

namespace A101.Infrastructure.Logging;

/// <summary>
/// Console-backed structured logger for standalone CLI and debug-friendly runs.
/// </summary>
public sealed class ConsoleStructuredLogger : IStructuredLogger
{
    public void Info(string message, params (string Key, object? Value)[] context)
    {
        Write("INF", message, null, context);
    }

    public void Warn(string message, params (string Key, object? Value)[] context)
    {
        Write("WRN", message, null, context);
    }

    public void Error(string message, Exception? ex = null, params (string Key, object? Value)[] context)
    {
        Write("ERR", message, ex, context);
    }

    private static void Write(string level, string message, Exception? ex, params (string Key, object? Value)[] context)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var renderedContext = context.Length == 0
            ? string.Empty
            : string.Join(", ", context.Select(c => $"{c.Key}={c.Value ?? "<null>"}"));
        var exceptionMessage = ex is null ? string.Empty : $" | {ex.GetType().Name}: {ex.Message}";
        var suffix = string.IsNullOrWhiteSpace(renderedContext) ? string.Empty : $" | {renderedContext}";

        Console.WriteLine($"[{level}] {timestamp} {message}{suffix}{exceptionMessage}");
    }
}