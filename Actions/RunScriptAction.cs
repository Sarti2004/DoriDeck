using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using ScoreInterface.Commands;
using System.Text.Json;
using System.IO;

namespace DoriDeck.Actions;

public class RunScriptAction : DoriDeckPluginAction
{
    public override string Name => "Run Script";
    public override string Description => "Runs a Lua script in Dorico.";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new RunScriptActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var scriptName = GetConfigValue<RunScriptActionConfig>(c => c.ScriptName);
        bool applyToAllFlows = GetConfigBoolValue<RunScriptActionConfig>(c => c.ApplyToAllFlows);

        if (string.IsNullOrEmpty(scriptName))
        {
            MacroDeckLogger.Warning(Main.Instance, "ScriptName is not configured for this action button.", Array.Empty<object>());
            return;
        }

        if (applyToAllFlows && Main.Instance._flows_count > 1)
        {
            _ = ExecuteBatchAsync(scriptName);
        }
        else
        {
            _ = ExecuteAsync(scriptName);
        }
    }

    private async Task ExecuteBatchAsync(string scriptName)
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        for (int i = 0; i < Main.Instance._flows_count; i++)
        {
            await dorico.SendRequestAsync(
                new Command(
                    "Edit.GoToFlow",
                    new CommandParameter("FlowID", i.ToString())));
            await Task.Delay(Main.Instance.FlowSwitchDelay);

#if DEBUG
            MacroDeckLogger.Warning(Main.Instance, "FlowID: {0}", i);
#endif

            _ = ExecuteAsync(scriptName);
        }
    }

    internal async Task ExecuteAsync(string scriptName)
    {
        var scriptPath = Main.Instance.ScriptPath.Replace("\\", "/");

#if DEBUG
        MacroDeckLogger.Information(
            Main.Instance,
            "Triggering Run Script Action (Dorico connected: {0})",
            Main.Instance.IsConnected ? "true" : "false");
#endif

        var dorico = await GetConnectedRemoteAsync("script execution");
        if (dorico == null) return;

        try
        {
            await dorico.SendRequestAsync(new Command("Script.RunScript", new CommandParameter("ScriptPath", Path.Combine(scriptPath, scriptName + ".lua"))));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Run Script failed: {0}", ex.Message);
        }
    }
}

public class RunScriptActionConfig
{
    public string ScriptName { get; set; } = string.Empty;

    public bool ApplyToAllFlows { get; set; } = false;
}

public class RunScriptActionConfigurator : ActionConfigControl
{
    private readonly RunScriptAction _action;
    private readonly System.Windows.Forms.TextBox _scriptNameTextBox;
    private readonly System.Windows.Forms.CheckBox _applyToAllFlowsCheckBox;

    public RunScriptActionConfigurator(RunScriptAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        _scriptNameTextBox = new System.Windows.Forms.TextBox
        {
            PlaceholderText = "Script name",
            Width = 350,
            Top = 10,
            Left = 10
        };

        // Populate autocomplete list from the scripts folder (configuration time only, not stored)
        try
        {
            var scriptPath = Main.Instance.ScriptPath;
            if (Directory.Exists(scriptPath))
            {
                var autoComplete = new System.Windows.Forms.AutoCompleteStringCollection();
                foreach (var file in Directory.GetFiles(scriptPath, "*.lua", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(scriptPath, file);
                    var scriptName = Path.ChangeExtension(relativePath, null)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    autoComplete.Add(scriptName);
                }
                _scriptNameTextBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
                _scriptNameTextBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.CustomSource;
                _scriptNameTextBox.AutoCompleteCustomSource = autoComplete;
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning(Main.Instance, "Failed to populate script autocomplete: {0}", ex.Message);
        }

        _applyToAllFlowsCheckBox = new System.Windows.Forms.CheckBox
        {
            Text = "Apply to all Flows",
            Top = _scriptNameTextBox.Bottom + 10,
            Left = 10,
            AutoSize = true,
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<RunScriptActionConfig>(action.Configuration);
                if (config != null)
                {
                    _scriptNameTextBox.Text = config.ScriptName;
                    _applyToAllFlowsCheckBox.Checked = config.ApplyToAllFlows;
                }
            }
            catch { }
        }

        var label = new System.Windows.Forms.Label
        {
            Text = "Script Name:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };
        _scriptNameTextBox.Left = label.Right + 20;

        var testButton = new System.Windows.Forms.Button
        {
            Text = "Test",
            Top = _scriptNameTextBox.Top,
            Left = _scriptNameTextBox.Right + 10,
            AutoSize = true
        };
        testButton.Click += async (_, _) =>
        {
            var scriptName = _scriptNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(scriptName))
                return;

            testButton.Enabled = false;
            try
            {
                await _action.ExecuteAsync(scriptName);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Test failed: {ex.Message}",
                    "Run Script – Test",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };

        Controls.Add(label);
        Controls.Add(_scriptNameTextBox);
        Controls.Add(testButton);
        Controls.Add(_applyToAllFlowsCheckBox);
    }

    public override bool OnActionSave()
    {
        var config = new RunScriptActionConfig
        {
            ScriptName = _scriptNameTextBox.Text.Trim(),
            ApplyToAllFlows = _applyToAllFlowsCheckBox.Checked
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }
}
