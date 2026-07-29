using WindowsInput;

namespace DoriDeck.Services;

public sealed class KeyboardService : IKeyboardService
{
    private readonly InputSimulator _simulator;

    public KeyboardService()
        : this(new InputSimulator())
    {
    }

    internal KeyboardService(InputSimulator simulator)
    {
        _simulator = simulator
            ?? throw new ArgumentNullException(nameof(simulator));
    }

    public void Press(VirtualKeyCode key)
    {
        _simulator.Keyboard.KeyPress(key);
    }

    public void PressChord(
        VirtualKeyCode modifier,
        VirtualKeyCode key)
    {
        _simulator.Keyboard.ModifiedKeyStroke(modifier, key);
    }

    public void EnterText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _simulator.Keyboard.TextEntry(text);
    }

    public bool TryRelease(VirtualKeyCode key)
    {
        try
        {
            _simulator.Keyboard.KeyUp(key);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ReleaseModifiersSafely()
    {
        TryRelease(VirtualKeyCode.CONTROL);
        TryRelease(VirtualKeyCode.LSHIFT);
        TryRelease(VirtualKeyCode.RSHIFT);
        TryRelease(VirtualKeyCode.LMENU);
        TryRelease(VirtualKeyCode.RMENU);
        TryRelease(VirtualKeyCode.LWIN);
        TryRelease(VirtualKeyCode.RWIN);
    }
}