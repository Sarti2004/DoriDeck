using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.Actions;

/// <summary>
/// Respells the selected note(s) to their enharmonic equivalent (e.g. C# to Db).
/// </summary>
internal sealed class RespellNoteAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private const string RespellCommand =
		"NoteInput.TransposeOrAddNotesToSelection?Definition=c#=db,db=c#,d#=eb,eb=d#,e#=f,f=e#," +
		"f#=gb,gb=f#,g#=ab,ab=g#,a#=bb,bb=a#,b#=c,c=b#,fb=e,e=fb,cb=b,b=cb";

	public string Id => "respell-note";

	public LocalizedText Name => Strings.Actions.RespellNote.Name();

	public LocalizedText Description => Strings.Actions.RespellNote.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			await RunWithModeSwitchAsync(dorico, RespellCommand, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
