using System.Globalization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using DoriDeck.Actions;
using DoriDeck.Services;
using Serilog;

namespace DoriDeck;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IConfigFlowProvider
{
	private readonly DoricoSession _session;
	private readonly ILogger _logger;
	private readonly IReadOnlyList<IActionDefinition> _staticActions;
	private IReadOnlyList<IActionDefinition> _actions;
	private HashSet<string> _dynamicScriptNames = [];

	public PluginIntegration(DoricoSession session, IKeyboardService keyboard, ILogger logger)
	{
		_session = session;
		_logger = logger.ForContext<PluginIntegration>();

		_staticActions =
		[
			new ConnectDoricoAction(_session),
			new RunScriptAction(_session, logger),
			new RunCommandAction(_session),
			new CustomScriptAction(_session),
			new RunCommandsAction(_session, logger),
			new InsertLyricsAction(_session, keyboard, logger),
			new DynamicReplacementAction(_session, logger),
			new RespellNoteAction(_session),
			new PickupMeasureAction(_session),
			new SectionHeaderAction(_session),
			new TupletAction(_session),
			new ChoirReductionAction(_session, keyboard),
			new ClearConnectionTokenAction(_session),
		];

		_actions = _staticActions;
	}

	public IReadOnlyList<IActionDefinition> Actions => _actions;

	/// <summary>
	/// Single source of truth for the variables this plugin publishes: the public name (a released API -
	/// renaming one breaks every widget already bound to it), the local id the host round-trips back
	/// through <see cref="ReadAsync"/> (must be lowercase/hyphenated - the public names aren't, so they
	/// can't double as the id), and how to read the current value from the session.
	/// </summary>
	private static readonly (string Name, string Id, VariableType Type, Func<DoricoSession, object?> Read)[] VariableSpecs =
	[
		("dorico_connected", "connected", VariableType.Boolean, s => s.IsConnected),
		("dorico_mode", "mode", VariableType.Text, s => s.WindowMode),
		("dorico_mode_raw", "moderaw", VariableType.Text, s => s.WindowModeRaw),
		("dorico_flow_id", "flow-id", VariableType.Text, s => s.CurrentFlowId),
		("dorico_flow_name", "flow-name", VariableType.Text, s => s.CurrentFlowName),
		("dorico_flow_count", "flow-count", VariableType.Text, s => s.FlowsCount.ToString(CultureInfo.InvariantCulture)),
		("dorico_has_score", "has-score", VariableType.Boolean, s => s.HasScore),
		("dorico_hasSelection", "has-selection", VariableType.Boolean, s => s.HasSelection),
		("dorico_activeOpenScoreID", "active-open-score-id", VariableType.Numeric, s => s.ActiveOpenScoreId),
		("dorico_noteInputMode", "note-input-mode", VariableType.Text, s => s.NoteInputMode),
		("dorico_accidental", "accidental", VariableType.Text, s => s.Accidental),
		("dorico_tuplet_mode", "tuplet-mode", VariableType.Boolean, s => s.TupletMode),
	];

	private static readonly Dictionary<string, Func<DoricoSession, object?>> VariableReadersById =
		VariableSpecs.ToDictionary(spec => spec.Id, spec => spec.Read, StringComparer.Ordinal);

	public IReadOnlyList<VariableDefinition> Variables { get; } =
		[.. VariableSpecs.Select(spec => VariableDefinition.Eager(spec.Name, spec.Type) with { Id = spec.Id })];

	public IReadOnlyList<VariableDefinition> DeclaredVariables => Variables;

	public bool SupportsCatalog => false;

	public bool SupportsSearch => false;

	public bool SupportsPush => false;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		VariableReading reading = VariableReadersById.TryGetValue(localId, out var read)
			? VariableReading.Of(read(_session))
			: VariableReading.Unavailable;

		return ValueTask.FromResult(reading);
	}

	public IConfigFlow CreateConfigFlow() => new DoriDeckConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await _session.InitializeAsync(context);
		RefreshDynamicScriptActions();
		_logger.Information("Initialized.");
	}

	public Task ShutdownAsync() => _session.ShutdownAsync();

	/// Creates a new action for every script in the script path
	private void RefreshDynamicScriptActions()
	{
		IReadOnlyList<string> scriptNames = _session.AutoLoadScripts
			? ScriptCatalog.DiscoverScriptNames(_session.ScriptPath, _logger)
			: [];

		var scriptNameSet = new HashSet<string>(scriptNames, StringComparer.Ordinal);
		if (scriptNameSet.SetEquals(_dynamicScriptNames))
		{
			return;
		}

		_dynamicScriptNames = scriptNameSet;

		var usedIds = new HashSet<string>(StringComparer.Ordinal);
		var dynamicActions = scriptNames.Select(scriptName =>
		{
			var id = ScriptCatalog.MakeUniqueActionId(scriptName, usedIds);
			return (IActionDefinition)new DynamicScriptAction(_session, id, scriptName);
		});

		_actions = [.. _staticActions, .. dynamicActions];
	}
}
