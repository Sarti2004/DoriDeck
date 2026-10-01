using MacroDeck.Localization;
using MacroDeck.PianoFolder.Piano;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeck.PianoFolder.Notes;

public sealed class NoteDispatcher(INoteOutput output, ILogger logger) : INoteDispatcher
{
    private readonly ILogger _logger = logger.ForContext<NoteDispatcher>();

    public async Task<ActionResult> DispatchAsync(string noteId, int transposeSemitones, CancellationToken cancellationToken)
    {
        if (!PianoKeyboardModel.TryGetKey(noteId, out var key))
        {
            _logger.Warning("Rejected unknown note id {NoteId}.", noteId);
            return ActionResult.Failed(
                ActionErrorCodes.InvalidParameter,
                LocalizedText.FromLiteral($"'{noteId}' is not a recognized note id."));
        }

        var midiNumber = key.MidiNumber + PianoKeyboardModel.ClampTranspose(transposeSemitones);

        try
        {
            await output.SendNoteOnAsync(midiNumber, cancellationToken).ConfigureAwait(false);
            return ActionResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Note output failed for {NoteId} ({MidiNumber}).", noteId, midiNumber);
            return ActionResult.Failed(ActionErrorCodes.ProviderError, LocalizedText.FromLiteral(exception.Message));
        }
    }
}
