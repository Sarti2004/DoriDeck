namespace DoriDeck.Services;

public interface IApplicationFocusService
{
    bool IsForeground(string processNamePrefix);

    void EnsureForeground(
        string processNamePrefix,
        string? errorMessage = null);
}