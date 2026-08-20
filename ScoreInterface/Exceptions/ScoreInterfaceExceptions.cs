namespace ScoreInterface.Exceptions;

/// <summary>
/// Thrown when an error occurs communicating with Dorico.
/// </summary>
public class ScoreInterfaceException : Exception
{
    public ScoreInterfaceException(string message) : base(message)
    {
    }

    public ScoreInterfaceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when Dorico returns an error response ("code": "kError").
/// </summary>
/// <typeparam name="T">Type of the associated response, e.g. <see cref="Responses.Response"/>.</typeparam>
public class ScoreInterfaceException<T> : ScoreInterfaceException
{
    public T Response { get; }

    public ScoreInterfaceException(T response, string message) : base(message)
    {
        Response = response;
    }
}

/// <summary>
/// Thrown when an operation requires an open connection to Dorico, but there is none.
/// </summary>
public sealed class ScoreInterfaceNotConnectedException : Exception
{
    public ScoreInterfaceNotConnectedException() : base("ScoreInterfaceRemote is not connected to Dorico.")
    {
    }
}

/// <summary>
/// Thrown when an operation requires no connection to Dorico, but one is already open.
/// </summary>
public sealed class ScoreInterfaceConnectedException : Exception
{
    public ScoreInterfaceConnectedException() : base("ScoreInterfaceRemote is already connected to Dorico.")
    {
    }
}
