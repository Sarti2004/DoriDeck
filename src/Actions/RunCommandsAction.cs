using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface;
using ScoreInterface.Commands;
using ScoreInterface.Enums;

namespace DoriDeck.Actions;

/// Runs a sequence of Dorico commands, not ready yet, multiline needs to be implemented
internal sealed class RunCommandsAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);
	private static readonly char[] LineSeparators = ['\r', '\n'];

	private const string CommandsParameter = "commands";
	private const string ApplyToAllFlowsParameter = "applyToAllFlows";

	public string Id => "run-commands";

	public LocalizedText Name => Strings.Actions.RunCommands.Name();

	public LocalizedText Description => Strings.Actions.RunCommands.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			CommandsParameter,
			label: Strings.Actions.RunCommands.Commands.Label(),
			description: Strings.Actions.RunCommands.Commands.Description(),
			placeholder: Strings.Actions.RunCommands.Commands.Placeholder(),
			required: true),
		ActionParameter.Toggle(
			ApplyToAllFlowsParameter,
			label: Strings.Actions.RunCommands.ApplyToAllFlows.Label(),
			description: Strings.Actions.RunCommands.ApplyToAllFlows.Description(),
			defaultValue: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var commandsText = context.Parameters.TryGetValue(CommandsParameter, out var text)
				? text?.ToString()
				: null;

			if (string.IsNullOrWhiteSpace(commandsText))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.RunCommands.Commands.Label()));
			}

			commandsText = await session.ResolveVariablesAsync(commandsText);

			var commands = commandsText
				.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(command => command.Length > 0)
				.ToArray();

			if (commands.Length == 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.RunCommands.NoValidCommands());
			}

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var applyToAllFlows = context.Parameters.TryGetValue(ApplyToAllFlowsParameter, out var apply) &&
				apply is bool applyBool && applyBool;

			if (applyToAllFlows && session.FlowsCount > 1)
			{
				for (var flowId = 0; flowId < session.FlowsCount; flowId++)
				{
					await dorico.SendRequestAsync(
						new Command("Edit.GoToFlow", new CommandParameter("FlowID", flowId.ToString(CultureInfo.InvariantCulture))),
						context.CancellationToken);
					await Task.Delay(session.FlowSwitchDelay, context.CancellationToken);

					await RunSequenceAsync(dorico, commands, context.CancellationToken);
				}
			}
			else
			{
				await RunSequenceAsync(dorico, commands, context.CancellationToken);
			}

			return ActionResult.Success();
		}

		private async Task RunSequenceAsync(
			IScoreInterfaceRemote dorico,
			IReadOnlyList<string> commands,
			CancellationToken cancellationToken)
		{
			var originalStatus = await dorico.GetStatusAsync(cancellationToken).WaitAsync(cancellationToken);
			var originalMode = originalStatus?.WindowMode ?? WindowMode.Undefined;
			var currentMode = originalMode;

			try
			{
				foreach (var commandName in commands)
				{
					var (actionName, parameters) = ParseCommand(commandName.Replace(",", "\\\\,"));
					var targetMode = GetTargetMode(actionName);

					if (targetMode != WindowMode.Undefined && targetMode != currentMode)
					{
						await dorico.SendRequestAsync(
							new Command("Window.SwitchMode", new CommandParameter("WindowMode", targetMode.ToString())),
							cancellationToken).WaitAsync(CommandTimeout, cancellationToken);

						currentMode = targetMode;
					}

					await dorico.SendRequestAsync(new Command(actionName, parameters.ToArray()), cancellationToken)
						.WaitAsync(CommandTimeout, cancellationToken);

					await Task.Delay(session.TaskWaitDelay, cancellationToken);
				}
			}
			finally
			{
				if (originalMode != WindowMode.Undefined && currentMode != WindowMode.Undefined && originalMode != currentMode)
				{
					await dorico.SendRequestAsync(
						new Command("Window.SwitchMode", new CommandParameter("WindowMode", originalMode.ToString())),
						cancellationToken);
				}
			}
		}
	}
}
