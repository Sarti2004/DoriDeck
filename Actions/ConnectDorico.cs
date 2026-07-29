using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ActionButton;

namespace DoriDeck.Actions;

public class ConnectDorico : PluginAction
{
    public override string Name => "Dorico: Connect";
    public override string Description => "Connects to Dorico.";

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var dorico = Main.Instance.DoricoRemote;

        Main.Instance.InitializeDoricoRemote();
    }
}
