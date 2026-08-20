namespace ScoreInterface.Comms;

/// <summary>
/// Dorico connection information.
/// </summary>
/// <param name="SessionToken">Session token if previously connected to Dorico, otherwise null.</param>
/// <param name="Address">Address of Dorico's WebSocket.</param>
/// <param name="HandshakeVersion">Handshake version that Dorico is using.</param>
public sealed record ConnectionArguments(
    string? SessionToken = null,
    string Address = "ws://127.0.0.1:4560",
    string HandshakeVersion = "1.0");
