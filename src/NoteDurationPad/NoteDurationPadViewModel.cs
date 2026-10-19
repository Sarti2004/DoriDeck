using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.NoteDurationPad;

public sealed class NoteDurationPadViewModel
{
    public required INoteDispatcher Dispatcher { get; init; }

    public IReadOnlyDictionary<NoteDuration, UiResource> DurationIcons { get; init; } =
        new Dictionary<NoteDuration, UiResource>();

    public IReadOnlyDictionary<NoteDuration, UiResource> DottedDurationIcons { get; init; } =
        new Dictionary<NoteDuration, UiResource>();

    public UiResource? Background { get; init; }

    public UiResource? NoteInputIcon { get; init; }

    public required Func<CancellationToken, Task> Delete { get; init; }

    public required Func<CancellationToken, Task> Play { get; init; }

    public required Func<CancellationToken, Task> Record { get; init; }

    public required Func<CancellationToken, Task> EnterNoteInput { get; init; }

    public required Func<CancellationToken, Task> Up { get; init; }

    public required Func<CancellationToken, Task> Down { get; init; }

    public required Func<CancellationToken, Task> Left { get; init; }

    public required Func<CancellationToken, Task> Right { get; init; }

    public UiState<NoteDuration> Duration { get; } = new(NoteDuration.Quarter);

    public UiState<int> RhythmDots { get; } = new(0);

    public UiState<bool> InPlayback { get; } = new(false);

    public UiState<bool> IsRecording { get; } = new(false);

    public UiState<bool> NoteInputActive { get; } = new(false);
}
