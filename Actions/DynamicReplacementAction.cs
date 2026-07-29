using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DoriDeck.Actions;

public class DynamicReplacementAction : DoriDeckPluginAction
{
    public override string Name => "Find/Replace";
    public override string Description => "Walks forward through dynamics/system text/playing techniques and replaces exact matches (e.g. mp → mf).";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new DynamicReplacementActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var find = GetConfigValue<DynamicReplacementActionConfig>(c => c.Find);
        var replace = GetConfigValue<DynamicReplacementActionConfig>(c => c.Replace);

        if (string.IsNullOrWhiteSpace(find) || string.IsNullOrWhiteSpace(replace))
        {
            MacroDeckLogger.Warning(
                Main.Instance,
                "DynamicReplacementAction: Find and Replace must both be configured.",
                Array.Empty<object>());
            return;
        }

        _ = ExecuteAsync(find, replace);
    }

    private async Task ExecuteAsync(string find, string replace)
    {
        try
        {
            var walker = Main.Instance.DynamicReplacementWalker;
            var result = await walker.RunAsync(find, replace);

            MacroDeckLogger.Information(
                Main.Instance,
                "Dynamic replacement complete. Visited: {0}; Changed: {1}; Stop reason: {2}",
                result.Visited,
                result.Changed,
                result.StopReason);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(
                Main.Instance,
                "Dynamic replacement failed: {0}",
                ex.Message);
        }
    }
}

public class DynamicReplacementActionConfig
{
    public string Find { get; set; } = string.Empty;
    public string Replace { get; set; } = string.Empty;
}

public class DynamicReplacementActionConfigurator : ActionConfigControl
{
    private readonly DynamicReplacementAction _action;
    private readonly TextBox _findTextBox;
    private readonly TextBox _replaceTextBox;

    public DynamicReplacementActionConfigurator(
        DynamicReplacementAction action,
        ActionConfigurator actionConfigurator)
    {
        _action = action;

        var findLabel = new Label
        {
            Text = "Find:",
            AutoSize = true,
            Top = 12,
            Left = 10
        };

        var replaceLabel = new Label
        {
            Text = "Replace with:",
            AutoSize = true,
            Left = 10
        };

        var labelWidth = Math.Max(
            findLabel.PreferredSize.Width,
            replaceLabel.PreferredSize.Width
        );

        labelWidth = Math.Max(labelWidth, 150); // Ensure a minimum width for better alignment

        var textBoxLeft = 10 + labelWidth + 8;

        _findTextBox = new TextBox
        {
            PlaceholderText = "e.g. mp",
            Top = 10,
            Left = textBoxLeft,
            Width = 200
        };

        replaceLabel.Top = _findTextBox.Bottom + 14;

        _replaceTextBox = new TextBox
        {
            PlaceholderText = "e.g. mf",
            Top = _findTextBox.Bottom + 12,
            Left = textBoxLeft,
            Width = 200
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<DynamicReplacementActionConfig>(action.Configuration);
                if (config != null)
                {
                    _findTextBox.Text = config.Find;
                    _replaceTextBox.Text = config.Replace;
                }
            }
            catch (JsonException ex)
            {
                MacroDeckLogger.Warning(
                    Main.Instance,
                    "DynamicReplacementAction: Failed to load configuration: {0}",
                    ex.Message);
            }
        }

        Controls.Add(findLabel);
        Controls.Add(_findTextBox);
        Controls.Add(replaceLabel);
        Controls.Add(_replaceTextBox);
    }

    public override bool OnActionSave()
    {
        var find = _findTextBox.Text.Trim();
        var replace = _replaceTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(find) || string.IsNullOrWhiteSpace(replace))
        {
            System.Windows.Forms.MessageBox.Show(
                "Both 'Find' and 'Replace with' fields must be filled in.",
                "Replace Dynamic",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        _action.Configuration = JsonSerializer.Serialize(new DynamicReplacementActionConfig
        {
            Find = find,
            Replace = replace
        });

        return true;
    }
}
