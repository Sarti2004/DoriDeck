using DoriDeck.Services.MacOS;
using DoriDeck.Services.Windows;

namespace DoriDeck.Services;

/// <summary>
/// Cross-platform keyboard service facade.
/// Selects the platform-specific implementation at runtime.
/// </summary>
public sealed class KeyboardService : IKeyboardService
{
    private readonly IKeyboardService _implementation;

    public KeyboardService()
    {
        _implementation = CreateImplementation();
    }

    internal KeyboardService(IKeyboardService implementation)
    {
        _implementation = implementation
            ?? throw new ArgumentNullException(nameof(implementation));
    }

    public void Press(DoriDeckKey key)
        => _implementation.Press(key);

    public void PressChord(DoriDeckKey modifier, DoriDeckKey key)
        => _implementation.PressChord(modifier, key);

    public void EnterText(string text)
        => _implementation.EnterText(text);

    public bool TryRelease(DoriDeckKey key)
        => _implementation.TryRelease(key);

    public void ReleaseModifiersSafely()
        => _implementation.ReleaseModifiersSafely();

    private static IKeyboardService CreateImplementation()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsKeyboardService();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSKeyboardService();
        }

        throw new PlatformNotSupportedException(
            "Keyboard simulation is currently supported only on Windows and macOS.");
    }
}
