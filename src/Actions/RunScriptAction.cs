using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;
using System.IO;

namespace DoriDeck.Actions;

/// Runs a Lua script from the Dorico scripts folder.
internal sealed class RunScriptAction(DoricoSession session) :
	DoriDeckActionBase(session),
	IActionDefinition,
	IDynamicOptionsActionDefinition
{
	private const string ScriptNameParameter = "scriptName";
	private const string ApplyToAllFlowsParameter = "applyToAllFlows";

	public string Id => "run-script";

	public LocalizedText Name => Strings.Actions.RunScript.Name();

	public LocalizedText Description => Strings.Actions.RunScript.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(
			ScriptNameParameter,
			label: Strings.Actions.RunScript.ScriptName.Label(),
			description: Strings.Actions.RunScript.ScriptName.Description(),
			placeholder: Strings.Actions.RunScript.ScriptName.Placeholder(),
			required: true),
		ActionParameter.Toggle(
			ApplyToAllFlowsParameter,
			label: Strings.Actions.RunScript.ApplyToAllFlows.Label(),
			description: Strings.Actions.RunScript.ApplyToAllFlows.Description(),
			defaultValue: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != ScriptNameParameter)
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [],
			});
		}

		cancellationToken.ThrowIfCancellationRequested();

		var scriptPath = Session.ScriptPath;
		if (string.IsNullOrWhiteSpace(scriptPath) || !Directory.Exists(scriptPath))
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [],
				AllowsCustomValue = true,
				CacheSeconds = 5,
			});
		}

		try
		{
			var filter = context.Filter?.Trim();

			var scriptNames = Directory
				.EnumerateFiles(scriptPath, "*.lua", SearchOption.AllDirectories)
				.Select(file =>
				{
					cancellationToken.ThrowIfCancellationRequested();

					var relativePath = Path.GetRelativePath(scriptPath, file);
					return (Path.ChangeExtension(relativePath, null) ?? relativePath)
						.Replace('\\', '/');
				});

			if (!string.IsNullOrWhiteSpace(filter))
			{
				scriptNames = scriptNames.Where(scriptName =>
					scriptName.Contains(filter, StringComparison.OrdinalIgnoreCase));
			}

			var options = scriptNames
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(scriptName => scriptName, StringComparer.OrdinalIgnoreCase)
				.Select(scriptName => new ActionParameterOption
				{
					Label = scriptName,
					Value = scriptName,
				})
				.ToArray();

			return Task.FromResult(new DynamicOptionsResult
			{
				Options = options,
				AllowsCustomValue = true,
				CacheSeconds = 30,
			});
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			// Keep the field usable if the configured folder cannot be read.
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [],
				AllowsCustomValue = true,
				CacheSeconds = 5,
			});
		}
	}

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var scriptName = context.Parameters.TryGetValue(ScriptNameParameter, out var name)
				? name?.ToString()
				: null;

			if (string.IsNullOrWhiteSpace(scriptName))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.RunScript.ScriptName.Label()));
			}

			var applyToAllFlows = context.Parameters.TryGetValue(ApplyToAllFlowsParameter, out var apply) &&
				apply is bool applyBool && applyBool;

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			if (applyToAllFlows && session.FlowsCount > 1)
			{
				for (var flowId = 0; flowId < session.FlowsCount; flowId++)
				{
					await dorico.SendRequestAsync(
						new Command("Edit.GoToFlow", new CommandParameter("FlowID", flowId.ToString(CultureInfo.InvariantCulture))),
						context.CancellationToken);
					await Task.Delay(session.FlowSwitchDelay, context.CancellationToken);

					await RunScriptAsync(dorico, scriptName, context.CancellationToken);
				}
			}
			else
			{
				await RunScriptAsync(dorico, scriptName, context.CancellationToken);
			}

			return ActionResult.Success();
		}

		private Task RunScriptAsync(
			ScoreInterface.IScoreInterfaceRemote dorico,
			string scriptName,
			CancellationToken cancellationToken) =>
			ScriptExecution.RunAsync(dorico, session.ScriptPath, scriptName, cancellationToken);
	}
}
