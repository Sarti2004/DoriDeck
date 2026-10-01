using ScoreInterface;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;


internal static class ScriptExecution
{
	public static Task RunAsync(
		IScoreInterfaceRemote dorico,
		string scriptPath,
		string scriptName,
		CancellationToken cancellationToken)
	{
		var fullPath = Path.Combine(scriptPath.Replace('\\', '/'), scriptName + ".lua").Replace('\\', '/');

		return dorico.SendRequestAsync(
			new Command("Script.RunScript", new CommandParameter("ScriptPath", fullPath)),
			cancellationToken);
	}
}
