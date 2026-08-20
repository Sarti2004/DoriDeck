namespace ScoreInterface.Requests;

/// <summary>
/// An option path and the value to set it to.
/// </summary>
public sealed record OptionValue(string Path, string Value);
