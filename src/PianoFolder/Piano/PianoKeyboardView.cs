using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.PianoFolder.Piano;

public static class PianoKeyboardView
{
    private const double RootPadding = 0.012;
    private const double RootGap = 0.010;

    public const double NaturalKeyAspect = 0.193;

    private const int NaturalsPerOctave = 7;
    private const int ColumnsPerNatural = 9;
    private const int OctaveColumns = NaturalsPerOctave * ColumnsPerNatural;
    private const int OctaveRows = 16;

    /// <summary>Accidentals reach 10/16 of a natural's length.</summary>
    private const int AccidentalRows = 10;

    private static readonly int[] UpperBandColumns = [6, 5, 5, 5, 6, 6, 5, 4, 5, 5, 5, 6];

    public static UiElement Build(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "piano-root",
            Direction = UiComponentDirections.Vertical,
            Background = PianoKeyStyle.InstrumentFrame,
            Padding = UiSize.FromBasis(RootPadding),
            Gap = UiSize.FromBasis(RootGap),
            Children =
            [
                PianoControlsView.Build(model),
                new UiWhen
                {
                    Key = "note-input-when",
                    Condition = () => model.ShowNoteInput.Value,
                    Content = () => PianoControlsView.BuildNoteInputRow(model),
                },
                BuildKeybed(model),
            ],
        };

    private static UiElement BuildKeybed(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "keybed-area",
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Children = [BuildKeybedFrame(model)],
        };

    private static UiElement BuildKeybedFrame(PianoKeyboardViewModel model) =>
        new UiModifier
        {
            Key = "keybed",
            Fill = true,
            Frame = UiValue.From(() => new UiFrame
            {
                AspectRatio = model.Octaves.Value * NaturalsPerOctave * NaturalKeyAspect,
            }),
            Clip = UiComponentClips.Bounds,
            Background = PianoKeyStyle.KeyboardChassis,
            Radius = UiSize.FromBasis(0.006),
            BorderWidth = UiSize.FromBasis(0.003),
            BorderColor = PianoKeyStyle.KeyboardChassisBorder,
            Child = new UiStack
            {
                Key = "octaves",
                Direction = UiComponentDirections.Horizontal,
                Children =
                [
                    new UiRepeat<PianoOctave>
                    {
                        Key = "octaves-repeat",
                        Items = UiValue.From(() =>
                            PianoKeyboardModel.BuildOctaves(model.Octaves.Value, model.TransposeOctaveNumber.Value)),
                        KeySelector = octave => octave.ElementKey,
                        Template = (octave, _) => BuildOctave(model, octave),
                    },
                ],
            },
        };

    private static UiElement BuildOctave(PianoKeyboardViewModel model, PianoOctave octave)
    {
        var naturals = octave.Keys.Where(key => !key.IsAccidental).ToList();
        var lowerRows = OctaveRows - AccidentalRows;

        return new UiLayer
        {
            Key = octave.ElementKey,
            Fill = true,
            Children =
            [
                LayerPlane($"{octave.ElementKey}-natural-faces", new UiGrid
                {
                    Key = "grid",
                    Fill = true,
                    Columns = NaturalsPerOctave,
                    Rows = 1,
                    Children = naturals.Select(key => BuildFace(model, key, columnSpan: 1, rowSpan: 1)).ToList(),
                }),
                LayerPlane($"{octave.ElementKey}-accidental-faces", new UiGrid
                {
                    Key = "grid",
                    Fill = true,
                    Columns = OctaveColumns,
                    Rows = OctaveRows,
                    Children = octave.Keys
                        .Select((key, index) => key.IsAccidental
                            ? BuildFace(model, key, UpperBandColumns[index], AccidentalRows)
                            : new UiStack
                            {
                                Key = $"{key.ElementKey}-gap",
                                ColumnSpan = UpperBandColumns[index],
                                RowSpan = AccidentalRows,
                            })
                        .ToList(),
                }),
                LayerPlane($"{octave.ElementKey}-tiles", new UiModifier
                {
                    Key = "invisible",
                    Fill = true,
                    Opacity = 0,
                    Child = new UiGrid
                    {
                        Key = "grid",
                        Columns = OctaveColumns,
                        Rows = OctaveRows,
                        Children =
                        [
                            .. octave.Keys.Select((key, index) => BuildTile(
                                model,
                                key,
                                key.IsAccidental ? key.ElementKey : $"{key.ElementKey}-upper",
                                UpperBandColumns[index],
                                AccidentalRows)),
                            .. naturals.Select(key => BuildTile(
                                model,
                                key,
                                key.ElementKey,
                                ColumnsPerNatural,
                                lowerRows)),
                        ],
                    },
                }),
            ],
        };
    }

    private static UiElement LayerPlane(string key, UiElement content) =>
        new UiStack
        {
            Key = key,
            Children = [content],
        };

    private static UiElement BuildFace(PianoKeyboardViewModel model, PianoKey key, int columnSpan, int rowSpan)
    {
        var pressed = model.PressedByNoteId[key.NoteId];
        var face = new UiButton
        {
            Key = $"{key.ElementKey}-face",
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            Background = PianoKeyStyle.InstrumentFrame,
        };

        if (model.Artwork is { } artwork)
        {
            var (idle, down) = key.IsAccidental
                ? (artwork.AccidentalIdle, artwork.AccidentalPressed)
                : (artwork.NaturalIdle, artwork.NaturalPressed);

            return SquareCorners(face with
            {
                Source = UiValue.From(() => pressed.Value ? down : idle),
                Fit = UiComponentImageFits.Cover,
            });
        }

        var (idleColor, pressedColor, border) = key.IsAccidental
            ? (PianoKeyStyle.AccidentalIdle, PianoKeyStyle.AccidentalPressed, PianoKeyStyle.AccidentalBorder)
            : (PianoKeyStyle.NaturalIdle, PianoKeyStyle.NaturalPressed, PianoKeyStyle.NaturalBorder);

        return new UiModifier
        {
            Key = $"{face.Key}-style",
            Radius = UiSize.FromBasis(0.004),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = border,
            Child = face with { Background = UiValue.From(() => pressed.Value ? pressedColor : idleColor) },
        };
    }

    private static UiElement BuildTile(
        PianoKeyboardViewModel model,
        PianoKey key,
        string elementKey,
        int columnSpan,
        int rowSpan) =>
        new UiButton
        {
            Key = elementKey,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            Events =
            [
                UiEventHandler.OnAsync(
                    UiComponentEvents.Press,
                    ct => OnKeyPressedAsync(model, key, ct))
            ],
        };

    private static UiElement SquareCorners(UiButton button) =>
        new UiModifier
        {
            Key = $"{button.Key}-style",
            Radius = UiSize.FromBasis(0),
            Child = button,
        };

    private static async Task OnKeyPressedAsync(
        PianoKeyboardViewModel model,
        PianoKey key,
        CancellationToken cancellationToken)
    {
        var pressed = model.PressedByNoteId[key.NoteId];
        pressed.Value = true;
        try
        {
            await model.Dispatcher
                .DispatchAsync(key.NoteId, model.Transpose.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            pressed.Value = false;
        }
    }
}
