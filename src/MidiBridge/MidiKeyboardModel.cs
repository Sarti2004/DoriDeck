using DoriDeck.PianoFolder.Piano;

namespace DoriDeck.MidiBridge;

public static class MidiKeyboardModel
{
	public const int MinOctaveCount = 1;
	public const int MaxOctaveCount = 8;
	public const int DefaultOctaveCount = 6;

	public const int MinShift = -3;
	public const int MaxShift = 3;

	private const int LowestOctave = -1;
	private const int HighestOctave = 8;

	private static readonly (string Name, int Offset, bool IsAccidental)[] Steps =
	[
		("c", 0, false), ("cs", 1, true), ("d", 2, false), ("ds", 3, true), ("e", 4, false), ("f", 5, false),
		("fs", 6, true), ("g", 7, false), ("gs", 8, true), ("a", 9, false), ("as", 10, true), ("b", 11, false),
	];

	public static int ClampOctaveCount(int value) => Math.Clamp(value, MinOctaveCount, MaxOctaveCount);

	public static int ClampShift(int value) => Math.Clamp(value, MinShift, MaxShift);

	public static int FirstOctave(int octaveCount, int shift)
	{
		octaveCount = ClampOctaveCount(octaveCount);
		var first = PianoKeyboardModel.BaseOctaveNumber - (octaveCount - 1) / 2 + ClampShift(shift);
		return Math.Clamp(first, LowestOctave, HighestOctave - octaveCount + 1);
	}

	public static IReadOnlyList<PianoOctave> BuildOctaves(int octaveCount, int shift)
	{
		var first = FirstOctave(octaveCount, shift);
		return
		[
			.. Enumerable.Range(first, ClampOctaveCount(octaveCount)).Select(octave => new PianoOctave(
				[
					.. Steps.Select(step =>
					{
						var noteId = $"{step.Name}{octave}";
						return new PianoKey(noteId, PianoKeyboardModel.ElementKeyFor(noteId), (octave + 1) * 12 + step.Offset, step.IsAccidental);
					}),
				])),
		];
	}
}
