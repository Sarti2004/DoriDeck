namespace DoriDeck.PianoFolder.Piano;

public sealed record PianoKey(string NoteId, string ElementKey, int MidiNumber, bool IsAccidental);

public sealed record PianoOctave(IReadOnlyList<PianoKey> Keys)
{
    public string ElementKey => $"octave-{Keys[0].NoteId}";
}

/// Black keys
public sealed record BlackBandSlot(PianoKey Natural, PianoKey? Accidental);
