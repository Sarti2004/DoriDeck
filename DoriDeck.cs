using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Logging;
using Microsoft.Extensions.DependencyInjection;
#if !DEBUG
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#endif
using DoricoNet;
using DoricoNet.Commands;
using DoricoNet.Comms;
using DoricoNet.Exceptions;
using DoricoNet.Responses;
using Lea;
using DoriDeck.Actions;
using DoriDeck.Services;

namespace DoriDeck;

public class Main : MacroDeckPlugin
{
    private IDoricoRemote? _doricoRemote;
    private IEventAggregator? _eventAggregator;
    private DynamicReplacementWalker? _dynamicReplacementWalker;
    private IServiceProvider? _serviceProvider;
    private SubscriptionToken? _disconnectSubscription;
    private SubscriptionToken? _statusSubscription;
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);
    private readonly SemaphoreSlim _contextVariableSemaphore = new(1, 1);

    private readonly IFlowResolver _flowResolver;
    private readonly DoricoContextVariableService _contextService;
    private readonly object _statusDebounceLock = new();
    private CancellationTokenSource? _statusDebounceCts;
    private IReadOnlyList<CommandInfo> _availableCommands = Array.Empty<CommandInfo>();

    private const string ClientName = "DoricoMacroDeckPlugin";
    private static readonly TimeSpan StatusDebounceDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DoricoRequestTimeout = TimeSpan.FromSeconds(5);
    private const string SessionTokenKey = "SessionToken";
    public const string ConnectionVariableName = "dorico_connected";
    public const string ModeVariableName = "dorico_mode";
    public const string ModeRawVariableName = "dorico_mode_raw";
    public const string CurrentFlowIdVariableName = "dorico_flow_id";
    public const string CurrentFlowNameVariableName = "dorico_flow_name";
    public const string FlowCountVariableName = "dorico_flow_count";
    public const string HasScoreVariableName = "dorico_has_score";
    public const string HasSelectionVariableName = "dorico_hasSelection";
    public const string ActiveOpenScoreIDVariableName = "dorico_activeOpenScoreID";
    public const string NoteInputModeVariableName = "dorico_noteInputMode";
    public const string WindowModeVariableName = "dorico_windowMode";
    public const string AccidentalVariableName = "dorico_accidental";
    public const string TupletModeVariableName = "dorico_tuplet_mode";

    private string _doricoVersion = "6";
    public int _flows_count = 0;

    public static Main Instance { get; private set; } = null!;

    public override bool CanConfigure => true;

    private string DefaultScriptPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Steinberg", $"Dorico {_doricoVersion}", "Script Plug-ins") + Path.DirectorySeparatorChar;

    /// <summary>
    /// Path to Dorico's own application log, e.g.
    /// "%AppData%\Steinberg\Dorico 5\application.log". Reflects the
    /// currently detected Dorico major version.
    /// </summary>
    public string DoricoApplicationLogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Steinberg", $"Dorico {_doricoVersion}", "application.log");

    public string ScriptPath
    {
        get => PluginConfiguration.GetValue(this, "ScriptPath") is string val && !string.IsNullOrEmpty(val)
            ? val
            : DefaultScriptPath;
        set => PluginConfiguration.SetValue(this, "ScriptPath", value);
    }

    public bool AutoLoadScripts
    {
        get => PluginConfiguration.GetValue(this, "AutoLoadScripts") is string v && bool.TryParse(v, out var b) && b;
        set => PluginConfiguration.SetValue(this, "AutoLoadScripts", value.ToString());
    }

    public bool ExtraActionsEnabled
    {
        get => PluginConfiguration.GetValue(this, "ExtraActionsEnabled") is string v && bool.TryParse(v, out var b) && b;
        set => PluginConfiguration.SetValue(this, "ExtraActionsEnabled", value.ToString());
    }

    public const int DefaultFlowSwitchDelay = 150;
    public const int DefaultTaskWaitDelay = 100;

    /// <summary>
    /// Delay (in milliseconds) applied after switching flows while running an action against all flows.
    /// </summary>
    public int FlowSwitchDelay
    {
        get => PluginConfiguration.GetValue(this, "FlowSwitchDelay") is string v && int.TryParse(v, out var i) && i >= 0
            ? i
            : DefaultFlowSwitchDelay;
        set => PluginConfiguration.SetValue(this, "FlowSwitchDelay", value.ToString());
    }

    /// <summary>
    /// Delay (in milliseconds) applied between consecutive commands within a command sequence.
    /// </summary>
    public int TaskWaitDelay
    {
        get => PluginConfiguration.GetValue(this, "TaskWaitDelay") is string v && int.TryParse(v, out var i) && i >= 0
            ? i
            : DefaultTaskWaitDelay;
        set => PluginConfiguration.SetValue(this, "TaskWaitDelay", value.ToString());
    }

    public bool IsConnected => _doricoRemote?.IsConnected ?? false;
    public IDoricoRemote? DoricoRemote => _doricoRemote;
    public DynamicReplacementWalker DynamicReplacementWalker =>
        _dynamicReplacementWalker
        ?? throw new InvalidOperationException(
            "The dynamic replacement walker has not been initialized.");

    /// <summary>
    /// Dorico commands fetched from the connected instance (name, display name and required/
    /// optional parameters), used to power command search/autocomplete, basic validation and
    /// parameter advice in action configurators.
    /// </summary>
    public IReadOnlyList<CommandInfo> AvailableCommands => _availableCommands;

    public Main()
    {
        Instance = this;
        _flowResolver = new FlowResolver();
        _contextService = new DoricoContextVariableService(this, _flowResolver);
    }

    public override void Enable()
    {
        try
        {
            Actions =
            [
                new ConnectDorico(),
                new RunScriptAction(),
                new RunCommandAction(),
                new CustomScriptAction(),
                new RunCommandsAction(),
                new InsertLyricsAction(),
                new DynamicReplacementAction()
            ];

            if (ExtraActionsEnabled)
            {
                Actions.Add(new RespellNote());
                Actions.Add(new PickupMeasure());
                Actions.Add(new SectionHeader());
                Actions.Add(new TupletAction());
            }

            if (AutoLoadScripts)
                LoadDynamicScriptActions();

            _contextService.UpdateConnectionVariable(IsConnected);
            _contextService.SaveDisconnectedContextVariables(ref _flows_count);

        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Error during Enable: {0}", ex.Message);
        }
    }

    public void LoadDynamicScriptActions()
    {
        try
        {
            var scriptPath = ScriptPath;
            if (!Directory.Exists(scriptPath))
            {
                MacroDeckLogger.Warning(Main.Instance, "Script path does not exist: {0}", scriptPath);
                return;
            }

            var luaFiles = Directory.GetFiles(scriptPath, "*.lua", SearchOption.AllDirectories);

            Actions.RemoveAll(a => a is DynamicScriptAction);

            foreach (var file in luaFiles)
            {
                var relativePath = Path.GetRelativePath(scriptPath, file);
                var scriptName = Path.ChangeExtension(relativePath, null)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');

                Actions.Add(new DynamicScriptAction
                {
                    ScriptName = scriptName
                });

                MacroDeckLogger.Information(Main.Instance, "Auto-loaded script action: {0}", scriptName);
            }

        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Error loading dynamic script actions: {0}", ex.Message);
        }
    }

    public void Disable()
    {
        try
        {
            _dynamicReplacementWalker?.Dispose();
            _dynamicReplacementWalker = null;

            lock (_statusDebounceLock)
            {
                _statusDebounceCts?.Cancel();
                _statusDebounceCts?.Dispose();
                _statusDebounceCts = null;
            }

            if (_doricoRemote?.IsConnected == true)
            {
                _doricoRemote.DisconnectAsync().GetAwaiter().GetResult();
            }

            if (_disconnectSubscription != null && _eventAggregator != null)
            {
                _eventAggregator.Unsubscribe<DisconnectResponse>(_disconnectSubscription);
                _disconnectSubscription = null;
            }

            if (_statusSubscription != null && _eventAggregator != null)
            {
                _eventAggregator.Unsubscribe<StatusResponse>(_statusSubscription);
                _statusSubscription = null;
            }

            (_serviceProvider as IDisposable)?.Dispose();
            _doricoRemote = null;
            _eventAggregator = null;
            _serviceProvider = null;

            _contextService.UpdateConnectionVariable(IsConnected);
            _contextService.SaveDisconnectedContextVariables(ref _flows_count);

            MacroDeckLogger.Information(Main.Instance, "Plugin disabled successfully");
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Error during Disable: {0}", ex.Message);
        }
    }

    public override void OpenConfigurator()
    {
        using var configurator = new PluginConfigurator();
        configurator.ShowDialog();
    }

    private void SetupDoricoServices()
    {
        if (_doricoRemote != null) return;

        (_serviceProvider as IDisposable)?.Dispose();

        var services = new ServiceCollection();

        services
            .AddSingleton<IEventAggregator, EventAggregator>()
            .AddTransient<IClientWebSocketWrapper, ClientWebSocketWrapper>();

#if DEBUG
        Action<string, bool> logger = (message, isError) =>
        {
            if (isError)
                MacroDeckLogger.Error(Main.Instance, "{0}", message);
            else
                MacroDeckLogger.Information(Main.Instance, "{0}", message);
        };

        services
            .AddSingleton<IDoricoCommsContext>(sp => new DoricoCommsContext(
                sp.GetRequiredService<IClientWebSocketWrapper>(),
                sp.GetRequiredService<IEventAggregator>(),
                logger))
            .AddTransient<IDoricoRemote>(sp => new DoricoRemote(
                sp.GetRequiredService<IDoricoCommsContext>(),
                logger));
#else
        services
            .AddSingleton<ILogger>(_ => NullLogger.Instance)
            .AddSingleton<IDoricoCommsContext>(sp => new DoricoCommsContext(
                sp.GetRequiredService<IClientWebSocketWrapper>(),
                sp.GetRequiredService<IEventAggregator>(),
                sp.GetRequiredService<ILogger>()))
            .AddTransient<IDoricoRemote>(sp => new DoricoRemote(
                sp.GetRequiredService<IDoricoCommsContext>(),
                sp.GetRequiredService<ILogger>()));
#endif

        _serviceProvider = services.BuildServiceProvider();
        _doricoRemote = _serviceProvider.GetService<IDoricoRemote>();
        _eventAggregator = _serviceProvider.GetService<IEventAggregator>();

        if (_eventAggregator != null)
        {
            _disconnectSubscription = _eventAggregator.Subscribe<DisconnectResponse>(OnDoricoDisconnected);
            _statusSubscription = _eventAggregator.Subscribe<StatusResponse>(OnDoricoStatusChanged);

            _dynamicReplacementWalker?.Dispose();
            _dynamicReplacementWalker = new DynamicReplacementWalker(this, _eventAggregator);
        }

        if (_doricoRemote != null)
        {
            _doricoRemote.Timeout = 60000; // 60 seconds
        }
    }

    public void InitializeDoricoRemote()
    {
        try
        {
            SetupDoricoServices();
            _ = ConnectToDoricoAsync();
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Failed to initialize Dorico remote: {0}", ex.Message);
        }
    }

    public async Task<bool> EnsureConnectedAsync()
    {
        if (IsConnected) return true;

        if (!await _connectSemaphore.WaitAsync(0))
        {
            // Another connection attempt is already in progress; wait for it to finish
            await _connectSemaphore.WaitAsync();
            try
            {
                return IsConnected;
            }
            finally
            {
                _connectSemaphore.Release();
            }
        }

        try
        {
            if (IsConnected) return true;

            SetupDoricoServices();
            await ConnectToDoricoAsync();
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "EnsureConnectedAsync failed: {0}", ex.Message);
        }
        finally
        {
            _connectSemaphore.Release();
        }

        return IsConnected;
    }

    public async Task ConnectToDoricoAsync()
    {
        try
        {
            MacroDeckLogger.Information(Main.Instance, "Dorico ConnectToDoricoAsync called");
            if (_doricoRemote == null) return;

            if (_doricoRemote.IsConnected)
            {
                MacroDeckLogger.Information(Main.Instance, "Dorico is already connected");
                return;
            }

            var savedToken = GetSavedSessionToken();

            var connectionArgs = !string.IsNullOrEmpty(savedToken)
                ? new ConnectionArguments(savedToken)
                : new ConnectionArguments();

            await _doricoRemote.ConnectAsync(ClientName, connectionArgs);
            await OnConnectedAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("kClientRejected_ConnectTokenRejected"))
        {
            MacroDeckLogger.Warning(Main.Instance, "Session token rejected. Clearing token and retrying with a new session.", Array.Empty<object>());
            PluginCredentials.SetCredentials(this, new Dictionary<string, string> { { SessionTokenKey, string.Empty } });

            try
            {
                await _doricoRemote!.ConnectAsync(ClientName, new ConnectionArguments());
                await OnConnectedAsync();
            }
            catch (Exception retryEx)
            {
                MacroDeckLogger.Error(Main.Instance, "Failed to connect to Dorico on retry: {0}", retryEx.Message);
                _contextService.UpdateConnectionVariable(IsConnected);
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Failed to connect to Dorico: {0}", ex.Message);
            _contextService.UpdateConnectionVariable(IsConnected);
        }
    }

    private async Task OnConnectedAsync()
    {
        if (_doricoRemote?.IsConnected != true) return;

        var newToken = _doricoRemote.SessionToken;
        if (!string.IsNullOrEmpty(newToken))
        {
            PluginCredentials.SetCredentials(this, new Dictionary<string, string> { { SessionTokenKey, newToken } });
        }

        MacroDeckLogger.Information(Main.Instance, "Successfully connected to Dorico");
        _contextService.UpdateConnectionVariable(IsConnected);
        await RefreshDoricoContextVariablesAsync();

        var appInfo = await _doricoRemote.GetAppInfoAsync();
        MacroDeckLogger.Information(Main.Instance, "Dorico Version: {0}", appInfo?.ToString() ?? "unknown");

        if (appInfo?.Number is string versionNumber && versionNumber.Length > 0)
        {
            var majorVersion = versionNumber.Split('.')[0];
            if (!string.IsNullOrEmpty(majorVersion))
            {
                _doricoVersion = majorVersion;
            }
        }

        try
        {
            var commands = await _doricoRemote.GetCommandsAsync();

            // Build the new list off to the side and assign it in one step;
            _availableCommands = commands
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            MacroDeckLogger.Information(Main.Instance, "Fetched {0} Dorico commands", _availableCommands.Count);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Failed to fetch Dorico commands: {0}", ex.Message);
        }
    }

    private string? GetSavedSessionToken()
    {
        var credentials = PluginCredentials.GetPluginCredentials(this);
        var entry = credentials?.FirstOrDefault(d => d.ContainsKey(SessionTokenKey));
        if (entry != null && entry.TryGetValue(SessionTokenKey, out var token) && !string.IsNullOrEmpty(token))
        {
            return token;
        }
        return null;
    }

    public async Task SendCommandAsync(string commandName, params CommandParameter[] parameters)
    {
        if (_doricoRemote?.IsConnected != true)
        {
            MacroDeckLogger.Warning(Main.Instance, "Dorico is not connected. Cannot send command.", Array.Empty<object>());
            return;
        }

        try
        {
            var command = new Command(commandName, parameters);
            await _doricoRemote.SendRequestAsync(command);

            MacroDeckLogger.Information(Main.Instance, "Command executed: {0}", commandName);
            await RefreshDoricoContextVariablesAsync();
        }
        catch (DoricoException<Response> ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Error sending command '{0}': {1}", commandName, ex.Message);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Unexpected error: {0}", ex.Message);
        }
    }

    public StatusResponse? GetCurrentStatus() => _doricoRemote?.CurrentStatus;

    private void OnDoricoDisconnected(DisconnectResponse evt)
    {
        MacroDeckLogger.Warning(Main.Instance, "Dorico disconnected (WebSocket closed).", Array.Empty<object>());
        _contextService.UpdateConnectionVariable(IsConnected);
        _contextService.SaveDisconnectedContextVariables(ref _flows_count);
    }

    private void OnDoricoStatusChanged(StatusResponse status)
    {
        // The event already contains the latest status, so save it immediately.
        _contextService.SaveStatusVariables(status);

        CancellationToken cancellationToken;

        lock (_statusDebounceLock)
        {
            _statusDebounceCts?.Cancel();
            _statusDebounceCts?.Dispose();

            _statusDebounceCts = new CancellationTokenSource();
            cancellationToken = _statusDebounceCts.Token;
        }

        // Debounce only the more expensive flow query.
        _ = RefreshDoricoContextVariablesDebouncedAsync(status, cancellationToken);
    }

    private async Task RefreshDoricoContextVariablesDebouncedAsync(
        StatusResponse status,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(StatusDebounceDelay, cancellationToken);

            await RefreshDoricoContextVariablesAsync(status, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // A newer status event restarted the debounce period.
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Debounced Dorico context refresh failed: {0}", ex.Message);
        }
    }

    public Task RefreshDoricoContextVariablesAsync(
        CancellationToken cancellationToken = default)
    {
        // Manual refresh: obtain a fresh status before updating variables.
        return RefreshDoricoContextVariablesAsync(status: null, cancellationToken);
    }

    private async Task RefreshDoricoContextVariablesAsync(
        StatusResponse? status,
        CancellationToken cancellationToken)
    {
        if (_doricoRemote?.IsConnected != true)
        {
            _contextService.SaveDisconnectedContextVariables(ref _flows_count);
            return;
        }

        bool semaphoreAcquired;

        try
        {
            semaphoreAcquired = await _contextVariableSemaphore.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!semaphoreAcquired)
        {
            return;
        }

        try
        {
            // Status events supply a status object. Only manual refreshes need
            // an additional GetStatusAsync request.
            if (status == null)
            {
                try
                {
                    status = await _doricoRemote
                        .GetStatusAsync()
                        .WaitAsync(DoricoRequestTimeout, cancellationToken);
                }
                catch (TimeoutException ex)
                {
                    MacroDeckLogger.Warning(Main.Instance, "Timed out while refreshing Dorico status: {0}", ex.Message);

                    status = _doricoRemote.CurrentStatus;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning(Main.Instance, "Could not refresh Dorico status: {0}", ex.Message);

                    status = _doricoRemote.CurrentStatus;
                }

                if (status != null)
                {
                    _contextService.SaveStatusVariables(status);
                }
            }

            try
            {
                await _contextService.SaveFlowVariablesAsync(_doricoRemote, status, count => _flows_count = count)
                    .WaitAsync(DoricoRequestTimeout, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "Timed out while refreshing flow variables: {0}", ex.Message);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // A newer status event superseded this refresh.
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Could not refresh Dorico context variables: {0}", ex.Message);
        }
        finally
        {
            _contextVariableSemaphore.Release();
        }
    }
}
