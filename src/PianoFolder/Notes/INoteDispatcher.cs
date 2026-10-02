using MacroDeck.Sdk.Actions;

namespace DoriDeck.PianoFolder.Notes;


public interface INoteDispatcher
{
    Task<ActionResult> DispatchAsync(string noteId, int transposeSemitones, CancellationToken cancellationToken);
}
