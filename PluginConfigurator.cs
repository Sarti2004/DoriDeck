using SuchByte.MacroDeck.GUI.CustomControls;
using System.Windows.Forms;

namespace DoriDeck;

public class PluginConfigurator : DialogForm
{
    private readonly TextBox _scriptPathTextBox;
    private readonly CheckBox _autoLoadScriptsCheckBox;

    private readonly Label _advancedToggleLabel;
    private readonly Panel _advancedPanel;
    private readonly NumericUpDown _flowSwitchDelayNumericUpDown;
    private readonly NumericUpDown _taskWaitDelayNumericUpDown;

    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    private bool _advancedExpanded = false;

    public PluginConfigurator()
    {
        Text = "DoriDeck Configuration";
        Width = 500;
        Height = 250;
        StartPosition = FormStartPosition.CenterParent;

        var label = new Label
        {
            Text = "Scripts Folder:",
            Top = 20,
            Left = 20,
            AutoSize = true
        };

        _scriptPathTextBox = new TextBox
        {
            Text = Main.Instance.ScriptPath,
            Top = 18,
            Left = 120,
            Width = 330
        };

        _autoLoadScriptsCheckBox = new CheckBox
        {
            Text = "Auto load scripts",
            Top = 55,
            Left = 20,
            AutoSize = true,
            Checked = Main.Instance.AutoLoadScripts
        };

        _advancedToggleLabel = new Label
        {
            Text = "► Advanced",
            Top = 90,
            Left = 20,
            AutoSize = true,
            Cursor = Cursors.Hand,
            ForeColor = System.Drawing.SystemColors.HotTrack
        };
        _advancedToggleLabel.Click += (_, _) => ToggleAdvancedSection();

        _advancedPanel = new Panel
        {
            Top = _advancedToggleLabel.Bottom + 5,
            Left = 20,
            Width = 440,
            Height = 60,
            Visible = false
        };

        var flowSwitchDelayLabel = new Label
        {
            Text = "Flow switch delay (ms):",
            Top = 5,
            Left = 0,
            AutoSize = true
        };

        _flowSwitchDelayNumericUpDown = new NumericUpDown
        {
            Top = 2,
            Left = 160,
            Width = 80,
            Minimum = 0,
            Maximum = 60000,
            Increment = 10,
            Value = Main.Instance.FlowSwitchDelay
        };

        var taskWaitDelayLabel = new Label
        {
            Text = "Task wait delay (ms):",
            Top = 35,
            Left = 0,
            AutoSize = true
        };

        _taskWaitDelayNumericUpDown = new NumericUpDown
        {
            Top = 32,
            Left = 160,
            Width = 80,
            Minimum = 0,
            Maximum = 60000,
            Increment = 10,
            Value = Main.Instance.TaskWaitDelay
        };

        _advancedPanel.Controls.Add(flowSwitchDelayLabel);
        _advancedPanel.Controls.Add(_flowSwitchDelayNumericUpDown);
        _advancedPanel.Controls.Add(taskWaitDelayLabel);
        _advancedPanel.Controls.Add(_taskWaitDelayNumericUpDown);

        _saveButton = new Button
        {
            Text = "Save",
            Left = 20,
            Width = 100
        };
        _saveButton.Click += (_, _) =>
        {
            Main.Instance.ScriptPath = _scriptPathTextBox.Text.Trim();
            Main.Instance.AutoLoadScripts = _autoLoadScriptsCheckBox.Checked;
            Main.Instance.FlowSwitchDelay = (int)_flowSwitchDelayNumericUpDown.Value;
            Main.Instance.TaskWaitDelay = (int)_taskWaitDelayNumericUpDown.Value;

            if (Main.Instance.AutoLoadScripts)
                Main.Instance.LoadDynamicScriptActions();

            DialogResult = DialogResult.OK;
            Close();
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Left = 130,
            Width = 100
        };
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(label);
        Controls.Add(_scriptPathTextBox);
        Controls.Add(_autoLoadScriptsCheckBox);
        Controls.Add(_advancedToggleLabel);
        Controls.Add(_advancedPanel);
        Controls.Add(_saveButton);
        Controls.Add(_cancelButton);

        UpdateLayout();
    }

    private void ToggleAdvancedSection()
    {
        _advancedExpanded = !_advancedExpanded;
        _advancedPanel.Visible = _advancedExpanded;
        _advancedToggleLabel.Text = _advancedExpanded ? "▼ Advanced" : "► Advanced";
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        var buttonsTop = (_advancedExpanded ? _advancedPanel.Bottom : _advancedToggleLabel.Bottom) + 15;

        _saveButton.Top = buttonsTop;
        _cancelButton.Top = buttonsTop;

        Height = buttonsTop + 90;
    }
}
