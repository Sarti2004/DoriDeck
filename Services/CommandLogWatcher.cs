using System.Diagnostics;
using System.IO;
using DoricoNet;
using DoricoNet.Commands;

namespace DoriDeck.Services;

public sealed class CommandLogWatcher : IDisposable
{
    private const int PollIntervalMilliseconds = 25;
    private const string CompletionMarker = "notifyPostCommandExecute: ";
    private const string ExecutingMarker = "Executing command: ";
    private static readonly TimeSpan LogConfirmationTimeout = TimeSpan.FromSeconds(2);

    private readonly StreamReader? _reader;

    private CommandLogWatcher(StreamReader? reader)
    {
        _reader = reader;
    }


    public static CommandLogWatcher Open(string logPath)
    {
        try
        {
            var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            stream.Seek(0, SeekOrigin.End);

            return new CommandLogWatcher(new StreamReader(stream));
        }
        catch
        {
            // No log confirmation available; callers fall back to a fixed delay.
            return new CommandLogWatcher(null);
        }
    }


    public async Task SendCommandAsync(
        string commandName,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default)
    {

        await WaitForCommandCompletionAsync(
            commandName,
            fallbackDelay,
            cancellationToken);
    }


    public async Task WaitForCommandCompletionAsync(
        string commandName,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default)
    {
        if (_reader is null)
        {
            await Task.Delay(fallbackDelay, cancellationToken);
            return;
        }

        await WaitForLineAsync(
            line =>
                TryGetCompletedCommand(line, out var completedCommand) &&
                string.Equals(completedCommand, commandName, StringComparison.Ordinal)
                    ? completedCommand
                    : null,
            LogConfirmationTimeout,
            cancellationToken);
    }


    public Task<string?> WaitForExecutingCommandAsync(
        string commandNamePrefix,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return WaitForLineAsync(
            line =>
            {
                var command = TryGetExecutingCommand(line);
                return command is not null &&
                    command.StartsWith(commandNamePrefix, StringComparison.Ordinal)
                        ? command
                        : null;
            },
            timeout,
            cancellationToken);
    }


    private async Task<string?> WaitForLineAsync(
        Func<string, string?> tryMatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_reader is null)
        {
            return null;
        }

        var started = Stopwatch.StartNew();

        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string? line;
                while ((line = _reader.ReadLine()) is not null)
                {
                    var match = tryMatch(line);
                    if (match is not null)
                    {
                        return match;
                    }
                }
            }
            catch
            {
                // stop polling and do fallback timeout.
                break;
            }

            var remaining = timeout - started.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(PollIntervalMilliseconds)
                    ? remaining
                    : TimeSpan.FromMilliseconds(PollIntervalMilliseconds),
                cancellationToken);
        }

        return null;
    }

    private static bool TryGetCompletedCommand(string line, out string commandName)
    {
        var markerIndex = line.IndexOf(CompletionMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            commandName = string.Empty;
            return false;
        }

        var start = markerIndex + CompletionMarker.Length;
        var end = line.IndexOf(" (", start, StringComparison.Ordinal);

        commandName = end > start ? line[start..end] : line[start..].Trim();
        return true;
    }

    private static string? TryGetExecutingCommand(string line)
    {
        var markerIndex = line.IndexOf(ExecutingMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        return line[(markerIndex + ExecutingMarker.Length)..].Trim();
    }

    public string? ReadNextLine() => _reader?.ReadLine();

    public void SkipToEnd()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            _reader.ReadToEnd();
        }
        catch
        {
        }
    }

    public void Dispose() => _reader?.Dispose();
}
