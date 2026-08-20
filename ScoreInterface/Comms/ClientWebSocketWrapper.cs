using System.Net.WebSockets;

namespace ScoreInterface.Comms;

/// <summary>
/// Minimal wrapper for <see cref="ClientWebSocket"/> to allow for dependency injection and testing.
/// </summary>
public interface IClientWebSocketWrapper
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

    Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);

    void AssertSocketOpen();
}

public sealed class ClientWebSocketWrapper : IClientWebSocketWrapper, IDisposable
{
    private ClientWebSocket? _socket;
    private bool _disposed;

    public WebSocketState State => _socket?.State ?? WebSocketState.None;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (State == WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is already connected.");
        }

        _socket = new ClientWebSocket();
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    public async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription)
    {
        AssertSocketOpen();

        await _socket!.CloseAsync(closeStatus, statusDescription, CancellationToken.None).ConfigureAwait(false);

        _socket.Dispose();
        _socket = null;
    }

    public Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        AssertSocketOpen();

        return _socket!.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        AssertSocketOpen();

        var result = await _socket!.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (result.MessageType == WebSocketMessageType.Close && _socket != null)
        {
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None)
                .ConfigureAwait(false);

            _socket.Dispose();
            _socket = null;
        }

        return result;
    }

    public void AssertSocketOpen()
    {
        if (State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"WebSocket connection is not open: {State}.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _socket?.Dispose();
    }
}
