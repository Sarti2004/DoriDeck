using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.NoteDurationPad;


public static class NoteDurationPadView
{
    private const double KeyGap = 0.016;
    private const double SectionGap = 0.04;
    private const double LabelSize = 0.05;
    private const double ArrowSize = 0.08;
    private const double GlyphSize = 0.05;

    private const double DurationCellAspect = 0.8;

    /// The controls are one 3 x 4 grid
    private const int ControlColumns = 3;
    private const int ControlRows = 4;

    /// Landscape: durations on the left, controls on the right; height is 1 and the control keys are square.
    private const double LandscapeDurationsWidth = 4 * DurationCellAspect / 2;
    private const double LandscapeControlsWidth = (double)ControlColumns / ControlRows;
    private const double LandscapeAspect = LandscapeDurationsWidth + SectionGap * 2 + LandscapeControlsWidth;

    /// Portrait: durations above the controls; width is 1.
    private const double PortraitDurationsHeight = 2 / (4 * DurationCellAspect);
    private const double PortraitControlsHeight = ControlRows * 0.3;
    private const double PortraitAspect = 1 / (PortraitDurationsHeight + PortraitControlsHeight + SectionGap * 2);

    private static readonly NoteDuration[][] DurationRows =
    [
        [NoteDuration.Breve, NoteDuration.Whole, NoteDuration.Half, NoteDuration.Quarter],
        [NoteDuration.Eighth, NoteDuration.Sixteenth, NoteDuration.ThirtySecond, NoteDuration.SixtyFourth],
    ];

    private static readonly IReadOnlyDictionary<NoteDuration, string> DurationLabels = new Dictionary<NoteDuration, string>
    {
        [NoteDuration.Breve] = "B",
        [NoteDuration.Whole] = "W",
        [NoteDuration.Half] = "H",
        [NoteDuration.Quarter] = "Q",
        [NoteDuration.Eighth] = "8th",
        [NoteDuration.Sixteenth] = "16th",
        [NoteDuration.ThirtySecond] = "32th",
        [NoteDuration.SixtyFourth] = "64th",
    };

    public static UiElement Build(NoteDurationPadViewModel model) =>
        new UiLayer
        {
            Key = "pad",
            Fill = true,
            Children =
            [
                BuildPanel(model),
                new UiResponsive
                {
                    Key = "layout",
                    Fill = true,
                    Default = BuildPortrait(model),
                    Variants = [new UiResponsiveVariant { MinAspect = 1.0, Content = BuildLandscape(model) }],
                },
            ],
        };
    
    private static UiElement BuildPanel(NoteDurationPadViewModel model) =>
        new UiStack
        {
            Key = "panel",
            Background = NoteDurationPadStyle.Panel,
            Children = model.Background is { } background
                ?
                [
                    new UiModifier
                    {
                        Key = "image-style",
                        Radius = UiSize.FromBasis(0),
                        Child = new UiButton
                        {
                            Key = "image",
                            Fill = true,
                            Background = NoteDurationPadStyle.Panel,
                            Source = background,
                            Fit = UiComponentImageFits.Cover,
                        },
                    },
                ]
                : [],
        };

    private static UiElement BuildLandscape(NoteDurationPadViewModel model) =>
        Fitted("landscape", LandscapeAspect, new UiStack
        {
            Key = "row",
            Direction = UiComponentDirections.Horizontal,
            Justify = UiComponentJustify.SpaceBetween,
            Align = UiComponentAlignments.Stretch,
            Children =
            [
                DurationGrid(model, UiSize.FromParent(LandscapeDurationsWidth)),
                ControlsGrid(model, UiSize.FromParent(LandscapeControlsWidth)),
            ],
        });

    private static UiElement BuildPortrait(NoteDurationPadViewModel model) =>
        Fitted("portrait", PortraitAspect, new UiStack
        {
            Key = "column",
            Direction = UiComponentDirections.Vertical,
            Justify = UiComponentJustify.SpaceBetween,
            Align = UiComponentAlignments.Stretch,
            Children =
            [
                DurationGrid(model, UiSize.FromParent(PortraitDurationsHeight)),
                ControlsGrid(model, UiSize.FromParent(PortraitControlsHeight)),
            ],
        });


    private static UiElement Fitted(string key, double aspect, UiElement content) =>
        new UiStack
        {
            Key = key,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Padding = UiSize.FromBasis(0.03),
            Children =
            [
                new UiModifier
                {
                    Key = "fit",
                    Fill = true,
                    Frame = new UiFrame { AspectRatio = aspect },
                    Child = content,
                },
            ],
        };

    private static UiElement DurationGrid(NoteDurationPadViewModel model, UiSize mainSize) =>
        new UiGrid
        {
            Key = "durations",
            MainSize = mainSize,
            Columns = 4,
            Rows = 2,
            Gap = UiSize.FromBasis(KeyGap),
            Children = DurationRows.SelectMany(row => row).Select(duration => DurationButton(model, duration)).ToList(),
        };

    private static UiElement DurationButton(NoteDurationPadViewModel model, NoteDuration duration)
    {
        var button = new UiButton
        {
            Key = "button",
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Stretch,
            Background = NoteDurationPadStyle.Transparent,
            Tint = UiValue.From(() => model.Duration.Value == duration
                ? NoteDurationPadStyle.LedAmber
                : NoteDurationPadStyle.Legend),
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

        var dottedIcon = model.DottedDurationIcons.GetValueOrDefault(duration);

        button = model.DurationIcons.GetValueOrDefault(duration) is { } icon
            ? button with
            {
                Source = UiValue.From(() => model.Duration.Value == duration && model.RhythmDots.Value > 0 && dottedIcon is not null
                    ? dottedIcon
                    : icon),
                Fit = UiComponentImageFits.Contain,
                Zoom = 0.75,
            }
            : button with { Children = [Text(DurationLabels[duration])] };

        return Styled(
            duration.ToString().ToLowerInvariant(),
            duration.ToString(),
            button,
            () => model.Duration.Value == duration);
    }

    private static UiElement ControlsGrid(NoteDurationPadViewModel model, UiSize mainSize) =>
        new UiGrid
        {
            Key = "controls",
            MainSize = mainSize,
            Columns = ControlColumns,
            Rows = ControlRows,
            Gap = UiSize.FromBasis(KeyGap),
            Children =
            [
                CommandButton("delete", "Del", "Delete", model.Delete),
                GlyphButton(
                    "play",
                    "Play",
                    model.Play,
                    Toggled(() => model.InPlayback.Value, () => Icon(UiIcons.Play)),
                    () => model.InPlayback.Value,
                    longPressCommand: model.PlayFromSelected),
                GlyphButton(
                    "record",
                    "Record",
                    model.Record,
                    Toggled(() => model.IsRecording.Value, RecordDot),
                    () => model.IsRecording.Value),
                Blank("top-left"),
                ArrowButton("up", "Up", UiIcons.ArrowUp, model.Up),
                Blank("top-right"),
                ArrowButton("left", "Left", UiIcons.ArrowLeft, model.Left, longPressCommand: model.LeftBar),
                NoteInputButton(model),
                ArrowButton("right", "Right", UiIcons.ArrowRight, model.Right, longPressCommand: model.RightBar),
                Blank("bottom-left"),
                ArrowButton("down", "Down", UiIcons.ArrowDown, model.Down),
                Blank("bottom-right"),
            ],
        };

    private static UiElement CommandButton(string key, string label, string accessibilityLabel, Func<CancellationToken, Task> command) =>
        GlyphButton(key, accessibilityLabel, command, [Text(label)]);

    private static UiElement GlyphButton(
        string key,
        string accessibilityLabel,
        Func<CancellationToken, Task> command,
        IReadOnlyList<UiElement> glyph,
        Func<bool>? lit = null,
        Func<CancellationToken, Task>? longPressCommand = null) =>
        Styled(key, accessibilityLabel, new UiButton
        {
            Key = "button",
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Background = NoteDurationPadStyle.Transparent,
            Events =
            [
                UiEventHandler.OnAsync(UiComponentEvents.Press, command),
                ..(longPressCommand is not null
                    ? [
                        UiEventHandler.OnAsync(UiComponentEvents.LongPress, longPressCommand),
                        //UiEventHandler.OnAsync(UiComponentEvents.DoublePress, longPressCommand)
                      ]
                    : Array.Empty<UiEventHandler>())
            ],
            Children = glyph,
        }, lit);



    private static IReadOnlyList<UiElement> Toggled(Func<bool> active, Func<UiElement> idle) =>
    [
        new UiWhen { Key = "idle-when", Condition = () => !active(), Content = idle },
        new UiWhen { Key = "active-when", Condition = active, Content = StopSquare },
    ];

    private static UiElement Icon(string icon, double size = GlyphSize) =>
        new UiIcon
        {
            Key = "icon",
            Icon = icon,
            Size = UiSize.FromBasis(size),
            Color = NoteDurationPadStyle.Legend,
        };

    private static UiElement StopSquare() =>
        Shape("stop", UiComponentShapes.RoundedRectangle, NoteDurationPadStyle.Legend, GlyphSize * 0.7);

    private static UiElement RecordDot() =>
        Shape("dot", UiComponentShapes.Capsule, NoteDurationPadStyle.LedRed, GlyphSize * 0.75);

    private static UiElement Shape(string key, string shape, string color, double size) =>
        new UiModifier
        {
            Key = key,
            Frame = new UiFrame
            {
                Width = UiLength.OfBasis(size),
                Height = UiLength.OfBasis(size),
            },
            Child = new UiShape
            {
                Key = "shape",
                Shape = shape,
                CornerRadius = UiSize.FromBasis(size * 0.12),
                Color = color,
            },
        };



    private static UiElement NoteInputButton(NoteDurationPadViewModel model)
    {
        var legend = UiValue.From(() => model.NoteInputActive.Value ? NoteDurationPadStyle.LedAmber : NoteDurationPadStyle.Legend);
        var button = new UiButton
        {
            Key = "button",
            Fill = true,
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Background = NoteDurationPadStyle.Transparent,
            Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, model.EnterNoteInput)],
        };

        button = model.NoteInputIcon is { } icon
            ? button with { Source = icon, Fit = UiComponentImageFits.Contain, Zoom = 0.75, Tint = legend }
            : button with
            {
                Children =
                [
                    new UiIcon
                    {
                        Key = "icon",
                        Icon = UiIcons.MusicNote,
                        Size = UiSize.FromBasis(ArrowSize),
                        Color = legend,
                    },
                ],
            };

        return Styled("centre", "Note input", button, () => model.NoteInputActive.Value);
    }

    private static UiElement ArrowButton(string key, string label, string icon, Func<CancellationToken, Task> command, Func<CancellationToken, Task>? longPressCommand = null) =>
        GlyphButton(key, label, command, [Icon(icon, ArrowSize)], longPressCommand: longPressCommand);

    private static UiElement Blank(string key) => new UiStack { Key = key };


    private static UiElement Styled(
        string key,
        string accessibilityLabel,
        UiButton button,
        Func<bool>? lit = null)
    {
        var isLit = lit ?? (() => false);
        return new UiModifier
        {
            Key = $"{key}-style",
            Background = UiBackgroundValue.From(() => isLit() ? NoteDurationPadStyle.FaceLit : NoteDurationPadStyle.Face),
            Radius = UiSize.FromBasis(0.016),
            AccessibilityLabel = UiText.Of(accessibilityLabel),
            Child = button with
            {
                Key = key,
                BorderStyle = UiComponentBorderStyles.Static,
                BorderColor = NoteDurationPadStyle.FaceRim,
            },
        };
    }

    private static UiElement Text(string text) =>
        new UiTextRun
        {
            Key = "label",
            Text = UiText.Of(text),
            Size = UiSize.FromBasis(LabelSize),
            Weight = UiComponentTextWeights.SemiBold,
            Color = NoteDurationPadStyle.Legend,
            Align = UiComponentAlignments.Center,
            MaxLines = 1,
        };
}
