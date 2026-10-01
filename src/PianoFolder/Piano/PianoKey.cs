namespace MacroDeck.PianoFolder.Piano;


public sealed record PianoKey(string NoteId, string ElementKey, int MidiNumber, bool IsAccidental);

/// <summary>
/// One black-band slot, one per natural (see <see cref="PianoKeyboardModel.BuildBlackBandSlots"/>).
/// <see cref="Natural"/> is the natural key this slot sits above; <see cref="Accidental"/> is non-null
/// only when that natural has a trailing sharp, in which case the view splits this slot's own width
/// between a continuation filler and that accidental rather than filling the whole slot with one or the
/// other.
/// </summary>
public sealed record BlackBandSlot(PianoKey Natural, PianoKey? Accidental);
