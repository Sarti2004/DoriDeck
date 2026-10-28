using DoriDeck.PianoFolder.Piano;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.MidiBridge;

internal static class MidiKeyboardView
{
	private const string Transparent = "#00000000";
	private const string SelectedOverlay = "#FFB54766";
	private const string NaturalLegend = "#26272B";
	private const string AccidentalLegend = "#ECE9E2";
	private const string AssignedMarker = "#5B8DEF";
	private const string OctaveLegend = "#262627";

	private const double IconSize = 0.6;
	private const double MarkerSize = 0.3;
	private const double OctaveLabelSize = 0.55;
	private const double OctaveLabelPadding = 0.08;

	private const double KeyDivider = 0.012;

	public static UiElement Build(MidiBridgeEditorViewModel model) =>
		new UiConfigStack
		{
			Key = "kb",
			Direction = UiComponentDirections.Horizontal,
			Gap = UiStackGaps.None,
			Wrap = true,
			Children =
			[
				new UiRepeat<PianoOctave>
				{
					Key = "r",
					Items = UiValue.From(() => MidiKeyboardModel.BuildOctaves(model.Octaves.Value, model.Shift.Value)),
					KeySelector = octave => octave.Keys[0].NoteId,
					Template = (octave, _) => new UiModifier
					{
						Key = octave.Keys[0].NoteId,
						Fill = true,
						Frame = new UiFrame { AspectRatio = PianoKeyboardView.NaturalsPerOctave * PianoKeyboardView.NaturalKeyAspect },
						Clip = UiComponentClips.Bounds,
						Background = PianoKeyStyle.KeyboardChassis,
						Child = BuildOctave(model, octave),
					},
				},
			],
		};

	private static UiElement BuildOctave(MidiBridgeEditorViewModel model, PianoOctave octave)
	{
		var naturals = octave.Keys.Where(key => !key.IsAccidental).ToList();
		var lowerRows = PianoKeyboardView.OctaveRows - PianoKeyboardView.AccidentalRows;
		var upperColumns = PianoKeyboardView.UpperBandColumns;

		return new UiLayer
		{
			Key = "k",
			Children =
			[
				Plane("n", new UiGrid
				{
					Key = "g",
					Fill = true,
					Columns = PianoKeyboardView.NaturalsPerOctave,
					Gap = UiSize.FromParent(KeyDivider),
					Padding = UiSize.FromParent(KeyDivider / 2),
					Rows = 1,
					Children = naturals.Select(key => Face(model, key, columnSpan: 1, rowSpan: 1)).ToList(),
				}),
				Plane("a", new UiGrid
				{
					Key = "g",
					Fill = true,
					Columns = PianoKeyboardView.OctaveColumns,
					Rows = PianoKeyboardView.OctaveRows,
					Children = octave.Keys
						.Select((key, index) => key.IsAccidental
							? Face(model, key, upperColumns[index], PianoKeyboardView.AccidentalRows)
							: new UiStack
							{
								Key = key.NoteId,
								ColumnSpan = upperColumns[index],
								RowSpan = PianoKeyboardView.AccidentalRows,
							})
						.ToList(),
				}),
				Plane("t", new UiGrid
				{
					Key = "g",
					Fill = true,
					Columns = PianoKeyboardView.OctaveColumns,
					Rows = PianoKeyboardView.OctaveRows,
					Children =
					[
						.. octave.Keys.Select((key, index) => Tile(
							model,
							key,
							key.IsAccidental ? key.NoteId : key.NoteId + "u",
							upperColumns[index],
							PianoKeyboardView.AccidentalRows,
							showGlyph: key.IsAccidental)),
						.. naturals.Select(key => Tile(
							model,
							key,
							key.NoteId,
							PianoKeyboardView.ColumnsPerNatural,
							lowerRows,
							showGlyph: true)),
					],
				}),
			],
		};
	}

	private static UiElement Plane(string key, UiElement content) =>
		new UiStack
		{
			Key = key,
			Children = [content],
		};

	private static UiElement Face(MidiBridgeEditorViewModel model, PianoKey key, int columnSpan, int rowSpan)
	{
		var note = key.MidiNumber;
		var (idle, selected) = key.IsAccidental
			? (PianoKeyStyle.AccidentalIdle, PianoKeyStyle.AccidentalPressed)
			: (PianoKeyStyle.NaturalIdle, PianoKeyStyle.NaturalPressed);

		var face = new UiStack
		{
			Key = key.NoteId,
			ColumnSpan = columnSpan,
			RowSpan = rowSpan,
			Background = UiValue.From(() => model.Selected.Value == note ? selected : idle),
		};

		// Only C keys carry an octave label (C4 = MIDI 60); drawn on the face so it stays under the press overlay.
		return !key.IsAccidental && note % 12 == 0
			? face with
			{
				Justify = UiComponentJustify.End,
				Align = UiComponentAlignments.Center,
				Padding = UiSize.FromParent(OctaveLabelPadding),
				Children =
				[
					new UiTextRun
					{
						Key = "o",
						Text = UiText.Of($"C{note / 12 - 1}"),
						Size = UiSize.FromParent(OctaveLabelSize),
						Color = OctaveLegend,
						Align = UiComponentAlignments.Center,
						MaxLines = 1,
					},
				],
			}
			: face;
	}

	private static UiElement Tile(
		MidiBridgeEditorViewModel model,
		PianoKey key,
		string elementKey,
		int columnSpan,
		int rowSpan,
		bool showGlyph)
	{
		var note = key.MidiNumber;
		var tile = new UiButton
		{
			Key = elementKey,
			ColumnSpan = columnSpan,
			RowSpan = rowSpan,
			Background = UiValue.From(() => model.Selected.Value == note ? SelectedOverlay : Transparent),
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => model.Select(note))],
		};

		return showGlyph
			? tile with
			{
				Justify = key.IsAccidental ? UiComponentJustify.End : UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children = Glyph(model, note, key.IsAccidental ? AccidentalLegend : NaturalLegend),
			}
			: tile;
	}

	private static IReadOnlyList<UiElement> Glyph(MidiBridgeEditorViewModel model, int note, string legend) =>
	[
		new UiWhen
		{
			Key = "i",
			Condition = () => model.Bindings.Value.GetValueOrDefault(note)?.Icon is not null,
			Content = () => new UiIcon
			{
				Key = "i",
				Icon = UiValue.From(() => model.Bindings.Value.GetValueOrDefault(note)?.Icon ?? UiIcons.MusicNote),
				Size = UiSize.FromParent(IconSize),
				Color = legend,
			},
		},
		new UiWhen
		{
			Key = "m",
			Condition = () => model.Bindings.Value.TryGetValue(note, out var binding) && binding.Icon is null,
			Content = () => new UiModifier
			{
				Key = "m",
				Frame = new UiFrame
				{
					Width = UiLength.OfParent(MarkerSize),
					Height = UiLength.OfParent(MarkerSize),
				},
				Child = new UiShape
				{
					Key = "s",
					Shape = UiComponentShapes.Capsule,
					Color = AssignedMarker,
				},
			},
		},
	];
}
