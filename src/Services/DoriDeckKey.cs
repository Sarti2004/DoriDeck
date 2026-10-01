namespace DoriDeck.Services;

/// <summary>
/// Platform-independent key vocabulary for <see cref="IKeyboardService"/>. Each platform's
/// implementation maps these onto its own virtual key codes.
/// </summary>
public enum DoriDeckKey
{
	Return,
	Escape,
	UpArrow,
	DownArrow,
	LeftArrow,
	RightArrow,
	A,
	C,

	/// <summary>The generic Shift key, used for selection-extension chords.</summary>
	Shift,
	Control,
	LeftShift,
	RightShift,
	LeftAlt,
	RightAlt,

	/// <summary>The left platform modifier key: Windows on Windows, Command on macOS.</summary>
	LeftMeta,

	/// <summary>The right platform modifier key: Windows on Windows, Command on macOS.</summary>
	RightMeta,

	/// <summary>
	/// The OS convention modifier for copy/select-all style shortcuts: Control on Windows, Command
	/// on macOS.
	/// </summary>
	PrimaryModifier,
}
