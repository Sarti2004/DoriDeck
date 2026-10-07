using MacroDeck.Sdk.Actions;

namespace DoriDeck.PianoFolder.Notes;


public interface INoteDispatcher
{
    Task<ActionResult> DispatchAsync(string noteId, int transposeSemitones, CancellationToken cancellationToken);

    Task<ActionResult> DispatchDurationAsync(NoteDuration duration, CancellationToken cancellationToken) =>
        Task.FromResult(ActionResult.Success());

      Task<ActionResult> DispatchReturnAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ActionResult.Success());

    Task<ActionResult> DispatchForwardAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ActionResult.Success());

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
}
