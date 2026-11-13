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
internal sealed class ConvertPdfToMusicXmlAction(AudiverisConverter converter) : IActionDefinition
{
	private const string WorkingFolderParameter = "workingFolder";
	private const string KeepOmrParameter = "keepOmr";

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
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	public IActionExecutor CreateExecutor() => new Executor(converter);

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

			var options = new AudiverisOptions(
				IsOn(context, KeepOmrParameter),
				[.. RecognitionSwitches.Where(name => IsOn(context, name))]);

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
