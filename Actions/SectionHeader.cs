using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using DoricoNet.Commands;
using DoricoNet.Enums;

using System;
using System.Text.Json;
using System.Windows.Forms;
using DoricoNet.Requests;
using DoricoNet.Responses;
using DoricoNet.DataStructures;

namespace DoriDeck.Actions;

public class SectionHeader : DoriDeckPluginAction
{
    public override string Name => "Rehearsal Mark";
    public override string Description => "Create Custom Rehearsal Mark.";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SectionHeaderActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = ExecuteAsync();  
    }

    internal async Task ExecuteAsync()
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        var text = GetConfigValue<SectionHeaderActionConfig>(c => c.Text);
        var newLine = GetConfigBoolValue<SectionHeaderActionConfig>(c => c.NewLine);


        OptionCollection? engravingOptions = null;

        //var newLineChar = newLine ? Environment.NewLine : string.Empty;
        var newLineChar = newLine ? @"\r\n" : " ";

        string command1 = "NoteInput.CreateRehearsalMark";
        string command2 = @"UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomPrefix&Value=";
        string command21 = @"UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomPrefix&Value=";
        string command3 = @"UI.InvokePropertyChangeValue?Type=kRehearsalMarkCustomSuffix&Value="+newLineChar+text;
        try
        {
            engravingOptions = await dorico.GetEngravingOptionsAsync();

            var enclosureType = engravingOptions?
                .FirstOrDefault(p => p.Path == "rehearsalMarkOptions.enclosureType")
                ?.CurrentValue;
            if (enclosureType != "kNone")
            {
                await dorico.SetEngravingOptionsAsync([
                        new OptionValue("rehearsalMarkOptions.enclosureType", "kNone"),
                        new OptionValue("rehearsalMarkOptions.noteRelativeHorizontalAlignment", "kLeft"),
                        new OptionValue("rehearsalMarkOptions.minimumDistanceFromStave", "5")
                    ]);
                await Task.Delay(20);
            }

            await dorico.SendRequestAsync(new Command(command1));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command(command21));
            await Task.Delay(20);
            await dorico.SendRequestAsync(new Command(command2));
            await Task.Delay(10);
            await dorico.SendRequestAsync(new Command(command3));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
        }
    }
}


public class SectionHeaderActionConfig
{
    public bool NewLine { get; set; } = true;
    public string Text { get; set; } = string.Empty;
}

public class SectionHeaderActionConfigurator : ActionConfigControl
{
    private readonly SectionHeader _action;    
    private readonly TextBox _textTextBox;
    private readonly CheckBox _newLineCheckBox;

    public SectionHeaderActionConfigurator(SectionHeader action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        var label = new Label
        {
            Text = "Text:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };

        _textTextBox = new TextBox
        {
            Width = 350,
            Top = 10,
            Left = 100
        };

        _newLineCheckBox = new CheckBox
        {
            Text = "New Line",
            Top = _textTextBox.Bottom + 10,
            Left = 10,
            AutoSize = true,
            Checked = true
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<SectionHeaderActionConfig>(action.Configuration);
                if (config != null)
                {
                    _textTextBox.Text = config.Text;
                    _newLineCheckBox.Checked = config.NewLine;
                }
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "SectionHeaderAction: failed to load configuration: {0}", ex.Message);
            }
        }

        Controls.Add(label);
        Controls.Add(_textTextBox);
        Controls.Add(_newLineCheckBox);
    }

    public override bool OnActionSave()
    {
        var config = new SectionHeaderActionConfig
        {
            Text = _textTextBox.Text.Trim(),
            NewLine = _newLineCheckBox.Checked
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }
}