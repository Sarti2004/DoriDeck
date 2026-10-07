using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.PianoFolder.Piano;

internal static class PianoControlsView
{
    public const double PanelHeight = 0.12;

    private const double ControlHeight = 0.072;
    private const double SegmentWidth = 0.066;
    private const double LabelSize = 0.03;
    private const double ValueSize = 0.034;

    public static UiElement Build(PianoKeyboardViewModel model) =>
        Panel(
            "controls",
                [
                    .. BuildBack(model),
                    Label("octaves-label", "Octaves", 0.11),
                    SegmentGroup(
                        "octave-tabs",
                        Enumerable
                            .Range(PianoKeyboardModel.MinOctaveCount, PianoKeyboardModel.MaxOctaveCount)
                            .Select(octaves => Segment(
                                $"octave-tab-{octaves}",
                                octaves.ToString(),
                                () => model.Octaves.Value == octaves,
                                () => model.Octaves.Value = PianoKeyboardModel.ClampOctaveCount(octaves)))
                            .ToList()),
                    Divider("controls-divider-1"),
                    Label("shift-label", "Shift", 0.07),
                    SegmentGroup(
                        "octave-shift",
                        Enumerable
                            .Range(
                                PianoKeyboardModel.MinTransposeOctaveNumber,
                                PianoKeyboardModel.MaxTransposeOctaveNumber - PianoKeyboardModel.MinTransposeOctaveNumber + 1)
                            .Select(shift => Segment(
                                $"octave-shift-{ShiftKey(shift)}",
                                shift > 0 ? $"+{shift}" : shift.ToString(),
                                () => model.TransposeOctaveNumber.Value == shift,
                                () => model.TransposeOctaveNumber.Value =
                                    PianoKeyboardModel.ClampTransposeOctaveNumber(shift)))
                            .ToList()),
                    Divider("controls-divider-2"),
                    BuildDisplay(model),
                    Divider("controls-divider-3"),
                    Label("transpose-label", "Transpose", 0.15),
                    BuildTransposeStepper(model),
                    .. BuildNoteInputToggle(model),
                ]);

    private static UiElement Panel(string key, IReadOnlyList<UiElement> children) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Background = PianoKeyStyle.PanelBackground,
            Radius = UiSize.FromBasis(0.014),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.PanelBorder,
            Child = new UiStack
            {
                Key = key,
                Direction = UiComponentDirections.Horizontal,
                MainSize = UiSize.FromBasis(PanelHeight),
                Align = UiComponentAlignments.Center,
                Padding = UiSize.FromBasis(0.022),
                Gap = UiSize.FromBasis(0.022),
                Children = children,
            },
        };

    private static IEnumerable<UiElement> BuildBack(PianoKeyboardViewModel model)
    {
        if (model.GoBack is not { } goBack)
        {
            yield break;
        }

        yield return new UiModifier
        {
            Key = "back-group",
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(ControlHeight),
                Height = UiLength.OfBasis(ControlHeight),
            },
            Background = PianoKeyStyle.ControlIdle,
            Radius = UiSize.FromBasis(0.012),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiStack
            {
                Key = "back-cell",
                Padding = UiSize.FromBasis(0.004),
                Children =
                [
                    new UiModifier
                    {
                        Key = "back-style",
                        Radius = UiSize.FromBasis(0.009),
                        AccessibilityLabel = UiText.Of("Back"),
                        Child = new UiButton
                        {
                            Key = "back",
                            Fill = true,
                            Justify = UiComponentJustify.Center,
                            Align = UiComponentAlignments.Center,
                            Background = PianoKeyStyle.ControlButton,
                            Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, ct => goBack(ct))],
                            Children = [Chevron("back-icon", flip: true)],
                        },
                    },
                ],
            },
        };

        yield return Divider("controls-divider-back");
    }

    /// shows or hides the duration input row
    private static IEnumerable<UiElement> BuildNoteInputToggle(PianoKeyboardViewModel model)
    {
        var toggle = new UiButton
        {
            Key = "note-input-toggle",
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Background = UiValue.From(() => model.ShowNoteInput.Value
                ? PianoKeyStyle.ControlSelected
                : PianoKeyStyle.ControlButton),
            Events =
            [
                UiEventHandler.On(UiComponentEvents.Press, () =>
                    model.ShowNoteInput.Value = !model.ShowNoteInput.Value)
            ],
        };

        toggle = model.Artwork?.DurationIcons.GetValueOrDefault(NoteDuration.Eighth) is { } eighth
            ? toggle with { Source = eighth, Fit = UiComponentImageFits.Contain, Zoom = 0.7 }
            : toggle with
            {
                Children =
                [
                    new UiIcon
                    {
                        Key = "note-input-toggle-icon",
                        Icon = UiIcons.MusicNote,
                        Size = UiSize.FromBasis(0.036),
                        Color = PianoKeyStyle.ControlText,
                    },
                ],
            };

        yield return Divider("controls-divider-note-input");
        yield return new UiModifier
        {
            Key = "note-input-toggle-group",
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(ControlHeight),
                Height = UiLength.OfBasis(ControlHeight),
            },
            Background = PianoKeyStyle.ControlIdle,
            Radius = UiSize.FromBasis(0.012),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiStack
            {
                Key = "note-input-toggle-cell",
                Padding = UiSize.FromBasis(0.004),
                Children =
                [
                    new UiModifier
                    {
                        Key = "note-input-toggle-style",
                        Radius = UiSize.FromBasis(0.009),
                        AccessibilityLabel = UiText.Of("Durations"),
                        Child = toggle,
                    },
                ],
            },
        };
    }

    /// The optional second row: the note duration selector 
    public static UiElement BuildNoteInputRow(PianoKeyboardViewModel model) =>
        Panel(
            "note-input",
            [
                Label("duration-label", "Duration", 0.12),
                SegmentGroup(
                    "durations",
                    Enum.GetValues<NoteDuration>().Select(duration => DurationSegment(model, duration)).ToList(),
                    segmentWidth: 0.08),
                new UiStack { Key = "note-input-spacer", Fill = true },
                SegmentGroup(
                    "note-input-steps",
                    [
                        CommandSegment("note-return", "‹  Left", model.Dispatcher.DispatchReturnAsync),
                        CommandSegment("note-forward", "Right  ›", model.Dispatcher.DispatchForwardAsync),
                    ],
                    segmentWidth: 0.17),
            ]);

    private static readonly IReadOnlyDictionary<NoteDuration, string> DurationLabels = new Dictionary<NoteDuration, string>
    {
        [NoteDuration.Whole] = "W",
        [NoteDuration.Half] = "H",
        [NoteDuration.Quarter] = "Q",
        [NoteDuration.Eighth] = "8th",
        [NoteDuration.Sixteenth] = "16th",
        [NoteDuration.ThirtySecond] = "32th",
    };

    private static UiElement DurationSegment(PianoKeyboardViewModel model, NoteDuration duration)
    {
        var key = $"duration-{duration.ToString().ToLowerInvariant()}";
        var button = new UiButton
        {
            Key = key,
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Stretch,
            Background = UiValue.From(() => model.Duration.Value == duration
                ? PianoKeyStyle.ControlSelected
                : PianoKeyStyle.ControlIdle),
            Events =
            [
                UiEventHandler.OnAsync(UiComponentEvents.Press, ct =>
                {
                    model.Duration.Value = duration;
                    return model.Dispatcher.DispatchDurationAsync(duration, ct);
                }),
                UiEventHandler.OnAsync(UiComponentEvents.LongPress, async ct =>
                {
                    model.Duration.Value = duration;
                    model.RhythmDots.Value = model.RhythmDots.Value > 0 ? 0 : 1;
                    await model.Dispatcher.DispatchDurationAsync(duration, ct).ConfigureAwait(false);
                    await model.Dispatcher.DispatchRhythmDotsAsync(model.RhythmDots.Value, ct).ConfigureAwait(false);
                })
            ],
        };

        button = model.Artwork?.DurationIcons.GetValueOrDefault(duration) is { } icon
            ? button with { Source = icon, Fit = UiComponentImageFits.Contain, Zoom = 0.7 }
            : button with { Children = [SegmentText($"{key}-label", DurationLabels[duration])] };

        return new UiModifier
        {
            Key = $"{key}-style",
            Radius = UiSize.FromBasis(0.009),
            AccessibilityLabel = UiText.Of(duration.ToString()),
            Child = button,
        };
    }

    private static UiElement CommandSegment(string key, string label, Func<CancellationToken, Task> command) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Radius = UiSize.FromBasis(0.009),
            Child = new UiButton
            {
                Key = key,
                Fill = true,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Stretch,
                Background = PianoKeyStyle.ControlButton,
                Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, command)],
                Children = [SegmentText($"{key}-label", label)],
            },
        };

    private static UiElement SegmentText(string key, string text) =>
        new UiTextRun
        {
            Key = key,
            Text = UiText.Of(text),
            Size = UiSize.FromBasis(ValueSize),
            Color = PianoKeyStyle.ControlText,
            Align = UiComponentAlignments.Center,
            MaxLines = 1,
        };

    private static string ShiftKey(int shift) => shift switch
    {
        < 0 => $"minus-{-shift}",
        0 => "zero",
        _ => $"plus-{shift}",
    };

    private static UiElement Label(string key, string text, double width) =>
        new UiTextRun
        {
            Key = key,
            Text = UiText.Of(text),
            Size = UiSize.FromBasis(LabelSize),
            Color = PianoKeyStyle.ControlLabelText,
            MainSize = UiSize.FromBasis(width),
            MaxLines = 1,
        };

    private static UiElement Divider(string key) =>
        new UiModifier
        {
            Key = key,
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(0.0025),
                Height = UiLength.OfBasis(ControlHeight * 0.8),
            },
            Background = PianoKeyStyle.PanelDivider,
            Child = new UiStack { Key = $"{key}-line" },
        };

    private static UiElement SegmentGroup(string key, IReadOnlyList<UiElement> segments, double segmentWidth = SegmentWidth) =>
        new UiModifier
        {
            Key = key,
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(segments.Count * segmentWidth + 0.008),
                Height = UiLength.OfBasis(ControlHeight),
            },
            Background = PianoKeyStyle.ControlIdle,
            Radius = UiSize.FromBasis(0.012),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiStack
            {
                Key = $"{key}-segments",
                Direction = UiComponentDirections.Horizontal,
                Padding = UiSize.FromBasis(0.004),
                Children = segments,
            },
        };

    private static UiElement Segment(string key, string label, Func<bool> selected, Action press) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Radius = UiSize.FromBasis(0.009),
            Child = new UiButton
            {
                Key = key,
                Fill = true,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Stretch,
                Background = UiValue.From(() => selected() ? PianoKeyStyle.ControlSelected : PianoKeyStyle.ControlIdle),
                Events = [UiEventHandler.On(UiComponentEvents.Press, press)],
                Children =
                [
                    new UiTextRun
                    {
                        Key = $"{key}-label",
                        Text = UiText.Of(label),
                        Size = UiSize.FromBasis(ValueSize),
                        Color = PianoKeyStyle.ControlText,
                        Align = UiComponentAlignments.Center,
                    },
                ],
            },
        };

    private static UiElement BuildDisplay(PianoKeyboardViewModel model) =>
        new UiModifier
        {
            Key = "display",
            Fill = true,
            Frame = new UiFrame { Height = UiLength.OfBasis(ControlHeight) },
            Background = PianoKeyStyle.DisplayBackground,
            Radius = UiSize.FromBasis(0.006),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.DisplayBorder,
            Child = new UiStack
            {
                Key = "display-content",
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Stretch,
                Children =
                [
                    new UiTextRun
                    {
                        Key = "display-range",
                        Text = UiText.From(() => RangeText(model)),
                        Size = UiSize.FromBasis(ValueSize),
                        Color = PianoKeyStyle.DisplayText,
                        Align = UiComponentAlignments.Center,
                        MaxLines = 1,
                    },
                ],
            },
        };

    private static string RangeText(PianoKeyboardViewModel model)
    {
        var naturals = PianoKeyboardModel.BuildNaturalKeys(model.Octaves.Value, model.TransposeOctaveNumber.Value);
        return $"{naturals[0].NoteId.ToUpperInvariant()} – {naturals[^1].NoteId.ToUpperInvariant()}";
    }

    private static UiElement BuildTransposeStepper(PianoKeyboardViewModel model) =>
        new UiModifier
        {
            Key = "transpose",
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(3 * SegmentWidth + 0.008),
                Height = UiLength.OfBasis(ControlHeight),
            },
            Background = PianoKeyStyle.ControlIdle,
            Radius = UiSize.FromBasis(0.012),
            BorderWidth = UiSize.FromBasis(0.002),
            BorderColor = PianoKeyStyle.ControlBorder,
            Child = new UiStack
            {
                Key = "transpose-segments",
                Direction = UiComponentDirections.Horizontal,
                Align = UiComponentAlignments.Stretch,
                Padding = UiSize.FromBasis(0.004),
                Children =
                [
                    StepButton(model, "transpose-down", flip: true, delta: -1),
                    new UiStack
                    {
                        Key = "transpose-value-cell",
                        Fill = true,
                        Justify = UiComponentJustify.Center,
                        Align = UiComponentAlignments.Stretch,
                        Children =
                        [
                            new UiTextRun
                            {
                                Key = "transpose-value",
                                Text = UiText.From(() => model.Transpose.Value > 0
                                    ? $"+{model.Transpose.Value}"
                                    : model.Transpose.Value.ToString()),
                                Size = UiSize.FromBasis(ValueSize),
                                Color = PianoKeyStyle.ControlText,
                                Align = UiComponentAlignments.Center,
                            },
                        ],
                    },
                    StepButton(model, "transpose-up", flip: false, delta: 1),
                ],
            },
        };

    private static UiElement StepButton(PianoKeyboardViewModel model, string key, bool flip, int delta) =>
        new UiModifier
        {
            Key = $"{key}-style",
            Radius = UiSize.FromBasis(0.009),
            Child = new UiButton
            {
                Key = key,
                Fill = true,
                Justify = UiComponentJustify.Center,
                Align = UiComponentAlignments.Center,
                Background = PianoKeyStyle.ControlButton,
                Events =
                [
                    UiEventHandler.On(UiComponentEvents.Press, () =>
                        model.Transpose.Value = PianoKeyboardModel.ClampTranspose(model.Transpose.Value + delta))
                ],
                Children = [Chevron($"{key}-icon", flip)],
            },
        };

    ///  The icon set has only a right pointing chevron, so the left one is the same turned half a turn.
    private static UiElement Chevron(string key, bool flip)
    {
        var icon = new UiIcon
        {
            Key = key,
            Icon = UiIcons.ChevronRight,
            Size = UiSize.FromBasis(0.036),
            Color = PianoKeyStyle.ControlText,
        };

        return flip
            ? new UiTransform { Key = $"{key}-flip", Rotation = 180, MainSize = UiSize.FromBasis(0.036), Children = [icon] }
            : icon;
    }
}
