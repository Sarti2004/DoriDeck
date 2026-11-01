using MacroDeck.Ui.Config.Options;

namespace DoriDeck.Actions;

/// Supplies autocomplete options for our own editors (e.g. the MIDI bridge), which, unlike Macro Deck's
/// standard combobox, can show a label that differs from the value.
internal interface IUiOptionsProvider
{
	/// Options for <paramref name="parameterName"/> matching <paramref name="filter"/>, at most <paramref name="limit"/>.
	IReadOnlyList<UiOption> GetUiOptions(string parameterName, string? filter, int limit);
}
