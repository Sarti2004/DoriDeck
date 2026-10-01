using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface;
using ScoreInterface.Commands;
using ScoreInterface.Enums;

namespace DoriDeck.Actions;

// Base actions class
internal abstract class DoriDeckActionBase(DoricoSession session)
{
	protected DoricoSession Session { get; } = session;

	protected static async Task<IScoreInterfaceRemote?> GetConnectedRemoteAsync(
		DoricoSession session,
		CancellationToken cancellationToken) =>
		await session.EnsureConnectedAsync().WaitAsync(cancellationToken) ? session.Remote : null;

	protected static (string ActionName, List<CommandParameter> Parameters) ParseCommand(string commandName)
	{
		var parts = commandName.Split('?', 2);
		var actionName = parts[0];
		var parameters = new List<CommandParameter>();

		if (parts.Length > 1)
		{
			foreach (var param in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
			{
				var keyValue = param.Split('=', 2);
				if (keyValue.Length == 2)
				{
					parameters.Add(new CommandParameter(keyValue[0], keyValue[1]));
				}
			}
		}

		return (actionName, parameters);
	}

	protected static WindowMode GetTargetMode(string actionName) => actionName switch
	{
		_ when actionName.StartsWith("Setup.", StringComparison.Ordinal) => WindowMode.kSetupMode,
		_ when actionName.StartsWith("Project.", StringComparison.Ordinal) => WindowMode.kSetupMode,
		_ when actionName.StartsWith("NoteInput.", StringComparison.Ordinal) => WindowMode.kWriteMode,
		_ when actionName.StartsWith("EventEdit.", StringComparison.Ordinal) => WindowMode.kWriteMode,
		_ when actionName.StartsWith("Page.", StringComparison.Ordinal) => WindowMode.kEngraveMode,
		_ when actionName.StartsWith("Print.", StringComparison.Ordinal) => WindowMode.kPrintMode,
		_ => WindowMode.Undefined,
	};

	protected static async Task RunWithModeSwitchAsync(
		IScoreInterfaceRemote dorico,
		string commandName,
		CancellationToken cancellationToken)
	{
		var (actionName, parameters) = ParseCommand(commandName.Replace(",", "\\\\,"));

		var originalStatus = await dorico.GetStatusAsync(cancellationToken).WaitAsync(cancellationToken);
		var originalMode = originalStatus?.WindowMode ?? WindowMode.Undefined;
		var targetMode = GetTargetMode(actionName);

		try
		{
			if (targetMode != WindowMode.Undefined && targetMode != originalMode)
			{
				await dorico.SendRequestAsync(
					new Command("Window.SwitchMode", new CommandParameter("WindowMode", targetMode.ToString())),
					cancellationToken);
			}

			await dorico.SendRequestAsync(new Command(actionName, parameters.ToArray()), cancellationToken);
		}
		finally
		{
			if (originalMode != WindowMode.Undefined && targetMode != WindowMode.Undefined && originalMode != targetMode)
			{
				await dorico.SendRequestAsync(
					new Command("Window.SwitchMode", new CommandParameter("WindowMode", originalMode.ToString())),
					cancellationToken);
			}
		}
	}

	protected static void EnsureHasSelection(DoricoSession session)
	{
		if (!session.HasScore)
		{
			throw new InvalidOperationException("Dorico does not have an active score.");
		}

		if (!session.HasSelection)
		{
			throw new InvalidOperationException("Select the first note before running this action.");
		}
	}
}
