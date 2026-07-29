using WindowsInput;

namespace DoriDeck.Services;

public interface IKeyboardService
{
    void Press(VirtualKeyCode key);

    void PressChord(
        VirtualKeyCode modifier,
        VirtualKeyCode key);

    void EnterText(string text);

    bool TryRelease(VirtualKeyCode key);

    void ReleaseModifiersSafely();
}