using ScoreInterface.Comms;
using ScoreInterface.Commands;
using ScoreInterface.DataStructures;
using ScoreInterface.Exceptions;
using ScoreInterface.Json;
using ScoreInterface.Requests;
using ScoreInterface.Responses;
using System.Net.WebSockets;
using System.Text.Json;

namespace ScoreInterface;

/// <summary>
/// Interacts with the Dorico Remote Control API. Only the operations DoriDeck actually calls are
/// exposed here.
/// </summary>
public interface IScoreInterfaceRemote
{
    /// <summary>
    /// True if currently connected to Dorico, otherwise false.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Session token previously provided by Dorico. If valid, Dorico will accept a future
    /// connection with this token without prompting the user.
    /// </summary>
    string? SessionToken { get; }

    /// <summary>
    /// Default timeout for calls to Dorico, in milliseconds.
    /// </summary>
    int Timeout { get; set; }

    /// <summary>
    /// The most recent status information from Dorico.
    /// </summary>
    StatusResponse? CurrentStatus { get; }

    /// <summary>
    /// Raised for every raw JSON message Dorico sends, regardless of whether it maps to a type
    /// this build recognizes. Nothing Dorico sends is ever silently discarded.
    /// </summary>
    event Action<string>? RawMessageReceived;

    Task ConnectAsync(string clientName, ConnectionArguments connectionArguments, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<VersionResponse?> GetAppInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandInfo>> GetCommandsAsync(CancellationToken cancellationToken = default);

    Task<OptionCollection?> GetEngravingOptionsAsync(CancellationToken cancellationToken = default);

    Task<Response?> SetEngravingOptionsAsync(IEnumerable<OptionValue> optionValues, CancellationToken cancellationToken = default);

    Task<Response?> SetNotationOptionsAsync(IEnumerable<OptionValue> optionValues, IEnumerable<int>? flowIds = null, CancellationToken cancellationToken = default);

    Task<Response?> SetLayoutOptionsAsync(IEnumerable<OptionValue> optionValues, IEnumerable<int>? layoutIds = null, CancellationToken cancellationToken = default);

    Task<FlowsListResponse?> GetFlowsAsync(CancellationToken cancellationToken = default);

    Task<PropertiesListResponse?> GetPropertiesAsync(CancellationToken cancellationToken = default);

    Task<StatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<Response?> SendRequestAsync(Command command, CancellationToken cancellationToken = default);
}

public sealed class ScoreInterfaceRemote : IScoreInterfaceRemote
{
    private readonly IScoreInterfaceCommsContext _comms;
    private IReadOnlyList<CommandInfo>? _commands;

    public ScoreInterfaceRemote(IScoreInterfaceCommsContext commsContext, Action<string, bool>? logger = null)
    {
        _comms = commsContext;
        _ = logger; // logging happens inside IScoreInterfaceCommsContext; kept for constructor-signature symmetry.
    }

    public bool IsConnected => _comms.State == WebSocketState.Open;

    public string? SessionToken { get; private set; }

    public int Timeout { get; set; } = 30000;

    public StatusResponse? CurrentStatus => _comms.CurrentStatus;

    public event Action<string>? RawMessageReceived
    {
        add => _comms.RawMessageReceived += value;
        remove => _comms.RawMessageReceived -= value;
    }

    public async Task ConnectAsync(
        string clientName,
        ConnectionArguments connectionArguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientName);
        ArgumentNullException.ThrowIfNull(connectionArguments);

        if (IsConnected) return;

        try
        {
            if (_comms.State != WebSocketState.Open)
            {
                await _comms.ConnectAsync(connectionArguments, cancellationToken).ConfigureAwait(false);
            }

            if (_comms.State != WebSocketState.Open)
            {
                throw new ScoreInterfaceException("Could not connect to Dorico. Make sure Dorico is running.");
            }

            if (string.IsNullOrEmpty(connectionArguments.SessionToken))
            {
                SessionToken = await ConnectWithoutSessionTokenAsync(
                    clientName, connectionArguments.HandshakeVersion, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                SessionToken = connectionArguments.SessionToken;
                await ConnectWithSessionTokenAsync(
                    clientName, connectionArguments.SessionToken, connectionArguments.HandshakeVersion, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (ScoreInterfaceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScoreInterfaceException("Could not connect to Dorico. Make sure Dorico is running.", ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        // Dorico typically answers by closing the socket rather than sending a JSON reply; the
        // comms context resolves this call (without error) as soon as that happens.
        await _comms.SendAsync("{\"message\": \"disconnect\"}", "response", cancellationToken, Timeout)
            .ConfigureAwait(false);
    }

    public async Task<VersionResponse?> GetAppInfoAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var result = await _comms.SendAsync(
            "{\"message\": \"getappinfo\", \"info\": \"version\"}", "version", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        return Deserialize<VersionResponse>(result.Json);
    }

    public async Task<IReadOnlyList<CommandInfo>> GetCommandsAsync(CancellationToken cancellationToken = default)
    {
        if (_commands != null) return _commands;

        AssertConnected();

        var result = await _comms.SendAsync("{\"message\": \"getcommands\"}", "commandlist", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        var dto = Deserialize<CommandListDto>(result.Json);
        _commands = dto?.Commands ?? [];

        return _commands;
    }

    public async Task<OptionCollection?> GetEngravingOptionsAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var result = await _comms.SendAsync(
            "{\"message\": \"getoptions\", \"optionsType\": \"kEngraving\"}", "optionslist", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        var dto = Deserialize<OptionsListDto>(result.Json);
        if (dto == null) return null;

        var collection = new OptionCollection();
        if (dto.Options != null) collection.AddRange(dto.Options);

        return collection;
    }

    public async Task<Response?> SetEngravingOptionsAsync(
        IEnumerable<OptionValue> optionValues,
        CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var values = string.Join(',', optionValues.Select(v => $"{{\"path\": \"{v.Path}\", \"value\": \"{v.Value}\" }}"));
        var json = $"{{\"message\": \"setoptions\", \"optionsType\": \"kEngraving\", \"optionvalues\": [{values}]}}";

        var result = await _comms.SendAsync(json, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        return Deserialize<Response>(result.Json);
    }

    public async Task<Response?> SetNotationOptionsAsync(
        IEnumerable<OptionValue> optionValues,
        IEnumerable<int>? flowIds = null,
        CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var values = string.Join(',', optionValues.Select(v => $"{{\"path\": \"{v.Path}\", \"value\": \"{v.Value}\" }}"));
        var flowIdsPart = flowIds != null ? $", \"flowIDs\": [{string.Join(", ", flowIds.Select(id => $"\"{id}\""))}]" : string.Empty;
        var json = $"{{\"message\": \"setoptions\", \"optionsType\": \"kNotation\"{flowIdsPart}, \"optionvalues\": [{values}]}}";

        var result = await _comms.SendAsync(json, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        return Deserialize<Response>(result.Json);
    }

    public async Task<Response?> SetLayoutOptionsAsync(
        IEnumerable<OptionValue> optionValues,
        IEnumerable<int>? layoutIds = null,
        CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var values = string.Join(',', optionValues.Select(v => $"{{\"path\": \"{v.Path}\", \"value\": \"{v.Value}\" }}"));
        var layoutIdsPart = layoutIds != null ? $", \"layoutIDs\": [{string.Join(", ", layoutIds.Select(id => $"\"{id}\""))}]" : string.Empty;
        var json = $"{{\"message\": \"setoptions\", \"optionsType\": \"kLayout\"{layoutIdsPart}, \"optionvalues\": [{values}]}}";

        var result = await _comms.SendAsync(json, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        return Deserialize<Response>(result.Json);
    }

    public async Task<FlowsListResponse?> GetFlowsAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var result = await _comms.SendAsync("{\"message\": \"getflows\"}", "flowslist", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        return Deserialize<FlowsListResponse>(result.Json);
    }

    public async Task<PropertiesListResponse?> GetPropertiesAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var result = await _comms.SendAsync("{\"message\": \"getproperties\"}", "propertieslist", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        return Deserialize<PropertiesListResponse>(result.Json);
    }

    public async Task<StatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        AssertConnected();

        var result = await _comms.SendAsync("{\"message\": \"getstatus\"}", "status", cancellationToken, Timeout)
            .ConfigureAwait(false);

        ThrowIfError(result);

        return Deserialize<StatusResponse>(result.Json);
    }

    public async Task<Response?> SendRequestAsync(Command command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        AssertConnected();

        var parameters = string.Join(',', command.Parameters.Select(p => p.ToString()));
        var json = $"{{\"message\": \"command\",\"command\": \"{command.Name}?{parameters}\"}}";

        var result = await _comms.SendAsync(json, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        return Deserialize<Response>(result.Json);
    }

    private async Task<string> ConnectWithoutSessionTokenAsync(
        string clientName,
        string handshakeVersion,
        CancellationToken cancellationToken)
    {
        var connectJson =
            $"{{\"message\": \"connect\", \"clientName\": \"{clientName}\", \"handshakeVersion\": \"{handshakeVersion}\"}}";

        var result = await _comms.SendAsync(connectJson, "sessiontoken", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        var sessionToken = ExtractField(result.Json, "sessionToken");
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new InvalidOperationException("No sessionToken returned");
        }

        var acceptJson = $"{{ \"message\": \"acceptsessiontoken\", \"sessionToken\": \"{sessionToken}\"}}";
        var acceptResult = await _comms.SendAsync(acceptJson, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(acceptResult);

        return sessionToken;
    }

    private async Task ConnectWithSessionTokenAsync(
        string clientName,
        string sessionToken,
        string handshakeVersion,
        CancellationToken cancellationToken)
    {
        var connectJson =
            $"{{\"message\": \"connect\", \"clientName\": \"{clientName}\", \"handshakeVersion\": \"{handshakeVersion}\",\"sessionToken\":\"{sessionToken}\"}}";

        var result = await _comms.SendAsync(connectJson, "response", cancellationToken, Timeout).ConfigureAwait(false);
        ThrowIfError(result);

        var code = ExtractField(result.Json, "code");
        if (!string.Equals(code, "kConnected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unable to connect with sessionToken {sessionToken}");
        }
    }

    private void AssertConnected()
    {
        if (!IsConnected)
        {
            throw new ScoreInterfaceNotConnectedException();
        }
    }

    private static void ThrowIfError(CommsResult result)
    {
        if (result.IsAborted)
        {
            throw new ScoreInterfaceException("Request was canceled or timed out.");
        }

        if (result.IsError)
        {
            var response = new Response(result.ErrorCode!, result.ErrorDetail);
            throw new ScoreInterfaceException<Response>(
                response, $"Response code: {result.ErrorCode}, Detail: {result.ErrorDetail ?? "null"}");
        }
    }

    private static T? Deserialize<T>(string? json) =>
        json != null ? JsonSerializer.Deserialize<T>(json, ScoreInterfaceJsonOptions.Options) : default;

    private static string? ExtractField(string? json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CommandListDto(List<CommandInfo>? Commands);

    private sealed record OptionsListDto(List<ScoreInterface.Responses.OptionInfo>? Options);
}
