using DoriDeck.Services;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Notifications;

namespace DoriDeck.Actions;

/// <summary>
/// Converts the PDFs dropped into a working folder to MusicXML with Audiveris, next to the PDFs. Only
/// registered when Audiveris is installed.
/// </summary>
internal sealed class ConvertPdfToMusicXmlAction(AudiverisConverter converter) : IActionDefinition, IStateProviderActionDefinition
{
	private const string IdleState = "idle";
	private const string WorkingState = "working";

	private const string WorkingFolderParameter = "workingFolder";
	private const string KeepOmrParameter = "keepOmr";
	private const string SeparatePartsParameter = "separateParts";
	private const string OcrLanguagesParameter = "ocrLanguages";

	private const string DefaultOcrLanguages = "eng";

	// Parameter names are the Audiveris processing switches they turn on.
	private static readonly string[] RecognitionSwitches = ["smallHeads", "implicitTuplets", "lyricsAboveStaff", "crossHeads"];

	public string Id => "convert-pdf-to-musicxml";

	public LocalizedText Name => Strings.Actions.ConvertPdfToMusicXml.Name();

	public LocalizedText Description => Strings.Actions.ConvertPdfToMusicXml.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Folder(
			WorkingFolderParameter,
			label: Strings.Actions.ConvertPdfToMusicXml.WorkingFolder.Label(),
			description: Strings.Actions.ConvertPdfToMusicXml.WorkingFolder.Description(),
			required: true),
		ActionParameter.Toggle(
			KeepOmrParameter,
			label: Strings.Actions.ConvertPdfToMusicXml.KeepOmr.Label(),
			description: Strings.Actions.ConvertPdfToMusicXml.KeepOmr.Description(),
			defaultValue: false),
		ActionParameter.Toggle(
			SeparatePartsParameter,
			label: Strings.Actions.ConvertPdfToMusicXml.SeparateParts.Label(),
			description: Strings.Actions.ConvertPdfToMusicXml.SeparateParts.Description(),
			defaultValue: true),
		ActionParameter.Choice(
			OcrLanguagesParameter,
			[
				new ActionParameterOption { Value = DefaultOcrLanguages, Label = Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.English() },
				new ActionParameterOption { Value = "rus", Label = Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.Russian() },
				new ActionParameterOption { Value = "deu", Label = Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.German() },
				new ActionParameterOption { Value = "fra", Label = Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.French() },
			],
			label: Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.Label(),
			description: Strings.Actions.ConvertPdfToMusicXml.OcrLanguages.Description(),
			defaultValue: DefaultOcrLanguages),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	public IActionExecutor CreateExecutor() => new Executor(converter);

	public TimeSpan StatePollInterval => TimeSpan.FromSeconds(1);

	public Task<ActionStateSnapshot?> GetActionStateAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
	{
		var status = converter.Status;

		ActionStateDefinition[] states =
		[
			new(IdleState, Strings.Audiveris.States.Idle()),
			new(WorkingState, Strings.Audiveris.States.Working())
			{
				DefaultAppearance = new ActionStateAppearance { Label = status.Length > 0 ? status : null },
			},
		];

		return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(states, status.Length > 0 ? WorkingState : IdleState));
	}

	private sealed class Executor(AudiverisConverter converter) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => Task.FromResult(Execute(context));

		private ActionResult Execute(ActionExecutionContext context)
		{
			var folder = context.Parameters.TryGetValue(WorkingFolderParameter, out var folderValue)
				? folderValue?.ToString()?.Trim()
				: null;

			if (string.IsNullOrEmpty(folder))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.ConvertPdfToMusicXml.WorkingFolder.Label()));
			}

			if (!Directory.Exists(folder))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.ConvertPdfToMusicXml.FolderNotFound(folder));
			}

			var ocrLanguages = context.Parameters.TryGetValue(OcrLanguagesParameter, out var languagesValue)
				? languagesValue?.ToString()?.Replace(" ", string.Empty, StringComparison.Ordinal)
				: DefaultOcrLanguages;

			// Buttons set up before the toggle existed have no value for it, so missing means its default.
			var separateParts = !context.Parameters.TryGetValue(SeparatePartsParameter, out var separateValue) ||
				separateValue is not bool separate || separate;

			var options = new AudiverisOptions(
				IsOn(context, KeepOmrParameter),
				separateParts,
				[.. RecognitionSwitches.Where(name => IsOn(context, name))],
				string.IsNullOrEmpty(ocrLanguages) ? DefaultOcrLanguages : ocrLanguages);

			IReadOnlyList<string> pdfs;
			try
			{
				pdfs = AudiverisConverter.FindPendingPdfs(folder);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, ex.Message);
			}

			if (pdfs.Count == 0)
			{
				// Not awaited: a slow host shouldn't hold the button's reply, and NotifyAsync swallows its own failures.
				_ = converter.NotifyAsync(
					Strings.Audiveris.Title(),
					Strings.Actions.ConvertPdfToMusicXml.NothingToConvert(),
					NotificationSeverity.Info);
				return ActionResult.Success();
			}

			return converter.TryStart(pdfs, folder, options)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.ConvertPdfToMusicXml.AlreadyRunning());
		}

		private static bool IsOn(ActionExecutionContext context, string name) =>
			context.Parameters.TryGetValue(name, out var value) && value is bool on && on;
	}
}
