namespace DoriDeck.Services;

internal interface IClipboardPlatform
{
    string? ReadUnicodeText(int retryCount, TimeSpan retryDelay);
    void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay);
}
