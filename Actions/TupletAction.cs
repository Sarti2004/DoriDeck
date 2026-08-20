using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using ScoreInterface.Commands;
using DoriDeck.Services;
using ScoreInterface.Enums;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Forms;
using SuchByte.MacroDeck.Variables;

namespace DoriDeck.Actions;

public class TupletAction : DoriDeckPluginAction
{
    public override string Name => "Tuplet";
    public override string Description => "Create Tuplet of any ratio.";
    public override bool CanConfigure => true;
    public override string BindableVariable => Main.TupletModeVariableName;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new TupletActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = ExecuteAsync();  
    }

    internal async Task ExecuteAsync()
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        var ratio = GetConfigValue<TupletActionConfig>(c => c.Ratio);
MacroDeckLogger.Warning(Main.Instance, "TupletAction: in tuplet mode: {0}", ratio);

        var in_tuplet_mode = VariableManager.GetVariable(Main.Instance, Main.TupletModeVariableName).Value;
MacroDeckLogger.Warning(Main.Instance, "TupletAction: in tuplet mode: {0}", in_tuplet_mode);
           
        try
        {
            if (in_tuplet_mode == "True")
            {
                await dorico.SendRequestAsync(new Command("NoteInput.EndTupletRun"));
                VariableManager.SetValue(Main.TupletModeVariableName, false, VariableType.Bool, Main.Instance, Array.Empty<string>());
                return;
            }
            else
            {
                var commandParams = new List<CommandParameter>();
                ratio = ratio.Replace(",", "\\\\,");
                string command = "NoteInput.StartTupletRun";
                if (!string.IsNullOrEmpty(ratio))
                {
                    commandParams.Add(new CommandParameter("Definition", ratio));
                }

                await dorico.SendRequestAsync(new Command(command, commandParams.ToArray()));
                VariableManager.SetValue(Main.TupletModeVariableName, true, VariableType.Bool, Main.Instance, Array.Empty<string>());
            }
            
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
        }
    }
}


public class TupletActionConfig
{
    public string Ratio { get; set; } = "3";
}

public class TupletActionConfigurator : ActionConfigControl
{
    private readonly TupletAction _action;    
    private readonly TextBox _ratioTextBox;

    public TupletActionConfigurator(TupletAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        var label = new Label
        {
            Text = "Ratio:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };

        _ratioTextBox = new TextBox
        {
            Width = 350,
            Top = 10,
            Left = 100,
            Text = "3"
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<TupletActionConfig>(action.Configuration);
                if (config != null)
                {
                    _ratioTextBox.Text = config.Ratio;
                }
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "TupletAction: failed to load configuration: {0}", ex.Message);
            }
        }

        Controls.Add(label);
        Controls.Add(_ratioTextBox);
    }

    public override bool OnActionSave()
    {
        var config = new TupletActionConfig
        {
            Ratio = _ratioTextBox.Text.Trim(),
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }
}