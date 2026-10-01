using MacroDeck.Ui.Dsl;

namespace MacroDeck.PianoFolder.Piano;

public static class PianoKeyboardModel
{
    public const int MinOctaveCount = 1;
    public const int MaxOctaveCount = 4;
    public const int DefaultOctaveCount = 2;

    public const int MinTranspose = -12;
    public const int MaxTranspose = 12;

    public const int BaseOctaveNumber = 4;

    public const int MinTransposeOctaveNumber = -2;
    public const int MaxTransposeOctaveNumber = 2;

    public static int TransposeOctaveNumber { get; }

    private const string ElementKeyPrefix = "piano-";

    private static readonly (char Letter, int SemitoneOffset, bool HasSharpAfter)[] NaturalSteps =
    [
        ('c', 0, true),
        ('d', 2, true),
        ('e', 4, false),
        ('f', 5, true),
        ('g', 7, true),
        ('a', 9, true),
        ('b', 11, false),
    ];

    public static string ElementKeyFor(string noteId) => ElementKeyPrefix + noteId;

    public static int ClampOctaveCount(int value) => Math.Clamp(value, MinOctaveCount, MaxOctaveCount);

    public static int ClampTranspose(int value) => Math.Clamp(value, MinTranspose, MaxTranspose);

    public static int ClampTransposeOctaveNumber(int value) =>
        Math.Clamp(value, MinTransposeOctaveNumber, MaxTransposeOctaveNumber);

    public static IReadOnlyList<PianoKey> BuildNaturalKeys(int octaveCount) =>
        BuildNaturalKeys(octaveCount, 0);

    public static IReadOnlyList<PianoKey> BuildNaturalKeys(int octaveCount, int octaveShift)
    {
        octaveCount = ClampOctaveCount(octaveCount);
        octaveShift = ClampTransposeOctaveNumber(octaveShift);

        var keys = new List<PianoKey>(7 * octaveCount);
        for (var octave = 0; octave < octaveCount; octave++)
        {
            foreach (var step in NaturalSteps)
            {
                keys.Add(CreateKey(
                    step.Letter,
                    step.SemitoneOffset,
                    octave,
                    octaveShift,
                    isAccidental: false));
            }
        }

        return keys;
    }


    public static IReadOnlyList<PianoKey> BuildAccidentalKeys(int octaveCount) =>
        BuildAccidentalKeys(octaveCount, 0);

    public static IReadOnlyList<PianoKey> BuildAccidentalKeys(int octaveCount, int octaveShift)
    {
        octaveCount = ClampOctaveCount(octaveCount);
        octaveShift = ClampTransposeOctaveNumber(octaveShift);

        var keys = new List<PianoKey>(5 * octaveCount);
        for (var octave = 0; octave < octaveCount; octave++)
        {
            foreach (var step in NaturalSteps)
            {
                if (step.HasSharpAfter)
                {
                    keys.Add(CreateKey(
                        step.Letter,
                        step.SemitoneOffset + 1,
                        octave,
                        octaveShift,
                        isAccidental: true));
                }
            }
        }

        return keys;
    }

   
    public static IReadOnlyList<PianoKey> BuildChromaticKeys(int octaveCount) =>
        BuildChromaticKeys(octaveCount, 0);

    private static IReadOnlyList<PianoKey> BuildChromaticKeys(int octaveCount, int octaveShift) =>
        BuildNaturalKeys(octaveCount, octaveShift)
            .Concat(BuildAccidentalKeys(octaveCount, octaveShift))
            .OrderBy(key => key.MidiNumber)
            .ToList();

    /// The black band's row, one slot per natural - the same count and the same equal <c>fill</c> share
    /// per slot as the white band 
    public static IReadOnlyList<BlackBandSlot> BuildBlackBandSlots(int octaveCount) =>
        BuildBlackBandSlots(octaveCount, 0);

    public static IReadOnlyList<BlackBandSlot> BuildBlackBandSlots(int octaveCount, int octaveShift1)
    {
        octaveCount = ClampOctaveCount(octaveCount);
        var octaveShift = ClampTransposeOctaveNumber(octaveShift1);
        var naturals = BuildNaturalKeys(octaveCount, octaveShift);
        var slots = new List<BlackBandSlot>(naturals.Count);
        var naturalIndex = 0;

        for (var octave = 0; octave < octaveCount; octave++)
        {
            foreach (var step in NaturalSteps)
            {
                var natural = naturals[naturalIndex];
                var accidental = step.HasSharpAfter
                    ? CreateKey(
                        step.Letter,
                        step.SemitoneOffset + 1,
                        octave,
                        octaveShift,
                        isAccidental: true)
                    : null;

                slots.Add(new BlackBandSlot(natural, accidental));
                naturalIndex++;
            }
        }

        return slots;
    }

    /// <summary>Every note id playable by the <c>play-note</c> action, across the full
    /// <see cref="MaxOctaveCount"/> range regardless of what a given folder view instance currently shows,
    /// ordered by pitch. Used to build the action's parameter catalog and to validate incoming values.</summary>
    public static IReadOnlyList<PianoKey> AllPlayableKeys() =>
        Enumerable
            .Range(
                MinTransposeOctaveNumber,
                MaxTransposeOctaveNumber - MinTransposeOctaveNumber + 1)
            .SelectMany(octaveShift => BuildChromaticKeys(MaxOctaveCount, octaveShift))
            .GroupBy(key => key.NoteId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(key => key.MidiNumber)
            .ToList();

    private static readonly Lazy<IReadOnlyDictionary<string, PianoKey>> ByNoteIdLookup =
        new(() => AllPlayableKeys().ToDictionary(key => key.NoteId, StringComparer.Ordinal));

    public static bool TryGetKey(string noteId, out PianoKey key) =>
        ByNoteIdLookup.Value.TryGetValue(noteId, out key!);

    private static PianoKey CreateKey(
        char letter,
        int semitoneOffset,
        int octaveIndex,
        int octaveShift,
        bool isAccidental)
    {
        var octaveNumber = BaseOctaveNumber + octaveIndex + octaveShift;
        var noteId = isAccidental
            ? $"{letter}s{octaveNumber}"
            : $"{letter}{octaveNumber}";
        var midiNumber = (octaveNumber + 1) * 12 + semitoneOffset;

        return new PianoKey(noteId, ElementKeyFor(noteId), midiNumber, isAccidental);
    }
}
