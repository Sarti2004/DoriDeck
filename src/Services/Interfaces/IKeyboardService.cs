namespace DoriDeck.Services;

public interface IKeyboardService
{
	void Press(DoriDeckKey key);

	void PressChord(
		DoriDeckKey modifier,
		DoriDeckKey key);

	void EnterText(string text);

	bool TryRelease(DoriDeckKey key);

	void ReleaseModifiersSafely();
}
