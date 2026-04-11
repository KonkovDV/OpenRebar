namespace A101.Domain.Ports;

/// <summary>
/// Simple structured logger abstraction for standalone orchestration and infrastructure adapters.
/// </summary>
public interface IStructuredLogger
{
    void Info(string message, params (string Key, object? Value)[] context);
    void Warn(string message, params (string Key, object? Value)[] context);
    void Error(string message, Exception? ex = null, params (string Key, object? Value)[] context);
}