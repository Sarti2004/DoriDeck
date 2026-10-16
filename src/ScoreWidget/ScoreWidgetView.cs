using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.ScoreWidget;


public static class ScoreWidgetView
{
	private const string CardColor = "#17191D";
	private const string TitleColor = "#F2F3F5";
	private const string SecondaryColor = "#9A9EA6";
	private const string ValueColor = "#E6E7EA";

	private const string PaperColor = "#F4F0E6";
	private const string PlaceholderTile = "#25282D";
	private const string PlaceholderBorder = "#3A3D43";
	private const string PlaceholderGlyph = "#6E737B";

	private const string AccentFill = "#18213A";
	private const string AccentBorder = "#2A5BD7";
	private const string AccentCircle = "#2F6BF0";
	private const string NeutralFill = "#1E2024";
	private const string NeutralCircle = "#4A4D53";
	private const string GlyphColor = "#FFFFFF";
	private const string StopGlyphColor = "#EDEDED";

	private const string IndicatorFill = "#25282D";
	private const string IndicatorDot = "#6E737B";
	private const string PlaybackColor = "#2FA35A";
	private const string RecordColor = "#E0453A";
	private const string NoteInputColor = "#E5A93B";

	/// Opacity of an indicator whose state is off.
	private const double IndicatorOffOpacity = 0.45;

	/// It will be Letter/A4 image ratio most of the time.
	private const double PageAspect = 0.75;

	private const double WideImageWidth = 0.58;

	/// Sizes of the vertical layouts, as fractions of the widget's width (its shorter side).
	private sealed record VerticalSizes(
		string Key,
		double ImageHeight,
		double Title,
		int TitleLines,
		double Composer,
		double Duration,
		double Controls,
		double Indicators);

	private static readonly VerticalSizes SquareSizes = new("square", 0.26, 0.08, 1, 0.06, 0.055, 0.16, 0.07);

	// 1x2 and other portrait tiles: twice the height leaves room for a large page and a two-line title.
	private static readonly VerticalSizes TallSizes = new("tall", 0.95, 0.085, 2, 0.065, 0.06, 0.18, 0.08);

	public static UiElement Build(ScoreWidgetViewModel model) =>
		new UiStack
		{
			Key = "score-widget",
			Fill = true,
			Background = CardColor,
			Children =
			[
				new UiWhen
				{
					Key = "score-when",
					Condition = () => model.HasScore.Value,
					Content = () => Responsive(
						"score",
						wide: BuildScoreWide(model),
						tall: BuildScoreVertical(model, TallSizes),
						square: BuildScoreVertical(model, SquareSizes)),
				},
				new UiWhen
				{
					Key = "no-score-when",
					Condition = () => !model.HasScore.Value,
					Content = () => Responsive(
						"no-score",
						wide: BuildNoScoreWide(model),
						tall: BuildNoScoreVertical(model, TallSizes),
						square: BuildNoScoreVertical(model, SquareSizes)),
				},
			],
		};

	/// Wide tiles (2x1) put the page beside the text; square and portrait tiles stack page, text and controls.
	private static UiElement Responsive(string key, UiElement wide, UiElement tall, UiElement square) =>
		new UiResponsive
		{
			Key = key,
			Fill = true,
			Default = square,
			Variants =
			[
				new UiResponsiveVariant { MinAspect = 1.5, Content = wide },
				new UiResponsiveVariant { MaxAspect = 0.75, Content = tall },
			],
		};

	/// 2x1: page on the left; indicators, title, composer, duration and controls on the right.
	private static UiElement BuildScoreWide(ScoreWidgetViewModel model) =>
		WideFrame(
			"wide",
			ScoreImage("wide-image", model, WideImageWidth),
			[
				Indicators("wide-indicators", model, 0.1),
				Heading("wide-title", () => TitleOf(model), 0.13, maxLines: 1),
				Subheading("wide-composer", () => model.Composer.Value, 0.105),
				DurationText("wide-duration", model, 0.09),
				Controls("wide-controls", model, 0.24),
			]);

	/// Square and portrait: indicators and page on top, then title, composer and duration, controls along the bottom.
	private static UiElement BuildScoreVertical(ScoreWidgetViewModel model, VerticalSizes sizes) =>
		VerticalFrame(
			sizes.Key,
			Indicators($"{sizes.Key}-indicators", model, sizes.Indicators),
			ScoreImage($"{sizes.Key}-image", model, sizes.ImageHeight * PageAspect),
			sizes.ImageHeight,
			[
				Heading($"{sizes.Key}-title", () => TitleOf(model), sizes.Title, sizes.TitleLines, UiComponentAlignments.Center),
				Subheading($"{sizes.Key}-composer", () => model.Composer.Value, sizes.Composer, UiComponentAlignments.Center),
				DurationText($"{sizes.Key}-duration", model, sizes.Duration, UiComponentAlignments.Center),
			],
			Controls($"{sizes.Key}-controls", model, sizes.Controls));

	/// No score open, 2x1: placeholder page, "Open …" and a button that opens the most recent project.
	private static UiElement BuildNoScoreWide(ScoreWidgetViewModel model) =>
		WideFrame(
			"empty-wide",
			Placeholder("empty-wide-image", WideImageWidth),
			[
				Heading("empty-wide-title", () => OpenTitleOf(model), 0.12, maxLines: 1),
				Subheading("empty-wide-subtitle", () => "No score open", 0.095),
				new UiStack { Key = "empty-wide-gap", MainSize = UiSize.FromBasis(0.03) },
				new UiStack
				{
					Key = "empty-wide-actions",
					Direction = UiComponentDirections.Horizontal,
					MainSize = UiSize.FromBasis(0.24),
					Children = [OpenButton("open", model, 0.24, width: UiSize.FromBasis(0.66))],
				},
			]);

	private static UiElement BuildNoScoreVertical(ScoreWidgetViewModel model, VerticalSizes sizes)
	{
		var key = $"empty-{sizes.Key}";
		return VerticalFrame(
			key,
			top: null,
			Placeholder($"{key}-image", sizes.ImageHeight * PageAspect),
			sizes.ImageHeight,
			[
				Heading($"{key}-title", () => OpenTitleOf(model), sizes.Title, sizes.TitleLines, UiComponentAlignments.Center),
				Subheading($"{key}-subtitle", () => "No score open", sizes.Composer, UiComponentAlignments.Center),
			],
			new UiStack
			{
				Key = $"{key}-actions",
				Direction = UiComponentDirections.Horizontal,
				MainSize = UiSize.FromBasis(sizes.Controls),
				Children = [OpenButton("open", model, sizes.Controls, width: UiSize.None())],
			});
	}

	private static string TitleOf(ScoreWidgetViewModel model) =>
		model.Title.Value.Length > 0 ? model.Title.Value : "Untitled score";

	private static string OpenTitleOf(ScoreWidgetViewModel model) =>
		model.RecentFile.Value.Length > 0 ? $"Open {model.RecentFile.Value}" : "Dorico";

	private static UiElement WideFrame(string key, UiElement image, IReadOnlyList<UiElement> info) =>
		new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Padding = UiSize.FromBasis(0.08),
			Gap = UiSize.FromBasis(0.08),
			Align = UiComponentAlignments.Center,
			Children =
			[
				image,
				new UiStack
				{
					Key = $"{key}-info",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Stretch,
					Gap = UiSize.FromBasis(0.035),
					Children = info,
				},
			],
		};

	private static UiElement VerticalFrame(
		string key,
		UiElement? top,
		UiElement image,
		double imageHeight,
		IReadOnlyList<UiElement> info,
		UiElement bottom) =>
		new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Padding = UiSize.FromBasis(0.06),
			Gap = UiSize.FromBasis(0.03),
			Align = UiComponentAlignments.Stretch,
			Children =
			[
				.. top is null ? [] : new[] { top },

				// A row, so the page's MainSize is its width and the row's is its height.
				new UiStack
				{
					Key = $"{key}-image-row",
					Direction = UiComponentDirections.Horizontal,
					MainSize = UiSize.FromBasis(imageHeight),
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Children = [image],
				},
				new UiStack
				{
					Key = $"{key}-info",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Stretch,
					Gap = UiSize.FromBasis(0.01),
					Children = info,
				},
				bottom,
			],
		};

	private static UiElement ScoreImage(string key, ScoreWidgetViewModel model, double width) =>
		PageFrame(
			key,
			width,
			background: () => model.Preview.Value is not null ? PaperColor : PlaceholderTile,
			borderColor: () => model.Preview.Value is not null ? PaperColor : PlaceholderBorder,
			[
				new UiWhen
				{
					Key = "preview-when",
					Condition = () => model.Preview.Value is not null,
					Content = () => new UiImage
					{
						Key = "preview",
						Source = UiValue.From(() => model.Preview.Value!),

						// Size is the edge of a square the image is fitted into uncropped, so the frame's height lets a
						// page fill the frame's width too.
						Size = UiSize.FromBasis(width / PageAspect * 0.96),
					},
				},
				new UiWhen
				{
					Key = "glyph-when",
					Condition = () => model.Preview.Value is null,
					Content = () => DocumentGlyph(width),
				},
			]);

	private static UiElement Placeholder(string key, double width) =>
		PageFrame(key, width, () => PlaceholderTile, () => PlaceholderBorder, [DocumentGlyph(width)]);

	private static UiElement PageFrame(
		string key,
		double width,
		Func<string> background,
		Func<string> borderColor,
		IReadOnlyList<UiElement> content) =>
		new UiModifier
		{
			Key = key,
			MainSize = UiSize.FromBasis(width),
			Frame = UiValue.Of(new UiFrame { AspectRatio = PageAspect }),
			Background = UiBackgroundValue.From(() => background()),
			Radius = UiSize.FromBasis(width * 0.07),
			BorderWidth = UiSize.FromBasis(0.006),
			BorderColor = UiValue.From(borderColor),
			Clip = UiComponentClips.Bounds,
			Child = new UiStack
			{
				Key = "content",
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children = content,
			},
		};

	private static UiElement DocumentGlyph(double frameWidth)
	{
		var width = frameWidth * 0.44;
		var line = width * 0.035;

		UiElement Line(string key, double length) => new UiModifier
		{
			Key = key,
			Frame = UiValue.Of(new UiFrame
			{
				Width = UiLength.OfBasis(width * length),
				Height = UiLength.OfBasis(line),
			}),
			Child = new UiShape
			{
				Key = "bar",
				Shape = UiComponentShapes.Capsule,
				Color = PlaceholderGlyph,
			},
		};

		return new UiModifier
		{
			Key = "document",
			MainSize = UiSize.FromBasis(width / PageAspect),
			Frame = UiValue.Of(new UiFrame { AspectRatio = PageAspect }),
			Radius = UiSize.FromBasis(width * 0.07),
			BorderWidth = UiSize.FromBasis(width * 0.045),
			BorderColor = PlaceholderGlyph,
			Child = new UiStack
			{
				Key = "content",
				Direction = UiComponentDirections.Vertical,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = UiSize.FromBasis(width * 0.07),
				Children =
				[
					new UiIcon
					{
						Key = "note",
						Icon = UiIcons.MusicNote,
						Size = UiSize.FromBasis(width * 0.5),
						Color = PlaceholderGlyph,
					},
					Line("line-1", 0.62),
					Line("line-2", 0.62),
					Line("line-3", 0.4),
				],
			},
		};
	}

	private static UiElement Heading(string key, Func<string> text, double size, int maxLines, string? align = null) =>
		new UiTextRun
		{
			Key = key,
			Text = UiText.From(text),
			Size = UiSize.FromBasis(size),
			MinSize = UiSize.FromBasis(size * 0.65),
			MainSize = UiSize.FromBasis(size * 1.3 * maxLines),
			Weight = UiComponentTextWeights.Bold,
			Color = TitleColor,
			Align = align ?? UiComponentAlignments.Start,
			MaxLines = maxLines,
			Wrap = maxLines > 1,
		};

	private static UiElement Subheading(string key, Func<string> text, double size, string? align = null) =>
		new UiTextRun
		{
			Key = key,
			Text = UiText.From(text),
			Size = UiSize.FromBasis(size),
			MinSize = UiSize.FromBasis(size * 0.75),
			MainSize = UiSize.FromBasis(size * 1.3),
			Color = SecondaryColor,
			Align = align ?? UiComponentAlignments.Start,
			MaxLines = 1,
		};

	/// "Duration:" in grey, the value brighter.
	private static UiElement DurationText(string key, ScoreWidgetViewModel model, double size, string? align = null) =>
		new UiTextRun
		{
			Key = key,
			Text = UiText.From(() => model.Duration.Value.Length > 0 ? $"Duration: {model.Duration.Value}" : string.Empty),
			Spans = UiValue.From<IReadOnlyList<UiTextSpan>>(() => model.Duration.Value.Length > 0
				?
				[
					UiTextSpan.FromText("Duration: ", SecondaryColor, UiComponentTextWeights.Regular),
					UiTextSpan.FromText(model.Duration.Value, ValueColor, UiComponentTextWeights.Medium),
				]
				: []),
			Size = UiSize.FromBasis(size),
			MainSize = UiSize.FromBasis(size * 1.3),
			Color = SecondaryColor,
			Align = align ?? UiComponentAlignments.Start,
			MaxLines = 1,
		};

	/// Play, stop and record side by side.
	private static UiElement Controls(string key, ScoreWidgetViewModel model, double height) =>
		new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			MainSize = UiSize.FromBasis(height),
			Gap = UiSize.FromBasis(height * 0.12),
			Align = UiComponentAlignments.Stretch,
			Children =
			[
				TransportButton(
					"play",
					"Play",
					model.Play,
					height,
					AccentFill,
					AccentCircle,
					() => new UiIcon
					{
						Key = "glyph",
						Icon = UiIcons.Play,
						Size = UiSize.FromBasis(height * 0.3),
						Color = GlyphColor,
					}),
				TransportButton(
					"stop",
					"Stop",
					model.Stop,
					height,
					NeutralFill,
					NeutralCircle,
					() => new UiModifier
					{
						Key = "glyph",
						Frame = UiValue.Of(new UiFrame
						{
							Width = UiLength.OfBasis(height * 0.22),
							Height = UiLength.OfBasis(height * 0.22),
						}),
						Child = new UiShape
						{
							Key = "square",
							Shape = UiComponentShapes.RoundedRectangle,
							CornerRadius = UiSize.FromBasis(height * 0.03),
							Color = StopGlyphColor,
						},
					}),
				TransportButton(
					"record",
					"Record",
					model.Record,
					height,
					NeutralFill,
					NeutralCircle,
					() => new UiModifier
					{
						Key = "glyph",
						Frame = UiValue.Of(new UiFrame
						{
							Width = UiLength.OfBasis(height * 0.24),
							Height = UiLength.OfBasis(height * 0.24),
						}),
						Child = new UiShape
						{
							Key = "dot",
							Shape = UiComponentShapes.Capsule,
							Color = RecordColor,
						},
					}),
			],
		};

	/// Status on the top: mode, rhythmic grid, playback, recording, note input.
	private static UiElement Indicators(string key, ScoreWidgetViewModel model, double height) =>
		new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			MainSize = UiSize.FromBasis(height),
			Gap = UiSize.FromBasis(height * 0.3),
			Align = UiComponentAlignments.Stretch,
			Children =
			[
				new UiWhen
				{
					Key = "mode-when",
					Condition = () => model.ModeLetter.Value.Length > 0,
					Content = () => Indicator(
						"mode",
						"Dorico mode",
						IndicatorText(() => model.ModeLetter.Value, height),
						height,
						() => AccentFill,
						dotColor: null,
						isOn: () => true),
				},
				new UiWhen
				{
					Key = "grid-when",
					Condition = () => model.GridResolution.Value.Length > 0,
					Content = () => Indicator(
						"grid",
						"Rhythmic grid",
						GridGlyph(model, height),
						height,
						() => IndicatorFill,
						dotColor: null,
						isOn: () => true),
				},
				Indicator(
					"playback",
					"Playback",
					IndicatorText(() => "PLAY", height),
					height,
					() => IndicatorFill,
					() => model.InPlayback.Value ? PlaybackColor : IndicatorDot,
					() => model.InPlayback.Value),
				Indicator(
					"recording",
					"Recording",
					IndicatorText(() => "REC", height),
					height,
					() => IndicatorFill,
					() => model.IsRecording.Value ? RecordColor : IndicatorDot,
					() => model.IsRecording.Value),
				Indicator(
					"note-input",
					"Note input",
					IndicatorText(() => "INPUT", height),
					height,
					() => IndicatorFill,
					() => model.NoteInputActive.Value ? NoteInputColor : IndicatorDot,
					() => model.NoteInputActive.Value),
			],
		};

	/// Resolution grid icon
	private static UiElement GridGlyph(ScoreWidgetViewModel model, double height) =>
		new UiStack
		{
			Key = "grid-glyph",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiWhen
				{
					Key = "icon-when",
					Condition = () => model.GridIcon.Value is not null,
					Content = () => new UiImage
					{
						Key = "icon",
						Source = UiValue.From(() => model.GridIcon.Value!),
						Size = UiSize.FromBasis(height * 0.75),
					},
				},
				new UiWhen
				{
					Key = "text-when",
					Condition = () => model.GridIcon.Value is null,
					Content = () => IndicatorText(() => model.GridResolution.Value, height),
				},
			],
		};

	private static UiElement IndicatorText(Func<string> text, double height) =>
		new UiTextRun
		{
			Key = "text",
			Text = UiText.From(text),
			Size = UiSize.FromBasis(height * 0.5),
			Weight = UiComponentTextWeights.SemiBold,
			Color = GlyphColor,
			MaxLines = 1,
		};

	/// A pill with an optional status dot; dimmed while <paramref name="isOn"/> is false.
	private static UiElement Indicator(
		string key,
		string label,
		UiElement content,
		double height,
		Func<string> background,
		Func<string>? dotColor,
		Func<bool> isOn)
	{
		var dotSize = height * 0.36;

		// Zero-width ends: the stack's gap around them pads the pill's sides.
		UiElement Edge(string edgeKey) => new UiStack { Key = edgeKey, MainSize = UiSize.FromBasis(0) };

		return new UiModifier
		{
			Key = key,
			Background = UiBackgroundValue.From(() => background()),
			Radius = UiSize.FromBasis(height / 2),
			Opacity = UiValue.From(() => isOn() ? 1.0 : IndicatorOffOpacity),
			AccessibilityLabel = UiText.Of(label),
			Child = new UiStack
			{
				Key = "content",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = UiSize.FromBasis(height * 0.25),
				Children =
				[
					Edge("start"),
					.. dotColor is null
						? []
						: new UiElement[]
						{
							new UiModifier
							{
								Key = "dot",
								MainSize = UiSize.FromBasis(dotSize),
								Frame = UiValue.Of(new UiFrame { AspectRatio = 1 }),
								Background = UiBackgroundValue.From(() => dotColor()),
								Radius = UiSize.FromBasis(dotSize / 2),
								Child = new UiStack { Key = "fill" },
							},
						},
					content,
					Edge("end"),
				],
			},
		};
	}

	private static UiElement TransportButton(
		string key,
		string label,
		Func<CancellationToken, Task> onPress,
		double height,
		string fill,
		string circle,
		Func<UiElement> glyph)
	{
		var circleSize = height * 0.62;

		UiElement Circle(string circleKey) => new UiModifier
		{
			Key = circleKey,
			MainSize = UiSize.FromBasis(circleSize),
			Frame = UiValue.Of(new UiFrame { AspectRatio = 1 }),
			Background = circle,
			Radius = UiSize.FromBasis(circleSize / 2),
			Child = new UiStack
			{
				Key = "content",
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children = [glyph()],
			},
		};

		return new UiModifier
		{
			Key = $"{key}-style",
			Radius = UiSize.FromBasis(height * 0.2),
			AccessibilityLabel = UiText.Of(label),
			Child = new UiButton
			{
				Key = key,
				Fill = true,
				Background = fill,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, onPress)],
				Children =
				[
					new UiFirstFit
					{
						Key = "fit",
						Fill = true,
						Children =
						[
							new UiStack
							{
								Key = "labelled",
								Direction = UiComponentDirections.Horizontal,
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Gap = UiSize.FromBasis(height * 0.25),
								Children =
								[
									Circle("circle"),
									new UiTextRun
									{
										Key = "label",
										Text = UiText.Of(label),
										Size = UiSize.FromBasis(height * 0.34),
										Weight = UiComponentTextWeights.SemiBold,
										Color = GlyphColor,
										MaxLines = 1,
									},
								],
							},
							new UiStack
							{
								Key = "compact",
								Direction = UiComponentDirections.Horizontal,
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Children = [Circle("circle")],
							},
						],
					},
				],
			},
		};
	}

	/// "Open Score" with a folder icon.
	private static UiElement OpenButton(string key, ScoreWidgetViewModel model, double height, UiSize width) =>
		new UiWhen
		{
			Key = $"{key}-when",
			Condition = () => model.RecentFile.Value.Length > 0,
			Content = () => new UiModifier
			{
				Key = $"{key}-style",
				Radius = UiSize.FromBasis(height * 0.2),
				BorderWidth = UiSize.FromBasis(0.006),
				BorderColor = AccentBorder,
				AccessibilityLabel = UiText.From(() => model.IsOpening.Value ? "Opening score" : "Open Score"),
				Child = new UiButton
				{
					Key = key,
					MainSize = width,
					Fill = !width.IsDeclared,
					Direction = UiComponentDirections.Horizontal,
					Background = AccentFill,
					BorderStyle = UiValue.From(() => model.IsOpening.Value
						? UiComponentBorderStyles.Comet
						: UiComponentBorderStyles.Static),
					BorderColor = AccentCircle,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = UiSize.FromBasis(height * 0.28),
					Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, model.OpenScoreAsync)],
					Children =
					[
						new UiIcon
						{
							Key = "icon",
							Icon = UiValue.From(() => model.IsOpening.Value ? UiIcons.Refresh : UiIcons.Folder),
							Size = UiSize.FromBasis(height * 0.36),
							Color = GlyphColor,
						},
						new UiTextRun
						{
							Key = "label",
							Text = UiText.From(() => model.IsOpening.Value ? "Opening…" : "Open Score"),
							Size = UiSize.FromBasis(height * 0.32),
							Weight = UiComponentTextWeights.SemiBold,
							Color = GlyphColor,
							MaxLines = 1,
						},
					],
				},
			},
		};
}
