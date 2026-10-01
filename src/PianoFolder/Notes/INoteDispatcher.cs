using MacroDeck.Sdk.Actions;

namespace MacroDeck.PianoFolder.Notes;


public interface INoteDispatcher
{
    Task<ActionResult> DispatchAsync(string noteId, int transposeSemitones, CancellationToken cancellationToken);
}
