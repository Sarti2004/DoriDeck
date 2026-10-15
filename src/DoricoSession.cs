using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MacroDeck.Sdk;
using ScoreInterface;
using ScoreInterface.Comms;
using ScoreInterface.Enums;
using ScoreInterface.Responses;
using DoriDeck.Services;
using Lea;
using Serilog;
using System.Text.Json.Nodes;

namespace DoriDeck;

/// Owns the connection to Dorico and the state actions need to read. Registered as a singleton so the integration and
/// every action share one instance instead of reaching through a static singleton.
public sealed class DoricoSession : IAsyncDisposable
{
	private const string ClientName = "DoricoMacroDeckPlugin";
	private const string SessionTokenKey = "SessionToken";
	private static readonly TimeSpan StatusDebounceDelay = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan DoricoRequestTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan VariableLookupTimeout = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan AutoConnectRetryInterval = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan AutoConnectWindow = TimeSpan.FromSeconds(90);

	public const int DefaultFlowSwitchDelay = 150;
	public const int DefaultTaskWaitDelay = 200;
	public const string DefaultDoricoVersion = "6";

	private readonly IScoreInterfaceRemote _remote;
	private readonly IEventAggregator _eventAggregator;
	private readonly IKeyboardService _keyboard;
	private readonly IApplicationFocusService _applicationFocus;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _connectGate = new(1, 1);
	private readonly SemaphoreSlim _contextVariableGate = new(1, 1);
	private readonly SubscriptionToken _disconnectSubscription;
	private readonly SubscriptionToken _statusSubscription;
	private readonly SubscriptionToken _optionsChangedSubscription;
	private readonly SubscriptionToken _selectionChangedSubscription;
	private readonly object _statusDebounceLock = new();
	private readonly object _scoreInfoLock = new();
	private readonly ScoreOpenLogReader _scoreOpenLogReader;

	// Score info already read, by OCF session id (= openScoreID), so switching between open scores reads neither the
	// log nor the score again. A few KB per score opened in this Dorico run; cleared when Dorico restarts.
	private readonly Dictionary<int, LoadedScore> _loadedScores = new();
	private int? _loadedScoreId;
	private readonly CancellationTokenSource _shutdownCts = new();

	private IIntegrationContext? _context;
	private Guid _configEntryId;
	private CancellationTokenSource? _statusDebounceCts;
	private DynamicReplacementWalker? _dynamicReplacementWalker;
	private DoricoLaunchWatcher? _launchWatcher;
	private int _autoConnecting;
	private string _doricoVersion = DefaultDoricoVersion;
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
		_scoreOpenLogReader = new ScoreOpenLogReader(logger);

		_disconnectSubscription = _eventAggregator.Subscribe<DisconnectResponse>(OnDoricoDisconnected);
		_statusSubscription = _eventAggregator.Subscribe<StatusResponse>(OnDoricoStatusChanged);
		_optionsChangedSubscription = _eventAggregator.Subscribe<OptionsChangedResponse>(OnDoricoOptionsChanged);
		_selectionChangedSubscription = _eventAggregator.Subscribe<SelectionChangedResponse>(OnDoricoSelectionChanged);
		_remote.RawMessageReceived += OnDoricoRawMessage;
	}

	public bool IsConnected => _remote.IsConnected;

	public IScoreInterfaceRemote Remote => _remote;

	public int FlowsCount => _flowsCount;

	public string ScriptPath { get; private set; } = string.Empty;

	public string DoricoVersion { get; private set; } = DefaultDoricoVersion;

	public bool AutoLoadScripts { get; private set; }

	public int FlowSwitchDelay { get; private set; } = DefaultFlowSwitchDelay;

	public int TaskWaitDelay { get; private set; } = DefaultTaskWaitDelay;

	public bool HasScore { get; private set; }

	public bool HasSelection { get; private set; }

	public int ActiveOpenScoreId { get; private set; } = -1;

	public string NoteInputMode { get; private set; } = string.Empty;

	public string Accidental { get; private set; } = string.Empty;

	public string WindowModeRaw { get; private set; } = string.Empty;

	public string WindowMode { get; private set; } = string.Empty;

	/// <summary>Dorico's note-input duration, e.g. "kCrotchet", or empty until a status reports one.</summary>
	public string Duration { get; private set; } = string.Empty;

	public int RhythmDots { get; private set; }

	public event Action<int>? RhythmDotsChanged;

	public bool NoteInputActive { get; private set; }

	/// <summary>Flag reported from Dorico</summary>
	public event Action<bool>? NoteInputActiveChanged;

	public bool RestMode { get; private set; }

	public bool InPlayback { get; private set; }

	public bool IsRecording { get; private set; }

	/// <summary>Dorico's rhythmic grid resolution, e.g. "kCrotchet", or empty until a status reports one.</summary>
	public string RhythmicGridResolution { get; private set; } = string.Empty;

	/// <summary>Flag reported from Dorico</summary>
	public event Action<string>? DurationChanged;

	/// <summary>Raised when Dorico sends a confirmed disconnect notification.</summary>
	public event Action? Disconnected;

	public string CurrentFlowId { get; private set; } = string.Empty;

	public string CurrentFlowName { get; private set; } = string.Empty;

	public bool TupletMode { get; set; }

	public IReadOnlyList<CommandInfo> AvailableCommands { get; private set; } = [];

	public string ScoreInfoPath { get; private set; } = string.Empty;

	public string ScoreInfoJson { get; private set; } = string.Empty;

	public event Action<string>? ScoreInfoChanged;

	public ScoreSummary? ScoreSummary { get; private set; }

	public byte[]? ScorePreviewPng { get; private set; }

	public event Action? DisplayStateChanged;

	///list of recent files from FileList.xml</summary>
	public IReadOnlyList<string> RecentFiles { get; private set; } = [];

	public string RecentFileName(int index)
	{
		var recentFiles = RecentFiles;
		return index < recentFiles.Count ? Path.GetFileNameWithoutExtension(recentFiles[index]) : string.Empty;
	}

	public string DoricoRecentFileListPath =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			$"Dorico {_doricoVersion}",
			"recentFileList.xml");

	public string DoricoApplicationLogPath =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			$"Dorico {_doricoVersion}",
			"application.log");

	public DynamicReplacementWalker DynamicReplacementWalker =>
		_dynamicReplacementWalker ??=
			new DynamicReplacementWalker(this, _eventAggregator, _keyboard, _applicationFocus, _logger);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_configEntryId = await ResolveConfigEntryIdAsync(context);

		DoricoVersion = await ReadConfigValueAsync(DoriDeckConfigKeys.DoricoVersion, DefaultDoricoVersion);
		_doricoVersion = DoricoVersion;

		ScriptPath = await ReadConfigValueAsync(DoriDeckConfigKeys.ScriptPath, DefaultScriptPath());
		AutoLoadScripts = await ReadConfigBoolAsync(DoriDeckConfigKeys.AutoLoadScripts, defaultValue: false);
		FlowSwitchDelay = await ReadConfigIntAsync(DoriDeckConfigKeys.FlowSwitchDelay, DefaultFlowSwitchDelay);
		TaskWaitDelay = await ReadConfigIntAsync(DoriDeckConfigKeys.TaskWaitDelay, DefaultTaskWaitDelay);

		_launchWatcher = new DoricoLaunchWatcher(DoricoApplicationLogPath, _logger);
		_launchWatcher.Launched += OnDoricoLaunched;

		await EnsureConnectedAsync();
		LoadRecentFiles();
	}

	private void OnDoricoLaunched()
	{
		ForgetLoadedScores();

		if (IsConnected || Interlocked.Exchange(ref _autoConnecting, 1) == 1)
		{
			return;
		}

		_ = AutoConnectAsync();
	}

	/// Dorico takes a while after launch to open its Remote Control port, so retry for a bounded window.
	private async Task AutoConnectAsync()
	{
		try
		{
			if (string.IsNullOrEmpty(await ReadSessionTokenAsync()))
			{
				_logger.Information("Dorico started, but DoriDeck has no saved session; can not connect automatically.");
				return;
			}

			var started = Stopwatch.StartNew();
			while (!IsConnected && started.Elapsed < AutoConnectWindow)
			{
				await Task.Delay(AutoConnectRetryInterval, _shutdownCts.Token);

				// Cheap probe first so attempts made while Dorico is still starting don't each log a failure.
				if (await IsRemoteControlListeningAsync(_shutdownCts.Token) && await EnsureConnectedAsync())
				{
					_logger.Information("Connected to Dorico automatically after it started.");
					return;
				}
			}
		}
		catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to connect to Dorico automatically.");
		}
		finally
		{
			Interlocked.Exchange(ref _autoConnecting, 0);
		}
	}

	private static async Task<bool> IsRemoteControlListeningAsync(CancellationToken cancellationToken)
	{
		try
		{
			var port = new Uri(new ConnectionArguments().Address).Port;
			using var client = new TcpClient();
			await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}

	private sealed record LoadedScore(string Path, string Json, ScoreSummary Summary);

	/// Shows the info of the active score. Only a score not seen before needs the log, and only the lines Dorico
	/// wrote since the last read; a score already shown, or switched back to, costs a dictionary lookup.
	private void RefreshScoreInfo()
	{
		// Refreshes run on the thread pool; serialize them so the last one to finish reflects the latest active id.
		lock (_scoreInfoLock)
		{
			if (!HasScore)
			{
				return;
			}

			var scoreId = ActiveOpenScoreId;
			if (_loadedScoreId == scoreId)
			{
				return;
			}

			if (!_loadedScores.TryGetValue(scoreId, out var score))
			{
				var scorePath = _scoreOpenLogReader.GetScorePath(scoreId);
				if (scorePath is null)
				{
					_scoreOpenLogReader.ReadNewlyOpenedScores(DoricoApplicationLogPath);
					scorePath = _scoreOpenLogReader.GetScorePath(scoreId);
				}

				// No path means a score that was never saved, so there is nothing to read. Clear the previous score's
				// info rather than showing it for this one, and remember the id so the log isn't searched again.
				score = scorePath is null ? null : LoadScore(scorePath);
				if (score is not null)
				{
					_loadedScores[scoreId] = score;
				}
			}

			_loadedScoreId = scoreId;
			ShowScore(score);
		}
	}

	private LoadedScore? LoadScore(string scorePath)
	{
		try
		{
			var json = ScoreInfoReader.ReadJson(scorePath);
			//_logger.Information("Json.{json}", json);
			return new LoadedScore(scorePath, json, ScoreSummary.FromJson(json, scorePath));
		}
		catch (Exception ex)
		{
			_logger.Information(ex, "Failed to read score info from {Path}.", scorePath);
			return null;
		}
	}

	private void ShowScore(LoadedScore? score)
	{
		ScoreInfoPath = score?.Path ?? string.Empty;
		ScoreInfoJson = score?.Json ?? string.Empty;
		ScoreSummary = score?.Summary;

		// Not cached: at a few hundred KB each, previews of every score opened would add up. One zip entry per switch.
		ScorePreviewPng = score is null ? null : ReadPreview(score.Path);

		ScoreInfoChanged?.Invoke(ScoreInfoJson);
		DisplayStateChanged?.Invoke();
	}

	/// Session ids restart from 0 in every Dorico run, so cached info must not outlive it.
	private void ForgetLoadedScores()
	{
		lock (_scoreInfoLock)
		{
			_scoreOpenLogReader.Reset();
			_loadedScores.Clear();
			_loadedScoreId = null;
		}
	}

	private byte[]? ReadPreview(string scorePath)
	{
		try
		{
			return ScoreInfoReader.ReadPreviewPng(scorePath);
		}
		catch (Exception ex)
		{
			_logger.Information(ex, "Failed to read the score preview from {Path}.", scorePath);
			return null;
		}
	}

	private static async Task<Guid> ResolveConfigEntryIdAsync(IIntegrationContext context)
	{
		var entries = await context.Config.GetEntriesAsync();

		var match = entries.FirstOrDefault(entry =>
			string.Equals(entry.Title, DoriDeckConfigKeys.ConfigEntryTitle, StringComparison.Ordinal));

		return (match ?? (entries.Count > 0 ? entries[0] : null))?.Id ?? Guid.Empty;
	}

	public Task ShutdownAsync()
	{
		_shutdownCts.Cancel();

		lock (_statusDebounceLock)
		{
			_statusDebounceCts?.Cancel();
			_statusDebounceCts?.Dispose();
			_statusDebounceCts = null;
		}

		_dynamicReplacementWalker?.Dispose();
		_dynamicReplacementWalker = null;

		_launchWatcher?.Dispose();
		_launchWatcher = null;

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

			try
			{
				// Bounded: the host may not answer promptly (e.g. while draining is paused), and an action's reply
				// must not wait on it. An unresolved placeholder is left as-is, same as an unknown variable.
				var handle = await _context.Variables.GetByNameAsync(name).WaitAsync(VariableLookupTimeout);
				replacements[name] = handle?.Value?.ToString() ?? match.Value;
			}
			catch (TimeoutException)
			{
				_logger.Warning("Timed out resolving Macro Deck variable {Variable}.", name);
				replacements[name] = match.Value;
			}
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

		// Picks up a score opened before we connected, e.g. Dorico launched by double-clicking a .dorico file.
		_ = Task.Run(RefreshScoreInfo);

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
		_logger.Information("Dorico disconnected.");
		ForgetLoadedScores();
		ResetDisconnectedState();
		Disconnected?.Invoke();
	}

	// Debug: logs every status message exactly as Dorico sent it
	private void OnDoricoRawMessage(string json)
	{
		if (json.Contains("\"status\"", StringComparison.OrdinalIgnoreCase))
		{
			//_logger.Information("Dorico raw status: {Json}", json);
		}
			//_logger.Information("Dorico raw status: {Json}", json);
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

	private void OnDoricoOptionsChanged(OptionsChangedResponse response)
	{
		LoadRecentFiles();

		// Opening a score updates the recent file list, so this is also our cue to look for it in the log.
		_ = Task.Run(RefreshScoreInfo);
	}

	/// Dorico sends selectionchanged when the user switches to another open score, before any status reports it.
	private void OnDoricoSelectionChanged(SelectionChangedResponse response)
	{
		if (response.OpenScoreId == ActiveOpenScoreId)
		{
			return;
		}

		ActiveOpenScoreId = response.OpenScoreId;
		_ = Task.Run(RefreshScoreInfo);
	}

	private void LoadRecentFiles()
	{
		var path = DoricoRecentFileListPath;

		try
		{
			if (!File.Exists(path))
			{
				RecentFiles = [];
				DisplayStateChanged?.Invoke();
				return;
			}

			RecentFiles = System.Xml.Linq.XDocument.Load(path)
				.Descendants("file")
				.Select(file => file.Value.Trim())
				.Where(file => file.Length > 0)
				.ToList();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to read Dorico recent file list {Path}.", path);
		}

		DisplayStateChanged?.Invoke();
	}

	private async Task RefreshFlowsDebouncedAsync(StatusResponse status, CancellationToken cancellationToken)
	{
		//_logger.Information("Dorico status: {@Status}", status);
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
					//_logger.Information("Dorico status2: {@Status}", status);
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

				//_logger.Information($"Refreshed flows: {_flowsCount} flows found");
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
		var scoreChanged = status.HasScore && (!HasScore || status.ActiveOpenScoreID != ActiveOpenScoreId);
		var previousHasScore = HasScore;
		var previousWindowMode = WindowMode;
		var previousInPlayback = InPlayback;
		var previousIsRecording = IsRecording;
		var previousGridResolution = RhythmicGridResolution;

		HasScore = status.HasScore;
		HasSelection = status.HasSelection;
		ActiveOpenScoreId = status.ActiveOpenScoreID;

		if (scoreChanged)
		{
			_ = Task.Run(RefreshScoreInfo);
		}
		NoteInputMode = FriendlyDoricoMode(status.NoteInputMode.ToString());
		Accidental = status.Accidental.ToString();
		WindowModeRaw = status.WindowMode.ToString();
		WindowMode = FriendlyDoricoMode(WindowModeRaw);
		//_logger.Information("Dorico status: {@Status}", status);
		InPlayback = status.InPlayback ?? InPlayback;
		IsRecording = status.IsRecording ?? IsRecording;
		if (!string.IsNullOrEmpty(status.RhythmicGridResolutionValue))
		{
			RhythmicGridResolution = status.RhythmicGridResolutionValue;
		}

		if (HasScore != previousHasScore ||
			!string.Equals(WindowMode, previousWindowMode, StringComparison.Ordinal) ||
			InPlayback != previousInPlayback ||
			IsRecording != previousIsRecording ||
			!string.Equals(RhythmicGridResolution, previousGridResolution, StringComparison.Ordinal))
		{
			DisplayStateChanged?.Invoke();
		}

		if (status.RhythmDots.HasValue)
		{
			SetRhythmDots(status.RhythmDots.Value);
		}

		if (status.RestMode.HasValue)
		{
			RestMode = status.RestMode.Value;
		}

		if (status.NoteInputActive.HasValue)
		{
			//_logger.Information($"Dorico NoteInputActive: {status.NoteInputActive.Value}");
			SetNoteInputActive(status.NoteInputActive.Value);
		}

		if (!string.IsNullOrEmpty(status.Duration) &&
			!string.Equals(status.Duration, Duration, StringComparison.OrdinalIgnoreCase))
		{
			Duration = status.Duration;
			DurationChanged?.Invoke(Duration);
		}
	}

	private void SetRhythmDots(int active)
	{
		if (active != RhythmDots)
		{
			RhythmDots = active;
			RhythmDotsChanged?.Invoke(active);
		}
	}

	private void SetNoteInputActive(bool active)
	{
		if (active != NoteInputActive)
		{
			NoteInputActive = active;
			NoteInputActiveChanged?.Invoke(active);
		}
	}

	private void ResetDisconnectedState()
	{
		HasScore = false;
		HasSelection = false;
		ActiveOpenScoreId = -1;
		NoteInputMode = string.Empty;
		Accidental = string.Empty;
		WindowMode = string.Empty;
		WindowModeRaw = string.Empty;
		Duration = string.Empty;
		RhythmDots = 0;
		SetNoteInputActive(false);
		RestMode = false;
		InPlayback = false;
		IsRecording = false;
		RhythmicGridResolution = string.Empty;
		CurrentFlowId = string.Empty;
		CurrentFlowName = string.Empty;
		TupletMode = false;
		_flowsCount = 0;
		DisplayStateChanged?.Invoke();
	}

	private static string FriendlyDoricoMode(string rawMode) =>
		rawMode.Trim().TrimStart('k').Replace("Mode", string.Empty);

	private string DefaultScriptPath() =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Steinberg",
			$"Dorico {DoricoVersion}",
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
		_eventAggregator.Unsubscribe<OptionsChangedResponse>(_optionsChangedSubscription);
		_eventAggregator.Unsubscribe<SelectionChangedResponse>(_selectionChangedSubscription);
		_remote.RawMessageReceived -= OnDoricoRawMessage;

		lock (_statusDebounceLock)
		{
			_statusDebounceCts?.Dispose();
		}

		_dynamicReplacementWalker?.Dispose();
		_shutdownCts.Cancel();
		_shutdownCts.Dispose();
		_launchWatcher?.Dispose();
		_connectGate.Dispose();
		_contextVariableGate.Dispose();

		if (_remote.IsConnected)
		{
			await _remote.DisconnectAsync();
		}
	}
}
