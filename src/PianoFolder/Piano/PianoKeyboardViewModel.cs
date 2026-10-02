using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.PianoFolder.Piano;

public sealed class PianoKeyboardViewModel
{
    public PianoKeyboardViewModel(INoteDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
        Octaves = new UiState<int>(PianoKeyboardModel.DefaultOctaveCount);
        Transpose = new UiState<int>(0);

        // Built once for the full playable range so switching octave count never needs to add or remove
        // dictionary entries - only which keys are currently rendered (and thus reachable) changes.
        PressedByNoteId = PianoKeyboardModel.AllPlayableKeys().ToDictionary(
            key => key.NoteId,
            _ => new UiState<bool>(false),
            StringComparer.Ordinal);
    }

    public INoteDispatcher Dispatcher { get; }

    public UiState<int> Octaves { get; }

    public UiState<int> Transpose { get; }

    public UiState<int> TransposeOctaveNumber { get; } = new(0);

    public IReadOnlyDictionary<string, UiState<bool>> PressedByNoteId { get; }
}
