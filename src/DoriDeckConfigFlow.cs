using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace DoriDeck;

public static class DoriDeckConfigKeys
{

	public const string ConfigEntryTitle = "DoriDeck";

	public const string ScriptPath = "ScriptPath";
	public const string DoricoVersion = "DoricoVersion";
	public const string AutoLoadScripts = "AutoLoadScripts";
	public const string FlowSwitchDelay = "FlowSwitchDelay";
	public const string TaskWaitDelay = "TaskWaitDelay";
}

/// <summary>
/// <c>IConfigFlowContext</c> carries none of the entry's stored values, so the form is prefilled from
/// <paramref name="session"/>, which holds what was last read from config. Without it every reopen
/// would show the field defaults and look as if nothing had been saved.
/// </summary>
internal sealed class DoriDeckConfigFlow(DoricoSession session) : IConfigFlow
{
	private const string SettingsStepId = "settings";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(BuildStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> values,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var scriptPath = values.TryGetValue(DoriDeckConfigKeys.ScriptPath, out var scriptPathValue)
			? scriptPathValue?.ToString()?.Trim()
			: null;

		var doricoVersion = values.TryGetValue(DoriDeckConfigKeys.DoricoVersion, out var doricoVersionValue)
			? doricoVersionValue?.ToString()?.Trim()
			: null;
		if (string.IsNullOrEmpty(doricoVersion))
		{
			doricoVersion = DoricoSession.DefaultDoricoVersion;
		}

		var autoLoadScripts = values.TryGetValue(DoriDeckConfigKeys.AutoLoadScripts, out var autoLoadValue) &&
			bool.TryParse(autoLoadValue?.ToString(), out var autoLoadBool) && autoLoadBool;

		if (!TryReadNonNegativeInt(values, DoriDeckConfigKeys.FlowSwitchDelay, DoricoSession.DefaultFlowSwitchDelay, out var flowSwitchDelay))
		{
			return Task.FromResult(ConfigFlowResult.Error(
				BuildStep(),
				DoriDeckConfigKeys.FlowSwitchDelay,
				FieldError(DoriDeckConfigKeys.FlowSwitchDelay, Strings.ConfigFlow.DelayMustNotBeNegative())));
		}

		if (!TryReadNonNegativeInt(values, DoriDeckConfigKeys.TaskWaitDelay, DoricoSession.DefaultTaskWaitDelay, out var taskWaitDelay))
		{
			return Task.FromResult(ConfigFlowResult.Error(
				BuildStep(),
				DoriDeckConfigKeys.TaskWaitDelay,
				FieldError(DoriDeckConfigKeys.TaskWaitDelay, Strings.ConfigFlow.DelayMustNotBeNegative())));
		}

		var entries = new Dictionary<string, ConfigFlowValue>
		{
			[DoriDeckConfigKeys.ScriptPath] = ConfigFlowValue.Plain(string.IsNullOrEmpty(scriptPath) ? DefaultScriptPath(doricoVersion) : scriptPath),
			[DoriDeckConfigKeys.DoricoVersion] = ConfigFlowValue.Plain(doricoVersion),
			[DoriDeckConfigKeys.AutoLoadScripts] = ConfigFlowValue.Plain(autoLoadScripts ? "true" : "false"),
			[DoriDeckConfigKeys.FlowSwitchDelay] = ConfigFlowValue.Plain(flowSwitchDelay.ToString(CultureInfo.InvariantCulture)),
			[DoriDeckConfigKeys.TaskWaitDelay] = ConfigFlowValue.Plain(taskWaitDelay.ToString(CultureInfo.InvariantCulture)),
		};

		return Task.FromResult(ConfigFlowResult.Complete(DoriDeckConfigKeys.ConfigEntryTitle, entries));
	}

	private static IReadOnlyDictionary<string, LocalizedText> FieldError(string field, LocalizedText message) =>
		new Dictionary<string, LocalizedText> { [field] = message };

	private static bool TryReadNonNegativeInt(
		IReadOnlyDictionary<string, object?> values,
		string key,
		int defaultValue,
		out int result)
	{
		result = defaultValue;

		if (!values.TryGetValue(key, out var raw) || raw is null)
		{
			return true;
		}

		if (!int.TryParse(raw.ToString(), out var parsed) || parsed < 0)
		{
			return false;
		}

		result = parsed;
		return true;
	}

	private ConfigFlowStep BuildStep() => new()
	{
		StepId = SettingsStepId,
		Title = Strings.ConfigFlow.StepTitle(),
		Description = Strings.ConfigFlow.StepDescription(),
		Fields =
		[
			ActionParameter.Text(
				DoriDeckConfigKeys.DoricoVersion,
				label: Strings.ConfigFlow.DoricoVersionLabel(),
				description: Strings.ConfigFlow.DoricoVersionDescription(),
				placeholder: DoricoSession.DefaultDoricoVersion,
				defaultValue: session.DoricoVersion,
				required: false),
			ActionParameter.Folder(
				DoriDeckConfigKeys.ScriptPath,
				label: Strings.ConfigFlow.ScriptPathLabel(),
				description: Strings.ConfigFlow.ScriptPathDescription(),
				//defaultValue: DefaultScriptPath,
				required: false),
			ActionParameter.Toggle(
				DoriDeckConfigKeys.AutoLoadScripts,
				label: Strings.ConfigFlow.AutoLoadScriptsLabel(),
				description: Strings.ConfigFlow.AutoLoadScriptsDescription(),
				defaultValue: session.AutoLoadScripts),
			ActionParameter.Number(
				DoriDeckConfigKeys.FlowSwitchDelay,
				label: Strings.ConfigFlow.FlowSwitchDelayLabel(),
				description: Strings.ConfigFlow.FlowSwitchDelayDescription(),
				required: false,
				defaultValue: session.FlowSwitchDelay),
			ActionParameter.Number(
				DoriDeckConfigKeys.TaskWaitDelay,
				label: Strings.ConfigFlow.TaskWaitDelayLabel(),
				description: Strings.ConfigFlow.TaskWaitDelayDescription(),
				required: false,
				defaultValue: session.TaskWaitDelay),
		],
	};

	private static string DefaultScriptPath(string doricoVersion) =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			$"Dorico {doricoVersion}",
			"Script Plug-ins") + Path.DirectorySeparatorChar;
}
