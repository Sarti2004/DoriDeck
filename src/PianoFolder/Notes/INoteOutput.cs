namespace MacroDeck.PianoFolder.Notes;


public interface INoteOutput
{
    Task SendNoteOnAsync(int midiNumber, CancellationToken cancellationToken);
}
