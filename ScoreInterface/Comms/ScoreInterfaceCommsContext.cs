using ScoreInterface.Exceptions;
using ScoreInterface.Json;
using ScoreInterface.Responses;
using Lea;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ScoreInterface.Comms;

/// <summary>
/// The outcome of a single request/response round trip with Dorico.
/// </summary>
/// <param name="Json">
/// The raw JSON of the matching response, or null if the socket closed before one arrived.
/// </param>
/// <param name="IsAborted">True if the request timed out or was canceled before a response arrived.</param>
/// <param name="ErrorCode">Dorico's response code when it reported an error (e.g. "kError"), otherwise null.</param>
/// <param name="ErrorDetail">Dorico's error detail text, if any.</param>
public sealed record CommsResult(string? Json = null, bool IsAborted = false, string? ErrorCode = null, string? ErrorDetail = null)
{
    public bool IsError => ErrorCode != null;
}

/// <summary>
/// Abstraction for communicating with Dorico over its WebSocket remote control API.
/// </summary>
public interface IScoreInterfaceCommsContext
{
    /// <summary>
    /// The state of the WebSocket connection with Dorico.
    /// </summary>
    WebSocketState State { get; }

    /// <summary>
    /// The most recent full status from Dorico.
    /// </summary>
    StatusResponse? CurrentStatus { get; }

    /// <summary>
    /// Raised for every message received from Dorico, exactly as Dorico sent it - before any
    /// attempt is made to match it to a pending request or to a known message type. This is what
    /// guarantees new fields or entirely new message types from a newer Dorico version are never
    /// silently lost, even if this build doesn't have a type to represent them yet.
    /// </summary>
    event Action<string>? RawMessageReceived;

    /// <summary>
    /// Opens the connection to Dorico and starts the receive loop.
    /// </summary>
    Task ConnectAsync(ConnectionArguments connectionArgs, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a request to Dorico and waits for the response whose "message" field equals
    /// <paramref name="expectedMessageId"/> (or a "response" message reporting an error).
    /// </summary>
    Task<CommsResult> SendAsync(string requestJson, string expectedMessageId, CancellationToken cancellationToken, int timeout);
}

public sealed class ScoreInterfaceCommsContext : IScoreInterfaceCommsContext
{
    private readonly IClientWebSocketWrapper _socket;
    private readonly IEventAggregator _eventAggregator;
    private readonly Action<string, bool>? _logger;
    private readonly ConcurrentQueue<PendingRequest> _pending = new();
    private readonly object _statusLock = new();

    private string _lastStatusJson = "{\"message\":\"status\"}";
    private volatile bool _receiveLoopRunning;

    public ScoreInterfaceCommsContext(IClientWebSocketWrapper socket, IEventAggregator eventAggregator, Action<string, bool>? logger = null)
    {
        _socket = socket;
        _eventAggregator = eventAggregator;
        _logger = logger;
    }

    public WebSocketState State => _socket.State;

    public StatusResponse? CurrentStatus { get; private set; }

    public event Action<string>? RawMessageReceived;

    public async Task ConnectAsync(ConnectionArguments connectionArgs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionArgs);

        if (State == WebSocketState.Open)
        {
            throw new ScoreInterfaceConnectedException();
        }

        while (_pending.TryDequeue(out _)) { }

        await _socket.ConnectAsync(new Uri(connectionArgs.Address), cancellationToken).ConfigureAwait(false);
        _logger?.Invoke("Connection opened", false);

        StartReceiveLoop();
    }

    public async Task<CommsResult> SendAsync(
        string requestJson,
        string expectedMessageId,
        CancellationToken cancellationToken,
        int timeout)
    {
        _socket.AssertSocketOpen();

        var pending = new PendingRequest(expectedMessageId);
        _pending.Enqueue(pending);

        try
        {
            var bytes = Encoding.UTF8.GetBytes(requestJson);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);

            _logger?.Invoke($"sent Request:\n{requestJson}", false);

            try
            {
                if (timeout >= 0)
                {
                    using var timeoutCts = new CancellationTokenSource(timeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    await pending.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                else
                {
                    await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.Invoke(
                    cancellationToken.IsCancellationRequested
                        ? $"Request '{expectedMessageId}' canceled."
                        : $"Request '{expectedMessageId}' timed out.",
                    false);

                pending.Abort();
            }
        }
        catch (Exception)
        {
            pending.Abort();
        }

        return await pending.Task.ConfigureAwait(false);
    }

    private void StartReceiveLoop()
    {
        if (State != WebSocketState.Open || _receiveLoopRunning) return;

        _receiveLoopRunning = true;

        _ = Task.Factory.StartNew(
            ReceiveLoopAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        var segment = new ArraySegment<byte>(buffer);

        try
        {
            while (_receiveLoopRunning)
            {
                var (result, content) = await ReceiveFullMessageAsync(segment).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    HandleDisconnect(result);
                    break;
                }

                if (content.Length > 0)
                {
                    RawMessageReceived?.Invoke(content);
                    HandleMessage(content);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Receive loop stopped: {ex.Message}", true);
        }
        finally
        {
            _receiveLoopRunning = false;
        }
    }

    private async Task<(WebSocketReceiveResult Result, string Content)> ReceiveFullMessageAsync(ArraySegment<byte> buffer)
    {
        WebSocketReceiveResult result;
        var sb = new StringBuilder();

        do
        {
            result = await _socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (result, string.Empty);
            }

            if (result.Count > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, result.Count));
            }
        }
        while (!result.EndOfMessage);

        return (result, sb.ToString());
    }

    private void HandleDisconnect(WebSocketReceiveResult result)
    {
        _logger?.Invoke($"Connection closed: {result.CloseStatusDescription}", false);

        while (_pending.TryDequeue(out var item))
        {
            if (!item.IsAborted)
            {
                item.Complete(new CommsResult());
            }
        }

        _ = _eventAggregator.PublishAsync(new DisconnectResponse(result.CloseStatus, result.CloseStatusDescription));
    }

    private void HandleMessage(string content)
    {
        string? message;

        try
        {
            using var doc = JsonDocument.Parse(content);
            message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            _logger?.Invoke($"Could not parse message:\n{content}", true);
            return;
        }

        if (message == null)
        {
            _logger?.Invoke($"Message is missing a 'message' field:\n{content}", true);
            return;
        }

        switch (message.ToLowerInvariant())
        {
            case "status":
                HandleStatusMessage(content);
                break;
            case "selectionchanged":
                HandleSelectionChangedMessage(content);
                break;
            case "response":
                HandleResponseMessage(content);
                break;
            default:
                // Any other message id (sessiontoken, version, commandlist, optionslist,
                // flowslist, propertieslist, ...) is always a direct reply to whatever is at the
                // head of the queue. Anything Dorico sends that we don't recognize at all still
                // reached RawMessageReceived above, so nothing is silently dropped - it's just
                // not matched to a pending request here.
                CompleteIfMatches(message, content);
                break;
        }
    }

    private void HandleStatusMessage(string content)
    {
        DrainAbortedHead();

        var isDirectResponse = _pending.TryPeek(out var head) &&
            string.Equals(head!.ExpectedMessageId, "status", StringComparison.OrdinalIgnoreCase);

        string effectiveJson;

        lock (_statusLock)
        {
            // An unprompted status message can be a partial patch containing only the fields that
            // changed. Merge it onto the last known full status so fields it omits keep their
            // previous value instead of resetting to the type's default.
            effectiveJson = isDirectResponse ? content : MergeStatusJson(_lastStatusJson, content);
            _lastStatusJson = effectiveJson;
        }

        StatusResponse? status;

        try
        {
            status = JsonSerializer.Deserialize<StatusResponse>(effectiveJson, ScoreInterfaceJsonOptions.Options);
        }
        catch (JsonException ex)
        {
            _logger?.Invoke($"Could not parse status message: {ex.Message}\n{content}", true);
            status = null;
        }

        if (status != null)
        {
            CurrentStatus = status;
            _eventAggregator.Publish(status);
        }

        if (isDirectResponse)
        {
            _pending.TryDequeue(out var request);
            request!.Complete(new CommsResult(content));
        }
    }

    private void HandleSelectionChangedMessage(string content)
    {
        try
        {
            var selection = JsonSerializer.Deserialize<SelectionChangedResponse>(content, ScoreInterfaceJsonOptions.Options);
            if (selection != null)
            {
                _eventAggregator.Publish(selection);
            }
        }
        catch (JsonException ex)
        {
            _logger?.Invoke($"Could not parse selectionchanged message: {ex.Message}\n{content}", true);
        }
    }

    private void HandleResponseMessage(string content)
    {
        DrainAbortedHead();

        string? code = null;
        string? detail = null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString();
            if (doc.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString();
        }
        catch (JsonException)
        {
            _logger?.Invoke($"Could not parse response message:\n{content}", true);
        }

        if (!_pending.TryDequeue(out var head))
        {
            _logger?.Invoke($"Unexpected response message:\n{content}", true);
            return;
        }

        head.Complete(string.Equals(code, "kError", StringComparison.OrdinalIgnoreCase)
            ? new CommsResult(content, ErrorCode: code, ErrorDetail: detail)
            : new CommsResult(content));
    }

    private void CompleteIfMatches(string messageId, string content)
    {
        DrainAbortedHead();

        if (_pending.TryPeek(out var head) &&
            string.Equals(head!.ExpectedMessageId, messageId, StringComparison.OrdinalIgnoreCase))
        {
            _pending.TryDequeue(out _);
            head.Complete(new CommsResult(content));
        }
        else
        {
            _logger?.Invoke($"Unexpected message received:\n{content}", true);
        }
    }

    private void DrainAbortedHead()
    {
        while (_pending.TryPeek(out var head) && head!.IsAborted)
        {
            _pending.TryDequeue(out _);
        }
    }

    private static string MergeStatusJson(string baseJson, string patchJson)
    {
        using var baseDoc = JsonDocument.Parse(baseJson);
        using var patchDoc = JsonDocument.Parse(patchJson);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var property in baseDoc.RootElement.EnumerateObject())
            {
                if (!patchDoc.RootElement.TryGetProperty(property.Name, out var newValue) ||
                    newValue.ValueKind == JsonValueKind.Null)
                {
                    property.WriteTo(writer);
                }
            }

            foreach (var property in patchDoc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null ||
                    !baseDoc.RootElement.TryGetProperty(property.Name, out _))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed class PendingRequest(string expectedMessageId)
    {
        private readonly TaskCompletionSource<CommsResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ExpectedMessageId { get; } = expectedMessageId;

        public bool IsAborted { get; private set; }

        public Task<CommsResult> Task => _tcs.Task;

        public void Complete(CommsResult result) => _tcs.TrySetResult(result);

        public void Abort()
        {
            IsAborted = true;
            _tcs.TrySetResult(new CommsResult(IsAborted: true));
        }
    }
}
