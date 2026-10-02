using System.Globalization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.PianoFolder.Piano;

/// <summary>
/// Builds the piano keyboard folder view tree from a <see cref="PianoKeyboardViewModel"/>.
/// The same generated key model renders one through four octaves; two octaves remain the default.
/// </summary>
/// <remarks>
/// The keyboard deliberately uses two non-overlapping playable bands. Macro Deck UI has no
/// pointer-events-style click-through property, so this keeps every key independently pressable.
///
/// The visual treatment is applied with <see cref="UiModifier"/> members that decorate the existing
/// button node: gradient backgrounds, a very small radius and an inside border. That avoids the generic
/// rounded-tile appearance without introducing an extra interactive layer.
///
/// Accidentals use a seven-column <see cref="UiGrid"/> inside the natural's upper slot. Three columns
/// remain the natural-key continuation and four columns form the accidental, making the black key 4/7
/// (about 57%) of a natural key's width. This is substantially closer to piano proportions than the
/// previous two-thirds split and needs only one button per accidental.
/// </remarks>
public static class PianoKeyboardView
{
    private const double BlackKeyBandHeightFraction = 0.42;
    private const double ControlsHeightFraction = 0.11;

    private const double RootPadding = 0.012;
    private const double RootGap = 0.010;
    private const double KeyboardFramePadding = 0.010;

    private const int AccidentalGridColumns = 7;
    private const int NaturalContinuationColumns = 3;
    private const int AccidentalColumns = 4;

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
                BuildControlsStrip(model),
                BuildKeyboardFrame(model),
            ],
        };

    /// <summary>
    /// Adds a compact dark chassis around the keybed. Background, radius and border are modifier
    /// properties, so they decorate the stack itself; padding stays on the stack and therefore does not
    /// create a negotiated ui.modifier wrapper.
    /// </summary>
    private static UiElement BuildKeyboardFrame(PianoKeyboardViewModel model) =>
        new UiModifier
        {
            Key = "keyboard-frame-style",
            Background = PianoKeyStyle.KeyboardChassis,
            Radius = UiSize.FromBasis(0.014),
            BorderWidth = UiSize.FromBasis(0.003),
            BorderColor = PianoKeyStyle.KeyboardChassisBorder,
            Child = new UiStack
            {
                Key = "keyboard-frame",
                Direction = UiComponentDirections.Vertical,
                Fill = true,
                Padding = UiSize.FromBasis(KeyboardFramePadding),
                Children =
                [
                    BuildBlackKeyBand(model),
                    BuildWhiteKeyBand(model),
                ],
            },
        };

    private static UiElement BuildControlsStrip(PianoKeyboardViewModel model) =>
        new UiGrid
        {
            Key = "controls",
            Columns = 3,
            Rows = 1,
            MainSize = UiSize.FromBasis(ControlsHeightFraction),
            Children =
            [
                new UiStack
                {
                    Key = "controls-left",
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Stretch,
                    Gap = UiSize.FromBasis(0.003),
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "controls-left-label",
                            Text = UiText.Of("OCTAVES"),
                            Size = UiSize.FromBasis(0.027),
                            Color = PianoKeyStyle.ControlLabelText,
                            Align = UiComponentAlignments.Start,
                        },
                        new UiStack
                        {
                            Key = "controls-left-row",
                            Direction = UiComponentDirections.Horizontal,
                            Justify = UiComponentJustify.Start,
                            Align = UiComponentAlignments.Center,
                            Children =
                            [
                                BuildOctaveTabs(model),
                            ],
                        },
                    ],
                },
                new UiStack
                {
                    Key = "controls-center",
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Stretch,
                    Gap = UiSize.FromBasis(0.003),
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "controls-center-label",
                            Text = UiText.Of("SHIFT"),
                            Size = UiSize.FromBasis(0.027),
                            Color = PianoKeyStyle.ControlLabelText,
                            Align = UiComponentAlignments.Center,
                        },
                        new UiStack
                        {
                            Key = "controls-center-row",
                            Direction = UiComponentDirections.Horizontal,
                            Justify = UiComponentJustify.Center,
                            Align = UiComponentAlignments.Center,
                            Children =
                            [
                                BuildOctaveShiftControl(model),
                            ],
                        },
                    ],
                },
                new UiStack
                {
                    Key = "controls-right",
                    Direction = UiComponentDirections.Vertical,
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Stretch,
                    Gap = UiSize.FromBasis(0.003),
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "controls-right-label",
                            Text = UiText.Of("TRANSPOSE"),
                            Size = UiSize.FromBasis(0.027),
                            Color = PianoKeyStyle.ControlLabelText,
                            Align = UiComponentAlignments.End,
                        },
                        new UiStack
                        {
                            Key = "controls-right-row",
                            Direction = UiComponentDirections.Horizontal,
                            Justify = UiComponentJustify.End,
                            Align = UiComponentAlignments.Center,
                            Children =
                            [
                                BuildTransposeControl(model),
                            ],
                        },
                    ],
                },
            ],
        };

    private static UiElement BuildOctaveTabs(PianoKeyboardViewModel model)
    {
        var tabs = new List<UiElement>(PianoKeyboardModel.MaxOctaveCount);
        for (var octaves = PianoKeyboardModel.MinOctaveCount; octaves <= PianoKeyboardModel.MaxOctaveCount; octaves++)
        {
            tabs.Add(BuildOctaveTab(model, octaves));
        }

        return new UiStack
        {
            Key = "octave-tabs",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Gap = UiSize.FromBasis(0.006),
            Children = tabs,
        };
    }

    private static UiElement BuildOctaveTab(PianoKeyboardViewModel model, int octaves) =>
        new UiModifier
        {
            Key = $"octave-tab-{octaves}-style",
            Radius = UiSize.FromBasis(0.008),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiButton
            {
                Key = $"octave-tab-{octaves}",
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Center,
                MainSize = UiSize.FromBasis(0.075),
                Background = UiValue.From(() => model.Octaves.Value == octaves
                    ? PianoKeyStyle.ControlSelected
                    : PianoKeyStyle.ControlIdle),
                Events =
                [
                    UiEventHandler.On(UiComponentEvents.Press, () =>
                        model.Octaves.Value = PianoKeyboardModel.ClampOctaveCount(octaves))
                ],
                Children =
                [
                    new UiTextRun
                    {
                        Key = $"octave-tab-{octaves}-label",
                        Text = UiText.Of(octaves.ToString(CultureInfo.CurrentCulture)),
                        Size = UiSize.FromBasis(0.050),
                        Color = PianoKeyStyle.ControlText,
                    },
                ],
            },
        };

    /// <summary>
    /// Coarse pitch-range control. Each button selects an absolute octave offset through
    /// <see cref="PianoKeyboardModel.TransposeOctaveNumber"/>. The value is reactive, so changing it
    /// regenerates the visible note model at the shifted octave while the fine semitone transpose remains
    /// independent and is still applied only at dispatch time.
    /// </summary>
    private static UiElement BuildOctaveShiftControl(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "octave-shift",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Gap = UiSize.FromBasis(0.004),
            Children =
            [
                BuildOctaveShiftButton(model, "octave-shift-minus-2", "-2", -2),
                BuildOctaveShiftButton(model, "octave-shift-minus-1", "-1", -1),
                BuildOctaveShiftButton(model, "octave-shift-zero", "0", 0),
                BuildOctaveShiftButton(model, "octave-shift-plus-1", "+1", 1),
                BuildOctaveShiftButton(model, "octave-shift-plus-2", "+2", 2),
            ],
        };

    private static UiElement BuildOctaveShiftButton(
        PianoKeyboardViewModel model,
        string key,
        string label,
        int octaveDelta) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Radius = UiSize.FromBasis(0.007),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiButton
            {
                Key = key,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Center,
                MainSize = UiSize.FromBasis(0.052),
                Background = UiValue.From(() =>
                    model.TransposeOctaveNumber.Value == octaveDelta
                        ? PianoKeyStyle.ControlSelected
                        : PianoKeyStyle.ControlIdle),
                Events =
                [
                    UiEventHandler.On(
                        UiComponentEvents.Press,
                        () => model.TransposeOctaveNumber.Value =
                            PianoKeyboardModel.ClampTransposeOctaveNumber(octaveDelta))
                ],
                Children =
                [
                    new UiTextRun
                    {
                        Key = $"{key}-label",
                        Text = UiText.Of(label),
                        Size = UiSize.FromBasis(0.041),
                        Color = PianoKeyStyle.ControlText,
                    },
                ],
            },
        };

    private static UiElement BuildTransposeControl(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "transpose",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Gap = UiSize.FromBasis(0.006),
            Children =
            [
                BuildTransposeStepButton(model, "transpose-down", "-", delta: -1),
                new UiTextRun
                {
                    Key = "transpose-value",
                    Text = UiText.From(() => model.Transpose.Value.ToString(CultureInfo.CurrentCulture)),
                    Size = UiSize.FromBasis(0.050),
                    Color = PianoKeyStyle.ControlText,
                    Align = UiComponentAlignments.Center,
                    MainSize = UiSize.FromBasis(0.070),
                },
                BuildTransposeStepButton(model, "transpose-up", "+", delta: 1),
            ],
        };

    private static UiElement BuildTransposeStepButton(
        PianoKeyboardViewModel model,
        string key,
        string label,
        int delta) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Background = PianoKeyStyle.ControlIdle,
            Radius = UiSize.FromBasis(0.008),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiButton
            {
                Key = key,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Center,
                MainSize = UiSize.FromBasis(0.064),
                Events =
                [
                    UiEventHandler.On(UiComponentEvents.Press, () =>
                        model.Transpose.Value =
                            PianoKeyboardModel.ClampTranspose(model.Transpose.Value + delta))
                ],
                Children =
                [
                    new UiTextRun
                    {
                        Key = $"{key}-label",
                        Text = UiText.Of(label),
                        Size = UiSize.FromBasis(0.050),
                        Color = PianoKeyStyle.ControlText,
                    },
                ],
            },
        };

    /// <summary>
    /// Upper keyboard band. Every slot has the same width as one natural key. Slots without an
    /// accidental are a visual continuation of the natural below. Slots with an accidental use a
    /// seven-column grid: 3/7 natural continuation and 4/7 accidental.
    /// </summary>
    private static UiElement BuildBlackKeyBand(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "black-keys",
            Direction = UiComponentDirections.Horizontal,
            MainSize = UiSize.FromBasis(BlackKeyBandHeightFraction),
            Children =
            [
                new UiRepeat<BlackBandSlot>
                {
                    Key = "black-keys-repeat",
                    Items = UiValue.From(() =>
                        PianoKeyboardModel.BuildBlackBandSlots(model.Octaves.Value, model.TransposeOctaveNumber.Value)),
                    KeySelector = slot => slot.Natural.ElementKey,
                    Template = (slot, _) => slot.Accidental is { } accidental
                        ? BuildNaturalWithAccidentalColumn(model, slot.Natural, accidental)
                        : BuildNaturalContinuationFiller(
                            model,
                            slot.Natural,
                            $"{slot.Natural.ElementKey}-continuation",
                            fill: true),
                },
            ],
        };

    /// <summary>
    /// Lower keyboard band. Naturals now touch directly; their inside borders create the seam, so the
    /// keyboard reads as a continuous keybed rather than a row of separated buttons.
    /// </summary>
    private static UiElement BuildWhiteKeyBand(PianoKeyboardViewModel model) =>
        new UiStack
        {
            Key = "white-keys",
            Direction = UiComponentDirections.Horizontal,
            Fill = true,
            Children =
            [
                new UiRepeat<PianoKey>
                {
                    Key = "white-keys-repeat",
                    Items = UiValue.From(() =>
                        PianoKeyboardModel.BuildNaturalKeys(model.Octaves.Value, model.TransposeOctaveNumber.Value)),
                    KeySelector = key => key.ElementKey,
                    Template = (key, _) => BuildKeyButton(model, key),
                },
            ],
        };

    /// <summary>
    /// A natural's upper slot when it has a trailing accidental. The grid is one natural-key slot wide.
    /// Its first three columns continue the natural; its last four columns contain one real accidental
    /// button. There is no duplicate companion button.
    /// </summary>
    private static UiElement BuildNaturalWithAccidentalColumn(
        PianoKeyboardViewModel model,
        PianoKey natural,
        PianoKey accidental) =>
        new UiGrid
        {
            Key = $"{natural.ElementKey}-column",
            Columns = AccidentalGridColumns,
            Rows = 1,
            Fill = true,
            Children =
            [
                BuildNaturalContinuationFiller(
                    model,
                    natural,
                    $"{natural.ElementKey}-continuation",
                    columnSpan: NaturalContinuationColumns),
                BuildKeyButtonWithElementKey(
                    model,
                    accidental,
                    accidental.ElementKey,
                    columnSpan: AccidentalColumns),
            ],
        };

    private static UiElement BuildNaturalContinuationFiller(
        PianoKeyboardViewModel model,
        PianoKey natural,
        string elementKey,
        bool fill = false,
        int? columnSpan = null)
    {
        UiButton button = columnSpan is { } span
            ? new UiButton
            {
                Key = elementKey,
                Fill = fill,
                ColumnSpan = span,
                Children =
                [
                    BuildWhiteKeyPressedFace(model, natural, elementKey),
                ],
            }
            : new UiButton
            {
                Key = elementKey,
                Fill = fill,
                Children =
                [
                    BuildWhiteKeyPressedFace(model, natural, elementKey),
                ],
            };

        return DecorateKey(button, natural.IsAccidental);
    }

    private static UiElement BuildKeyButton(PianoKeyboardViewModel model, PianoKey key) =>
        BuildKeyButtonWithElementKey(model, key, key.ElementKey);

    private static UiElement BuildKeyButtonWithElementKey(
        PianoKeyboardViewModel model,
        PianoKey key,
        string elementKey,
        int? columnSpan = null)
    {
        UiButton button = columnSpan is { } span
            ? new UiButton
            {
                Key = elementKey,
                Fill = false,
                ColumnSpan = span,
                Events =
                [
                    UiEventHandler.OnAsync(
                        UiComponentEvents.Press,
                        ct => OnKeyPressedAsync(model, key, ct))
                ],
            }
            : new UiButton
            {
                Key = elementKey,
                Fill = true,
                Events =
                [
                    UiEventHandler.OnAsync(
                        UiComponentEvents.Press,
                        ct => OnKeyPressedAsync(model, key, ct))
                ],
                Children = key.IsAccidental
                    ? []
                    :
                    [
                        BuildWhiteKeyPressedFace(model, key, elementKey),
                    ],
            };

        return DecorateKey(button, key.IsAccidental);
    }

    /// <summary>
    /// Adds a stronger pressed face to white keys. The reader's built-in press tint is white, so it is
    /// naturally subtle against ivory. This uses the existing note-level pressed state that is already
    /// toggled by the async "press" handler; it does not add press-start or press-end tracking.
    /// </summary>
    private static UiElement BuildWhiteKeyPressedFace(
        PianoKeyboardViewModel model,
        PianoKey key,
        string elementKey) =>
        new UiWhen
        {
            Key = $"{elementKey}-pressed-when",
            Condition = () => model.PressedByNoteId[key.NoteId].Value,
            Content = () =>
                new UiModifier
                {
                    Key = $"{elementKey}-pressed-style",
                    Background = PianoKeyStyle.NaturalPressed,
                    Radius = UiSize.FromBasis(0.004),
                    Child = new UiStack
                    {
                        Key = $"{elementKey}-pressed-face",
                        Fill = true,
                    },
                },
        };

    /// <summary>
    /// Applies the piano material directly to the button node. With only modifier members set,
    /// UiModifier does not add a layout wrapper; Macro Deck places the gradient, radius and border in
    /// the button's own modifiers object.
    /// </summary>
    private static UiElement DecorateKey(
        UiButton button,
        bool accidental) =>
        new UiModifier
        {
            Key = $"{button.Key}-style",
            Background = accidental
                ? PianoKeyStyle.AccidentalIdle
                : PianoKeyStyle.NaturalIdle,
            Radius = UiSize.FromBasis(accidental ? 0.008 : 0.004),
            BorderWidth = UiSize.FromBasis(accidental ? 0.0025 : 0.002),
            BorderColor = accidental
                ? PianoKeyStyle.AccidentalBorder
                : PianoKeyStyle.NaturalBorder,
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
