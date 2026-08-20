namespace ScoreInterface.Commands;

/// <summary>
/// A single parameter name/value pair for a <see cref="Command"/>.
/// </summary>
public sealed record CommandParameter(string Name, string Value)
{
    public override string ToString() => $"{Name}={Value}";
}

/// <summary>
/// Instructs Dorico to execute a command.
/// </summary>
public sealed class Command
{
    public string Name { get; }

    public IReadOnlyList<CommandParameter> Parameters { get; }

    public Command(string name, params CommandParameter[] parameters)
    {
        Name = name;
        Parameters = parameters ?? [];
    }
}
