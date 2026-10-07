using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.PianoFolder.Piano;

public sealed class PianoKeyboardViewModel
{
    public PianoKeyboardViewModel(
        INoteDispatcher dispatcher,
        PianoKeyArtwork? artwork = null,
        Func<CancellationToken, Task>? goBack = null)
    {
        Dispatcher = dispatcher;
        Artwork = artwork;
        GoBack = goBack;
        Octaves = new UiState<int>(PianoKeyboardModel.DefaultOctaveCount);
        Transpose = new UiState<int>(0);

        // Copy paste;
        PressedByNoteId = PianoKeyboardModel.AllPlayableKeys().ToDictionary(
            key => key.NoteId,
            _ => new UiState<bool>(false),
            StringComparer.Ordinal);
    }

    public INoteDispatcher Dispatcher { get; }

    public PianoKeyArtwork? Artwork { get; }

    public Func<CancellationToken, Task>? GoBack { get; }

    public UiState<bool> ShowNoteInput { get; } = new(false);

    public UiState<NoteDuration> Duration { get; } = new(NoteDuration.Quarter);

    public UiState<int> RhythmDots { get; } = new(0);

    public UiState<bool> NoteInputActive { get; } = new(false);

    public UiState<int> Octaves { get; }

    public UiState<int> Transpose { get; }

    public UiState<int> TransposeOctaveNumber { get; } = new(0);

    public IReadOnlyDictionary<string, UiState<bool>> PressedByNoteId { get; }
}
