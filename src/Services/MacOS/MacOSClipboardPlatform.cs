using DoriDeck.Services;

namespace DoriDeck.Services.MacOS;

internal sealed class MacOSClipboardPlatform : IClipboardPlatform
{
    public string? ReadUnicodeText(int retryCount, TimeSpan retryDelay) =>
        NSPasteboardInterop.ReadUnicodeText();

    public void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay) =>
        NSPasteboardInterop.WriteUnicodeText(text);
}
