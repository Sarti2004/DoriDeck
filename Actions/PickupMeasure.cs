using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;
using DoricoNet.Commands;
using DoricoNet.Enums;

namespace DoriDeck.Actions;

public class PickupMeasure : DoriDeckPluginAction
{
    public override string Name => "Pickup Measure";
    public override string Description => "Create a pickup measure automatically.";

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = ExecuteAsync();  
    }

    internal async Task ExecuteAsync()
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        try
        {
            await dorico.SendRequestAsync(new Command("Edit.SelectToStartOfSystem"));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command("NoteInput.InsertScope?InsertModeScope=kGlobalCurrentBar"));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command("Filter.Rests"));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command("Edit.Delete"));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command("NoteInput.Mode"));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
        }
    }
}
