using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Logging;

/// <summary>
/// Appends log lines to <c>iris-yyyyMMdd.log</c> in a folder and keeps only the newest few files.
/// Never receives tokens or passwords: callers log paths, status codes and ids only.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int KeepFiles = 7;
    private readonly string _folder;
    private readonly object _gate = new();

    public FileLoggerProvider(string folder)
    {
        _folder = folder;
        try
        {
            Directory.CreateDirectory(folder);
            var old = new DirectoryInfo(folder).GetFiles("iris-*.log");
            Array.Sort(old, (a, b) => b.Name.CompareTo(a.Name));
            for (var i = KeepFiles; i < old.Length; i++)
            {
                old[i].Delete();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(Path.Combine(_folder, $"iris-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var name = category[(category.LastIndexOf('.') + 1)..];
            var line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel.ToString()[..3].ToUpperInvariant()} {name}: {formatter(state, exception)}";
            owner.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
