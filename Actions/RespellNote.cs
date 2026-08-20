using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;
using ScoreInterface.Commands;
using ScoreInterface.Enums;

namespace DoriDeck.Actions;

public class RespellNote : DoriDeckPluginAction
{
    public override string Name => "Respell Note";
    public override string Description => "Respell notes automatically.";

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = ExecuteAsync();  
    }

    internal async Task ExecuteAsync()
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        string commandName = "NoteInput.TransposeOrAddNotesToSelection?Definition=c#=db,db=c#,d#=eb,eb=d#,e#=f,f=e#,f#=gb,gb=f#,g#=ab,ab=g#,a#=bb,bb=a#,b#=c,c=b#,fb=e,e=fb,cb=b,b=cb";
        commandName = commandName.Replace(",", "\\\\,");

        try
        {
            var originalStatus = await dorico.GetStatusAsync();
            var originalMode = originalStatus?.WindowMode ?? WindowMode.Undefined;

            var targetMode = GetTargetMode(commandName);

            try
            {
                if (targetMode != WindowMode.Undefined && targetMode != originalMode)
                {
                    await dorico.SendRequestAsync(new Command(
                        "Window.SwitchMode",
                        new CommandParameter("WindowMode", targetMode.ToString())
                    ));
                }

                await dorico.SendRequestAsync(new Command(commandName));
            }
            finally
            {
                if (originalMode != WindowMode.Undefined &&
                    targetMode != WindowMode.Undefined &&
                    originalMode != targetMode)
                {
                    await dorico.SendRequestAsync(new Command(
                        "Window.SwitchMode",
                        new CommandParameter("WindowMode", originalMode.ToString())
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
        }
    }
}
