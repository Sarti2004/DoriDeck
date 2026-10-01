using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;

/// <summary>
/// Runs an inline Lua snippet in Dorico, resolving <c>${variable}</c> placeholders first and writing
/// it to a temporary script file since Dorico only runs scripts from disk.
/// </summary>
internal sealed class CustomScriptAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private const string LuaScriptParameter = "luaScript";

	public string Id => "custom-script";

	public LocalizedText Name => Strings.Actions.CustomScript.Name();

	public LocalizedText Description => Strings.Actions.CustomScript.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			LuaScriptParameter,
			label: Strings.Actions.CustomScript.LuaScript.Label(),
			description: Strings.Actions.CustomScript.LuaScript.Description(),
			placeholder: Strings.Actions.CustomScript.LuaScript.Placeholder(),
			required: true),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var luaScript = context.Parameters.TryGetValue(LuaScriptParameter, out var script)
				? script?.ToString()
				: null;

			if (string.IsNullOrWhiteSpace(luaScript))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.CustomScript.LuaScript.Label()));
			}

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var resolvedScript = await session.ResolveVariablesAsync(luaScript);

			var tmpFile = Path.GetTempFileName();
			var tmpPath = Path.ChangeExtension(tmpFile, ".lua");
			File.Delete(tmpFile);

			try
			{
				await File.WriteAllTextAsync(tmpPath, resolvedScript, context.CancellationToken);

				await dorico.SendRequestAsync(
					new Command("Script.RunScript", new CommandParameter("ScriptPath", tmpPath.Replace('\\', '/'))),
					context.CancellationToken);

				return ActionResult.Success();
			}
			finally
			{
				File.Delete(tmpPath);
			}
		}
	}
}
