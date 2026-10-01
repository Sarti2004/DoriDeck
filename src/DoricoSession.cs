using System.Globalization;
using MacroDeck.Sdk;
using ScoreInterface;
using ScoreInterface.Comms;
using ScoreInterface.Enums;
using ScoreInterface.Responses;
using DoriDeck.Services;
using Lea;
using Serilog;

namespace DoriDeck;

/// Owns the connection to Dorico and the state actions need to read. Registered as a singleton so the integration and
/// every action share one instance instead of reaching through a static singleton.
public sealed class DoricoSession : IAsyncDisposable
{
	private const string ClientName = "DoricoMacroDeckPlugin";
	private const string SessionTokenKey = "SessionToken";
	private static readonly TimeSpan StatusDebounceDelay = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan DoricoRequestTimeout = TimeSpan.FromSeconds(5);

	public const int DefaultFlowSwitchDelay = 150;
	public const int DefaultTaskWaitDelay = 100;

	private readonly IScoreInterfaceRemote _remote;
	private readonly IEventAggregator _eventAggregator;
	private readonly IKeyboardService _keyboard;
	private readonly IApplicationFocusService _applicationFocus;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _connectGate = new(1, 1);
	private readonly SemaphoreSlim _contextVariableGate = new(1, 1);
	private readonly SubscriptionToken _disconnectSubscription;
	private readonly SubscriptionToken _statusSubscription;
	private readonly object _statusDebounceLock = new();

	private IIntegrationContext? _context;
	private Guid _configEntryId;
	private CancellationTokenSource? _statusDebounceCts;
	private DynamicReplacementWalker? _dynamicReplacementWalker;
	private string _doricoVersion = "6";
	private int _flowsCount;

	public DoricoSession(
		IScoreInterfaceRemote remote,
		IEventAggregator eventAggregator,
		IKeyboardService keyboard,
		IApplicationFocusService applicationFocus,
		ILogger logger)
	{
		_remote = remote;
		_eventAggregator = eventAggregator;
		_keyboard = keyboard;
		_applicationFocus = applicationFocus;
		_logger = logger.ForContext<DoricoSession>();

		_disconnectSubscription = _eventAggregator.Subscribe<DisconnectResponse>(OnDoricoDisconnected);
		_statusSubscription = _eventAggregator.Subscribe<StatusResponse>(OnDoricoStatusChanged);
	}

	public bool IsConnected => _remote.IsConnected;

	public IScoreInterfaceRemote Remote => _remote;

	public int FlowsCount => _flowsCount;

	public string ScriptPath { get; private set; } = string.Empty;

	public bool AutoLoadScripts { get; private set; }

	public int FlowSwitchDelay { get; private set; } = DefaultFlowSwitchDelay;

	public int TaskWaitDelay { get; private set; } = DefaultTaskWaitDelay;

	public bool HasScore { get; private set; }

	public bool HasSelection { get; private set; }

	public int ActiveOpenScoreId { get; private set; }

	public string NoteInputMode { get; private set; } = string.Empty;

	public string Accidental { get; private set; } = string.Empty;

	public string WindowModeRaw { get; private set; } = string.Empty;

	public string WindowMode { get; private set; } = string.Empty;

	public string CurrentFlowId { get; private set; } = string.Empty;

	public string CurrentFlowName { get; private set; } = string.Empty;

	public bool TupletMode { get; set; }

	public IReadOnlyList<CommandInfo> AvailableCommands { get; private set; } = [];

	public string DoricoApplicationLogPath =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			$"Dorico {_doricoVersion}",
			"application.log");

	public DynamicReplacementWalker DynamicReplacementWalker =>
		_dynamicReplacementWalker ??=
			new DynamicReplacementWalker(this, _eventAggregator, _keyboard, _applicationFocus, _logger);

	/// <summary>
	/// Reads settings from persisted configuration and connects to Dorico. Safe to call repeatedly:
	/// the host reruns it after every reconnect and configuration change.
	/// </summary>
	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_configEntryId = await ResolveConfigEntryIdAsync(context);

		ScriptPath = await ReadConfigValueAsync(DoriDeckConfigKeys.ScriptPath, DefaultScriptPath());
		AutoLoadScripts = await ReadConfigBoolAsync(DoriDeckConfigKeys.AutoLoadScripts, defaultValue: false);
		FlowSwitchDelay = await ReadConfigIntAsync(DoriDeckConfigKeys.FlowSwitchDelay, DefaultFlowSwitchDelay);
		TaskWaitDelay = await ReadConfigIntAsync(DoriDeckConfigKeys.TaskWaitDelay, DefaultTaskWaitDelay);

		await EnsureConnectedAsync();
	}

	/// <summary>
	/// <c>IIntegrationContext</c> carries no entry id of its own, so the entry this plugin was configured
	/// into has to be found by scanning <c>IIntegrationConfig.GetEntriesAsync</c> for the title
	/// <see cref="DoriDeckConfigKeys.ConfigEntryTitle"/> gave it. Falls back to the first entry, then to
	/// <see cref="Guid.Empty"/>, so a renamed or not-yet-configured entry still resolves to something
	/// rather than throwing.
	/// </summary>
	private static async Task<Guid> ResolveConfigEntryIdAsync(IIntegrationContext context)
	{
		var entries = await context.Config.GetEntriesAsync();

		var match = entries.FirstOrDefault(entry =>
			string.Equals(entry.Title, DoriDeckConfigKeys.ConfigEntryTitle, StringComparison.Ordinal));

		return (match ?? entries.FirstOrDefault())?.Id ?? Guid.Empty;
	}

	public Task ShutdownAsync()
	{
		lock (_statusDebounceLock)
		{
			_statusDebounceCts?.Cancel();
			_statusDebounceCts?.Dispose();
			_statusDebounceCts = null;
		}

		_dynamicReplacementWalker?.Dispose();
		_dynamicReplacementWalker = null;

		return _remote.IsConnected ? _remote.DisconnectAsync() : Task.CompletedTask;
	}

	public async Task<bool> EnsureConnectedAsync()
	{
		if (IsConnected)
		{
			return true;
		}

		if (!await _connectGate.WaitAsync(0))
		{
			await _connectGate.WaitAsync();
			try
			{
				return IsConnected;
			}
			finally
			{
				_connectGate.Release();
			}
		}

		try
		{
			if (IsConnected)
			{
				return true;
			}

			await ConnectAsync();
		}
		finally
		{
			_connectGate.Release();
		}

		return IsConnected;
	}

	public async Task ConnectAsync()
	{
		if (_remote.IsConnected)
		{
			return;
		}

		var savedToken = await ReadSessionTokenAsync();		
		var connectionArgs = string.IsNullOrEmpty(savedToken)
			? new ConnectionArguments()
			: new ConnectionArguments(savedToken);

		try
		{
			await _remote.ConnectAsync(ClientName, connectionArgs);
			await OnConnectedAsync();
		}
		catch (Exception ex) when (ex.Message.Contains("kClientRejected_ConnectTokenRejected"))
		{
			_logger.Warning("Dorico session token rejected. Reconnecting without a cached session.");
			await ClearSessionTokenAsync();

			try
			{
				await _remote.ConnectAsync(ClientName, new ConnectionArguments());
				await OnConnectedAsync();
			}
			catch (Exception retryEx)
			{
				_logger.Warning(retryEx, "Failed to connect to Dorico on retry.");
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to connect to Dorico.");
		}
	}

	public Task RefreshContextAsync(CancellationToken cancellationToken = default) =>
		RefreshContextAsync(status: null, cancellationToken);

	private static readonly System.Text.RegularExpressions.Regex VariablePlaceholder = new(
		@"\$\{(?<name>[a-zA-Z0-9_.\-]+)\}",
		System.Text.RegularExpressions.RegexOptions.Compiled);

	/// <summary>
	/// Substitutes every <c>${name}</c> placeholder in <paramref name="content"/> with that Macro Deck
	/// variable's current value. A placeholder naming a variable that does not exist is left as-is.
	/// </summary>
	public async Task<string> ResolveVariablesAsync(string content)
	{
		if (_context is null)
		{
			return content;
		}

		var matches = VariablePlaceholder.Matches(content).Cast<System.Text.RegularExpressions.Match>().ToList();
		if (matches.Count == 0)
		{
			return content;
		}

		var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var match in matches)
		{
			var name = match.Groups["name"].Value;
			if (replacements.ContainsKey(name))
			{
				continue;
			}

			var handle = await _context.Variables.GetByNameAsync(name);
			replacements[name] = handle?.Value?.ToString() ?? match.Value;
		}

		return VariablePlaceholder.Replace(content, match => replacements[match.Groups["name"].Value]);
	}

	private async Task OnConnectedAsync()
	{
		if (!_remote.IsConnected)
		{
			return;
		}

		if (!string.IsNullOrEmpty(_remote.SessionToken))
		{
			await SaveSessionTokenAsync(_remote.SessionToken);
		}

		_logger.Information("Connected to Dorico.");
		await RefreshContextAsync();

		var appInfo = await _remote.GetAppInfoAsync();
		if (appInfo?.Number is { Length: > 0 } versionNumber)
		{
			var majorVersion = versionNumber.Split('.')[0];
			if (!string.IsNullOrEmpty(majorVersion))
			{
				_doricoVersion = majorVersion;
			}
		}

		try
		{
			AvailableCommands = (await _remote.GetCommandsAsync())
				.Where(c => !string.IsNullOrEmpty(c.Name))
				.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to fetch the Dorico command list.");
		}
	}

	private void OnDoricoDisconnected(DisconnectResponse response)
	{
		_logger.Warning("Dorico disconnected.");
		ResetDisconnectedState();
	}

	private void OnDoricoStatusChanged(StatusResponse status)
	{
		ApplyStatus(status);

		CancellationToken cancellationToken;
		lock (_statusDebounceLock)
		{
			_statusDebounceCts?.Cancel();
			_statusDebounceCts?.Dispose();
			_statusDebounceCts = new CancellationTokenSource();
			cancellationToken = _statusDebounceCts.Token;
		}

		_ = RefreshFlowsDebouncedAsync(status, cancellationToken);
	}

	private async Task RefreshFlowsDebouncedAsync(StatusResponse status, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(StatusDebounceDelay, cancellationToken);
			await RefreshContextAsync(status, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// A newer status event restarted the debounce period.
		}
	}

	private async Task RefreshContextAsync(StatusResponse? status, CancellationToken cancellationToken)
	{
		if (!_remote.IsConnected)
		{
			ResetDisconnectedState();
			return;
		}

		if (!await _contextVariableGate.WaitAsync(0, cancellationToken))
		{
			return;
		}

		try
		{
			if (status == null)
			{
				try
				{
					status = await _remote.GetStatusAsync(cancellationToken).WaitAsync(DoricoRequestTimeout, cancellationToken);
					//_logger.Information("{Status}", status);
				}
				catch (TimeoutException)
				{
					status = _remote.CurrentStatus;
				}

				if (status != null)
				{
					ApplyStatus(status);
				}

				
			}

			if (status?.HasScore != true)
			{
				CurrentFlowId = string.Empty;
				CurrentFlowName = string.Empty;
				_flowsCount = 0;
				return;
			}

			try
			{
				var flowsResponse = await _remote.GetFlowsAsync(cancellationToken).WaitAsync(DoricoRequestTimeout, cancellationToken);
				var flows = flowsResponse?.Flows?.ToList() ?? [];
				_flowsCount = flows.Count;

				var activeWindowTitle = ActiveWindowReader.GetActiveDoricoWindowTitle();
				var currentFlow = ResolveCurrentFlow(flows, activeWindowTitle);

				_logger.Information($"Refreshed flows: {_flowsCount} flows found");
				CurrentFlowId = currentFlow?.FlowID.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
				CurrentFlowName = currentFlow?.FlowName ?? string.Empty;
			}
			catch (TimeoutException)
			{
				_logger.Warning("Timed out while refreshing the active Dorico flow.");
			}
		}
		finally
		{
			_contextVariableGate.Release();
		}
	}

	private static Flow? ResolveCurrentFlow(IReadOnlyList<Flow> flows, string? activeWindowTitle)
	{
		if (flows.Count == 0)
		{
			return null;
		}

		if (flows.Count == 1)
		{
			return flows[0];
		}

		if (string.IsNullOrWhiteSpace(activeWindowTitle))
		{
			return null;
		}

		var expectedFlowTitle = activeWindowTitle.Split(" in ")[0].Trim();

		return flows.FirstOrDefault(flow =>
			flow.FlowName.StartsWith(expectedFlowTitle, StringComparison.OrdinalIgnoreCase));
	}

	private void ApplyStatus(StatusResponse status)
	{
		HasScore = status.HasScore;
		HasSelection = status.HasSelection;
		ActiveOpenScoreId = status.ActiveOpenScoreID;
		NoteInputMode = FriendlyDoricoMode(status.NoteInputMode.ToString());
		Accidental = status.Accidental.ToString();
		WindowModeRaw = status.WindowMode.ToString();
		WindowMode = FriendlyDoricoMode(WindowModeRaw);
	}

	private void ResetDisconnectedState()
	{
		HasScore = false;
		HasSelection = false;
		ActiveOpenScoreId = 0;
		NoteInputMode = string.Empty;
		Accidental = string.Empty;
		WindowMode = string.Empty;
		WindowModeRaw = string.Empty;
		CurrentFlowId = string.Empty;
		CurrentFlowName = string.Empty;
		TupletMode = false;
		_flowsCount = 0;
	}

	private static string FriendlyDoricoMode(string rawMode) =>
		rawMode.Trim().TrimStart('k').Replace("Mode", string.Empty);

	private static string DefaultScriptPath() =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			"Dorico 6",
			"Script Plug-ins") + Path.DirectorySeparatorChar;

	private async Task<string> ReadConfigValueAsync(string key, string fallback)
	{
		if (_context is null)
		{
			return fallback;
		}

		var value = await _context.Config.GetStringAsync(_configEntryId, key);
		return string.IsNullOrEmpty(value) ? fallback : value;
	}

	private async Task<int> ReadConfigIntAsync(string key, int fallback)
	{
		if (_context is null)
		{
			return fallback;
		}

		var value = await _context.Config.GetStringAsync(_configEntryId, key);
		return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;
	}

	private async Task<bool> ReadConfigBoolAsync(string key, bool defaultValue)
	{
		if (_context is null)
		{
			return defaultValue;
		}

		var value = await _context.Config.GetStringAsync(_configEntryId, key);
		return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
	}

	private async Task<string?> ReadSessionTokenAsync() =>
		_context is null ? null : await _context.Config.GetSecretAsync(_configEntryId, SessionTokenKey);

	private Task SaveSessionTokenAsync(string token) =>
		_context?.Config.SetSecretAsync(_configEntryId, SessionTokenKey, token) ?? Task.CompletedTask;

	public Task ClearSessionTokenAsync() =>
		_context?.Config.SetSecretAsync(_configEntryId, SessionTokenKey, string.Empty) ?? Task.CompletedTask;

	public async ValueTask DisposeAsync()
	{
		_eventAggregator.Unsubscribe<DisconnectResponse>(_disconnectSubscription);
		_eventAggregator.Unsubscribe<StatusResponse>(_statusSubscription);

		lock (_statusDebounceLock)
		{
			_statusDebounceCts?.Dispose();
		}

		_dynamicReplacementWalker?.Dispose();
		_connectGate.Dispose();
		_contextVariableGate.Dispose();

		if (_remote.IsConnected)
		{
			await _remote.DisconnectAsync();
		}
	}
}
