using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DoriDeck.Services;
using DoriDeck.Syllabifiers;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;
using Serilog;

namespace DoriDeck.Actions;

/// Lyrics Syllabification
internal sealed class InsertLyricsAction(DoricoSession session, IKeyboardService keyboard, ILogger logger)
	: DoriDeckActionBase(session), IActionDefinition
{
	private const int ClipboardRetryCount = 5;
	private static readonly TimeSpan ClipboardRetryDelay = TimeSpan.FromMilliseconds(100);
	private const int OperationDelayMilliseconds = 5;

	private const string LanguageParameter = "language";
	private const string VerseParameter = "verse";
	private const string LyricsModeParameter = "lyricsMode";
	private const string UserDictionaryFolderParameter = "userDictionaryFolder";

	private static readonly Regex VerseToken = new(@"^\(Verse\s+(\d+)\)", RegexOptions.IgnoreCase);

	private readonly ILogger _logger = logger.ForContext<InsertLyricsAction>();

	public string Id => "insert-lyrics";

	public LocalizedText Name => Strings.Actions.InsertLyrics.Name();

	public LocalizedText Description => Strings.Actions.InsertLyrics.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			LyricsModeParameter,
			[
				new ActionParameterOption
				{
					Value = nameof(LyricsMode.SillabifyAndInsert),
					Label = "Syllabify and Insert"
				},
				new ActionParameterOption
				{
					Value = nameof(LyricsMode.SillabifyOnly),
					Label = "Syllabify Only"
				},
				new ActionParameterOption
				{
					Value = nameof(LyricsMode.InsertOnly),
					Label = "Insert Only"
				}
			],
			label: Strings.Actions.InsertLyrics.LyricsMode.Label(),
			description: Strings.Actions.InsertLyrics.LyricsMode.Description(),
			defaultValue: nameof(LyricsMode.SillabifyAndInsert)
		),
		
		ActionParameter.Choice(
			LanguageParameter,
			[
				new ActionParameterOption
				{
					Value = "EN",
					Label = "English/Russian"
				},
				new ActionParameterOption
				{
					Value = "DE",
					Label = "German"
				},
				new ActionParameterOption
				{
					Value = "FI",
					Label = "Finnish"
				},
				new ActionParameterOption
				{
					Value = "Latin",
					Label = "Latin"
				}
			],
			label: Strings.Actions.InsertLyrics.Language.Label(),
			description: Strings.Actions.InsertLyrics.Language.Description(),
			defaultValue: "EN"
		),
		
		ActionParameter.Choice(
			VerseParameter,
			[
				new ActionParameterOption
				{
					Value = "",
					Label = "Auto"
				},
				new ActionParameterOption
				{
					Value = "1",
					Label = "Verse 1"
				},
				new ActionParameterOption
				{
					Value = "2",
					Label = "Verse 2"
				},
				new ActionParameterOption
				{
					Value = "3",
					Label = "Verse 3"
				},
				new ActionParameterOption
				{
					Value = "4",
					Label = "Verse 4"
				},
				new ActionParameterOption
				{
					Value = "Chorus",
					Label = "Chorus"
				}
			],
			label: Strings.Actions.InsertLyrics.Verse.Label(),
			description: Strings.Actions.InsertLyrics.Verse.Description(),
			defaultValue: ""
		),
		ActionParameter.Text(
			UserDictionaryFolderParameter,
			label: Strings.Actions.InsertLyrics.UserDictionaryFolder.Label(),
			description: Strings.Actions.InsertLyrics.UserDictionaryFolder.Description(),
			defaultValue: UserDictionary.DataDirectory
		)
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session, keyboard, _logger);

	private sealed class Executor(DoricoSession session, IKeyboardService keyboard, ILogger logger) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var language = ReadParameter(context, LanguageParameter, "EN");
			var verse = ReadParameter(context, VerseParameter, string.Empty);
			var lyricsMode = Enum.TryParse<LyricsMode>(ReadParameter(context, LyricsModeParameter, string.Empty), ignoreCase: true, out var mode)
				? mode
				: LyricsMode.SillabifyAndInsert;

			var rawText = (await Task.Run(
				() => ClipboardSnapshot.ReadUnicodeText(ClipboardRetryCount, ClipboardRetryDelay), context.CancellationToken))?.Trim();

			if (string.IsNullOrWhiteSpace(rawText))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.InsertLyrics.ClipboardEmpty());
			}

			var verseMatch = VerseToken.Match(rawText);
			if (verseMatch.Success)
			{
				verse = verseMatch.Groups[1].Value;
				rawText = rawText[verseMatch.Length..].TrimStart();
			}
			else if (rawText.StartsWith("(Chorus)", StringComparison.OrdinalIgnoreCase))
			{
				verse = "Chorus";
				rawText = rawText["(Chorus)".Length..];
			}

			var processedText = rawText;

			if (mode is LyricsMode.SillabifyAndInsert or LyricsMode.SillabifyOnly)
			{
				processedText = new LyricProcessor(logger).Process(rawText, language);
			}

			if (mode is LyricsMode.SillabifyOnly)
			{
				await Task.Run(
					() => ClipboardSnapshot.WriteUnicodeText(processedText, ClipboardRetryCount, ClipboardRetryDelay),
					context.CancellationToken);
			}

			if (mode is not (LyricsMode.SillabifyAndInsert or LyricsMode.InsertOnly))
			{
				return ActionResult.Success();
			}

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			try
			{
				EnsureHasSelection(session);
			}
			catch (InvalidOperationException ex)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, ex.Message);
			}

			var parts = SplitIntoLyricParts(processedText);
			if (parts.Length == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.InsertLyrics.NoLyricsFound());
			}

			await dorico.SendRequestAsync(new Command("NoteInput.StartLyricInput"), context.CancellationToken);

			if (int.TryParse(verse, out var lyricLine) && lyricLine > 0)
			{
				const int currentLyricLine = 1;
				var diff = lyricLine - currentLyricLine;
				var direction = diff > 0 ? "kIncrementVerseNumber" : "kDecrementVerseNumber";

				for (var i = 0; i < Math.Abs(diff); i++)
				{
					var entry = await ReadSelectedTextAsync(context.CancellationToken);
					await Task.Delay(20, context.CancellationToken);
					await dorico.SendRequestAsync(
						new Command(
							"NoteInput.AcceptCurrentLyricInput",
							new CommandParameter("LyricText", entry),
							new CommandParameter("LyricsEntryAdvanceType", direction)),
						context.CancellationToken);
					await Task.Delay(20, context.CancellationToken);
				}
			}
			else if (verse.Equals("Chorus", StringComparison.OrdinalIgnoreCase))
			{
				await Task.Delay(20, context.CancellationToken);
				await dorico.SendRequestAsync(
					new Command("NoteInput.AcceptCurrentLyricInput", new CommandParameter("LyricsEntryAdvanceType", "kDecrementVerseNumber")),
					context.CancellationToken);
				await Task.Delay(20, context.CancellationToken);
			}

			for (var visited = 0; visited < parts.Length; visited++)
			{
				await Task.Delay(OperationDelayMilliseconds, context.CancellationToken);

				var lyricText = parts[visited];
				string advanceType;

				if (lyricText == "-" || string.IsNullOrWhiteSpace(lyricText))
				{
					advanceType = GetAdvanceType(lyricText);
					await dorico.SendRequestAsync(
						new Command("NoteInput.AcceptCurrentLyricInput", new CommandParameter("LyricText", string.Empty), new CommandParameter("LyricsEntryAdvanceType", advanceType)),
						context.CancellationToken);
				}
				else
				{
					advanceType = visited + 1 == parts.Length
						? GetAdvanceType(lyricText, isLastPart: true)
						: GetAdvanceType(parts[visited + 1]);

					await dorico.SendRequestAsync(
						new Command(
							"NoteInput.AcceptCurrentLyricInput",
							new CommandParameter("LyricText", lyricText),
							new CommandParameter("LyricsEntryAdvanceType", advanceType)),
						context.CancellationToken);

					visited++;
				}

				if (advanceType != "kEndInput")
				{
					await Task.Delay(OperationDelayMilliseconds, context.CancellationToken);
					await dorico.SendRequestAsync(new Command("NoteInput.AdvanceLyricInput"), context.CancellationToken);
				}
			}

			return ActionResult.Success();
		}

		private static string ReadParameter(ActionExecutionContext context, string name, string fallback) =>
			context.Parameters.TryGetValue(name, out var value) && value?.ToString() is { Length: > 0 } text ? text : fallback;

		private static string[] SplitIntoLyricParts(string text)
		{
			text = text.Normalize(NormalizationForm.FormC).ReplaceLineEndings(" ");
			text = text
				.Replace('\u00A0', ' ')
				.Replace('\u202F', ' ')
				.Replace("+", " ")
				.Replace(@"\", @"\\\\")
				.Replace("&", @"\\&")
				.Replace(",", @"\\,");

			return Regex.Matches(text.Trim(), @"\([^)]*\)|[^\s-]+|-|\s")
				.Select(match => match.Value)
				.ToArray();
		}

		private async Task<string> ReadSelectedTextAsync(CancellationToken cancellationToken)
		{
			for (var attempt = 1; attempt <= 2; attempt++)
			{
				keyboard.PressChord(DoriDeckKey.PrimaryModifier, DoriDeckKey.A);

				var clipboardSequenceBeforeCopy = WindowAndClipboardInterop.GetClipboardSequence();

				keyboard.PressChord(DoriDeckKey.PrimaryModifier, DoriDeckKey.C);

				var copied = await WaitForClipboardChangeAsync(clipboardSequenceBeforeCopy, TimeSpan.FromMilliseconds(200), cancellationToken);
				if (copied)
				{
					var text = await Task.Run(
						() => ClipboardSnapshot.ReadUnicodeText(2, TimeSpan.FromMilliseconds(200)), cancellationToken);
					if (text is not null)
					{
						return text;
					}
				}

				if (attempt < 2)
				{
					logger.Warning("Copy attempt {Attempt} failed while reading the lyric popover; retrying.", attempt);
					await Task.Delay(20, cancellationToken);
				}
			}

			throw new TimeoutException("Can't copy text from clipboard.");
		}

		private static async Task<bool> WaitForClipboardChangeAsync(uint previousSequence, TimeSpan timeout, CancellationToken cancellationToken)
		{
			var started = Stopwatch.StartNew();

			while (started.Elapsed < timeout)
			{
				if (WindowAndClipboardInterop.GetClipboardSequence() != previousSequence)
				{
					return true;
				}

				await Task.Delay(20, cancellationToken);
			}

			return false;
		}

		private static string GetAdvanceType(string text, bool isLastPart = false)
		{
			if (isLastPart)
			{
				return "kEndInput";
			}

			return text == "-" ? "kHyphenateCurrentWord" : "kEndOrExtendCurrentWord";
		}
	}
}

internal enum LyricsMode
{
	SillabifyAndInsert,
	SillabifyOnly,
	InsertOnly,
}
