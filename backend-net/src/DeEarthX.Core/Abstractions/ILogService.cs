namespace DeEarthX.Core.Abstractions;

public interface ILogService
{
    void Debug(string message, object? meta = null);
    void Info(string message, object? meta = null);
    void Warn(string message, object? meta = null);
    void Error(string message, object? meta = null);
}
