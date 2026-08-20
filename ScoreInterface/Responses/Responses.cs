using ScoreInterface.Enums;
using Lea;
using System.Net.WebSockets;

namespace ScoreInterface.Responses;

/// <summary>
/// A generic acknowledgement or error response. When <see cref="Code"/> is "kError", <see cref="Detail"/>
/// carries Dorico's specific error identifier (e.g. "kClientRejected_ConnectTokenRejected").
/// </summary>
public sealed record Response(string Code, string? Detail)
{
    public override string ToString() => $"Response: {Code} {Detail}";
}

/// <summary>
/// Details the current status of Dorico. Only the fields the plugin actually reads are declared
/// here; every other field Dorico sends is still available via
/// <see cref="IScoreInterfaceRemote.RawMessageReceived"/> without needing a matching property here.
/// </summary>
public sealed record StatusResponse : IEvent
{
    public int ActiveOpenScoreID { get; init; }

    public bool HasScore { get; init; }

    public bool HasSelection { get; init; }

    public NoteInputMode NoteInputMode { get; init; }

    public WindowMode WindowMode { get; init; }

    public Accidental Accidental { get; init; }
}

/// <summary>
/// Details on disconnecting from Dorico.
/// </summary>
public sealed record DisconnectResponse(WebSocketCloseStatus? CloseStatus, string? CloseStatusDescription) : IEvent;

/// <summary>
/// Notifies that the selection in a score has changed.
/// </summary>
public sealed record SelectionChangedResponse(int OpenScoreId) : IEvent;

/// <summary>
/// Details on an individual command Dorico accepts via a command message.
/// </summary>
public sealed record CommandInfo(
    string Name,
    string? DisplayName = null,
    IEnumerable<string>? RequiredParameters = null,
    IEnumerable<string>? OptionalParameters = null)
{
    public IEnumerable<string> RequiredParameters { get; } = RequiredParameters ?? [];

    public IEnumerable<string> OptionalParameters { get; } = OptionalParameters ?? [];

    public override string ToString() => DisplayName ?? Name;
}

/// <summary>
/// Version information about the connected instance of Dorico.
/// </summary>
public sealed record VersionResponse(string Variant, string Number)
{
    public override string ToString() => $"{Variant} {Number}";
}

/// <summary>
/// Details on an individual engraving option.
/// </summary>
public sealed record OptionInfo(string Path, string ValueType, string CurrentValue, IEnumerable<string>? EnumValues);

/// <summary>
/// Details on an individual flow.
/// </summary>
public sealed record Flow(int FlowID, string FlowName)
{
    public override string ToString() => FlowName;
}

/// <summary>
/// The list of flows in the active score.
/// </summary>
public sealed record FlowsListResponse(IEnumerable<Flow> Flows);

/// <summary>
/// Details about the properties which can be set on the current selection.
/// </summary>
public sealed record PropertiesListResponse(IEnumerable<string>? EventTypes);
