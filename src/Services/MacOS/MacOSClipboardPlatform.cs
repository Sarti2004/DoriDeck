using System.Diagnostics;
using System.Text;
using DoriDeck.Services;

namespace DoriDeck.Services.MacOS;

internal sealed class MacOSClipboardPlatform : IClipboardPlatform
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string? ReadUnicodeText(int retryCount, TimeSpan retryDelay)
    {
        var attempts = Math.Max(1, retryCount);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var text = RunClipboardCommand("/usr/bin/pbpaste");
            if (text.Length > 0)
            {
                return text;
            }

            if (attempt + 1 < attempts)
            {
                Thread.Sleep(retryDelay);
            }
        }

        return null;
    }

    public void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(text);

        using var process = StartClipboardCommand("/usr/bin/pbcopy", redirectInput: true);
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(text);
        process.StandardInput.Close();

        process.WaitForExit();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pbcopy failed with exit code {process.ExitCode}: {standardError}");
        }
    }

    private static string RunClipboardCommand(string executable)
    {
        using var process = StartClipboardCommand(executable, redirectOutput: true);
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} failed with exit code {process.ExitCode}: {standardError}");
        }

        return output;
    }

    private static Process StartClipboardCommand(
        string executable,
        bool redirectInput = false,
        bool redirectOutput = false)
    {
        var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true,
            StandardInputEncoding = redirectInput ? Utf8 : null,
            StandardOutputEncoding = redirectOutput ? Utf8 : null,
            StandardErrorEncoding = Utf8
        });

        return process ?? throw new InvalidOperationException($"Failed to start {executable}.");
    }
}
