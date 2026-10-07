namespace DoriDeck.PianoFolder.Notes;


public interface INoteOutput
{
    Task SendNoteOnAsync(int midiNumber, CancellationToken cancellationToken);

    Task SendDurationAsync(NoteDuration duration, CancellationToken cancellationToken) => Task.CompletedTask;

    Task SendRhythmDotsAsync(int rhythmDots, CancellationToken cancellationToken) => Task.CompletedTask;

    Task SendReturnAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task SendForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    NoteDuration? CurrentDuration => null;

    event Action<NoteDuration>? DurationChanged
    {
        add { }
        remove { }
    }

    bool? NoteInputActive => null;

    event Action<bool>? NoteInputActiveChanged
    {
        add { }
        remove { }
    }

    int? RhythmDots => null;

    event Action<int>? RhythmDotsChanged
    {
        add { }
        remove { }
    }
}
