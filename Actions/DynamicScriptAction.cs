using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;

namespace DoriDeck.Actions;

public class DynamicScriptAction : RunScriptAction
{
    //private readonly string _scriptName;
    public string ScriptName { get; set; } = string.Empty;

    public override string Name => $"Script - {ScriptName}";
    public override string Description => $"Runs the Lua script '{ScriptName}' in Dorico.";
    public override bool CanConfigure => false;


    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var config = new RunScriptActionConfig
        {
            ScriptName = ScriptName
        };

        if (string.IsNullOrEmpty(ScriptName))
        {
            MacroDeckLogger.Warning(Main.Instance, "ScriptName is not configured for this action button.", Array.Empty<object>());
            return;
        }

        _ = ExecuteAsync(ScriptName);
    }

}
