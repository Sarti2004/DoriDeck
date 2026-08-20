using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using ScoreInterface.Commands;
using ScoreInterface.Enums;
using System.Text.Json;

namespace DoriDeck.Actions;

public class RunCommandsAction : DoriDeckPluginAction
{
    public override string Name => "Command Sequence";
    public override string Description => "Runs a set of Dorico commands.";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new RunCommandsActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var commandsText = GetConfigValue<RunCommandsActionConfig>(c => c.Commands);
        bool applyToAllFlows = GetConfigBoolValue<RunCommandsActionConfig>(c => c.ApplyToAllFlows);

        if (string.IsNullOrWhiteSpace(commandsText))
        {
            MacroDeckLogger.Warning(Main.Instance, "Commands are not configured for this action button.", Array.Empty<object>());
            return;
        }

        try
        {
            commandsText = ResolveVariables(commandsText);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command Sequence variable resolution failed: {0}", ex.Message);

            return;
        }

        var commands = commandsText
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(command => command.Trim())
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .ToArray();

        if (commands.Length == 0)
        {
            MacroDeckLogger.Warning(Main.Instance, "No valid commands configured for this action button.", Array.Empty<object>());
            return;
        }

        if (applyToAllFlows && Main.Instance._flows_count > 1)
        {
            _ = ExecuteBatchAsync(commands);
        }
        else
        {
            _ = ExecuteAsync(commands);
        }
    }

    internal async Task ExecuteBatchAsync(string[] commandNames)
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
            _ = ExecuteAsync(commandNames);
        }
    }

    internal async Task ExecuteAsync(string[] commandNames)
    {
        var dorico = await GetConnectedRemoteAsync("command sequence execution");
        if (dorico == null) return;

        var originalMode = WindowMode.Undefined;
        var currentMode = originalMode;

        try
        {
            var originalStatus = await dorico.GetStatusAsync();
            originalMode = originalStatus?.WindowMode ?? WindowMode.Undefined;
            currentMode = originalMode;

            foreach (var commandName in commandNames)
            {
                try
                {
                    var commandNameParsed = commandName.Replace(",", "\\\\,");
                    var (actionName, parameters) = ParseCommand(commandNameParsed);

#if DEBUG
                    MacroDeckLogger.Warning(Main.Instance, "commandName: {0}", commandNameParsed);
#endif

                    var targetMode = GetTargetMode(actionName);

                    if (targetMode != WindowMode.Undefined && targetMode != currentMode)
                    {
                        await dorico.SendRequestAsync(new Command(
                            "Window.SwitchMode",
                            new CommandParameter("WindowMode", targetMode.ToString())
                        )).WaitAsync(TimeSpan.FromSeconds(2));

                        currentMode = targetMode;
                    }

                    var doricoCommand = new Command(actionName, parameters.ToArray());
                    await dorico.SendRequestAsync(doricoCommand).WaitAsync(TimeSpan.FromSeconds(2));
                    await Task.Delay(Main.Instance.TaskWaitDelay);
                }
                catch (TimeoutException ex)
                {
                    MacroDeckLogger.Error(Main.Instance, "Command timed out: {0}. {1}", commandName, ex.Message);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
                }
            }
        }
        finally
        {
            if (originalMode != WindowMode.Undefined &&
                currentMode != WindowMode.Undefined &&
                originalMode != currentMode)
            {
                await dorico.SendRequestAsync(new Command(
                    "Window.SwitchMode",
                    new CommandParameter("WindowMode", originalMode.ToString())
                ));
            }
        }
    }
}

public class RunCommandsActionConfig
{
    public string Commands { get; set; } = string.Empty;
    public bool ApplyToAllFlows { get; set; } = false;
}

public class RunCommandsActionConfigurator : ActionConfigControl
{
    private readonly RunCommandsAction _action;
    private readonly System.Windows.Forms.TextBox _commandsTextBox;
    private readonly System.Windows.Forms.CheckBox _applyToAllFlowsCheckBox;

    public RunCommandsActionConfigurator(RunCommandsAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        var label = new System.Windows.Forms.Label
        {
            Text = "Commands:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };

        _commandsTextBox = new System.Windows.Forms.TextBox
        {
            PlaceholderText = "Enter Dorico commands, one per line",
            AcceptsReturn = true,
            AcceptsTab = true,
            Multiline = true,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
            Width = 420,
            Height = 160,
            Top = label.Bottom+10,
            Left = 10
        };

        _applyToAllFlowsCheckBox = new System.Windows.Forms.CheckBox
        {
            Text = "Apply to all Flows",
            Top = _commandsTextBox.Bottom + 10,
            Left = _commandsTextBox.Left,
            AutoSize = true,
        };

        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<RunCommandsActionConfig>(action.Configuration);
                if (config != null)
                {
                    _commandsTextBox.Text = config.Commands;
                    _applyToAllFlowsCheckBox.Checked = config.ApplyToAllFlows;
                }
            }
            catch
            {
                // Ignore invalid configuration
            }
        }

        Controls.Add(label);
        Controls.Add(_commandsTextBox);

        var testButton = new System.Windows.Forms.Button
        {
            Text = "Test",
            Top = _commandsTextBox.Top,
            Left = _commandsTextBox.Right + 10,
            AutoSize = true
        };
        testButton.Click += async (_, _) =>
        {
            var commands = ParseCommandsText(_commandsTextBox.Text);
            if (commands.Length == 0)
                return;

            testButton.Enabled = false;
            try
            {
                await _action.ExecuteAsync(commands);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Test failed: {ex.Message}",
                    "Command Sequence – Test",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };
        Controls.Add(testButton);
        Controls.Add(_applyToAllFlowsCheckBox);
    }

    public override bool OnActionSave()
    {
        var config = new RunCommandsActionConfig
        {
            Commands = _commandsTextBox.Text.Trim(),
            ApplyToAllFlows = _applyToAllFlowsCheckBox.Checked
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }

    private static string[] ParseCommandsText(string text) =>
        text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToArray();
}
