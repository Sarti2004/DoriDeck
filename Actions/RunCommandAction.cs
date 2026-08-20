using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using ScoreInterface.Commands;
using ScoreInterface.Enums;
using ScoreInterface.Responses;
using System.Text.Json;

namespace DoriDeck.Actions;

public class RunCommandAction : DoriDeckPluginAction
{
    public override string Name => "Run Command";
    public override string Description => "Runs a Dorico command.";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new RunCommandActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var commandName = GetConfigValue<RunCommandActionConfig>(c => c.CommandName);
        bool applyToAllFlows = GetConfigBoolValue<RunCommandActionConfig>(c => c.ApplyToAllFlows);

        if (string.IsNullOrEmpty(commandName))
        {
            MacroDeckLogger.Warning(Main.Instance, "CommandName is not configured for this action button.", Array.Empty<object>());
            return;
        }

        try
        {
            commandName = ResolveVariables(commandName);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command Sequence variable resolution failed: {0}", ex.Message);

            return;
        }


        if (applyToAllFlows && Main.Instance._flows_count > 1)
        {
            _ = ExecuteBatchAsync(commandName);
        }
        else
        {
            _ = ExecuteAsync(commandName);
        }
    }

    private async Task ExecuteBatchAsync(string commandName)
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
            _ = ExecuteAsync(commandName);
        }
    }

    internal async Task ExecuteAsync(string commandName)
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        try
        {
            commandName = commandName.Replace(",", "\\\\,");
            var (actionName, parameters) = ParseCommand(commandName);

#if DEBUG
            MacroDeckLogger.Warning(Main.Instance, "commandName: {0}", commandName);
#endif

            var originalStatus = await dorico.GetStatusAsync();
            var originalMode = originalStatus?.WindowMode ?? WindowMode.Undefined;

            var targetMode = GetTargetMode(actionName);

            try
            {
                if (targetMode != WindowMode.Undefined && targetMode != originalMode)
                {
                    await dorico.SendRequestAsync(new Command(
                        "Window.SwitchMode",
                        new CommandParameter("WindowMode", targetMode.ToString())
                    ));
                }

                var doricoCommand = new Command(actionName, parameters.ToArray());
                await dorico.SendRequestAsync(doricoCommand);
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

public class RunCommandActionConfig
{
    public string CommandName { get; set; } = string.Empty;
    public bool ApplyToAllFlows { get; set; } = false;
}

public class RunCommandActionConfigurator : ActionConfigControl
{
    private readonly RunCommandAction _action;
    private readonly System.Windows.Forms.TextBox _commandNameTextBox;
    private readonly System.Windows.Forms.CheckBox _applyToAllFlowsCheckBox;
    private readonly System.Windows.Forms.Label _parameterAdviceLabel;

    public RunCommandActionConfigurator(RunCommandAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        _commandNameTextBox = new System.Windows.Forms.TextBox
        {
            PlaceholderText = "Enter or search Dorico command",
            Width = 350,
            Top = 10,
            Left = 10
        };

        // Populate a searchable auto-complete source with the commands Dorico reported (IScoreInterfaceRemote.GetCommandsAsync)
        var availableCommands = Main.Instance.AvailableCommands;
        if (availableCommands.Count > 0)
        {
            var autoCompleteEntries = availableCommands
                .SelectMany(c => new[] { c.Name, c.DisplayName })
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var autoCompleteSource = new AutoCompleteStringCollection();
            autoCompleteSource.AddRange(autoCompleteEntries.ToArray());

            _commandNameTextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _commandNameTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _commandNameTextBox.AutoCompleteCustomSource = autoCompleteSource;
        }

        _applyToAllFlowsCheckBox = new System.Windows.Forms.CheckBox
        {
            Text = "Apply to all Flows",
            Top = _commandNameTextBox.Bottom + 10,
            Left = 10,
            AutoSize = true,
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<RunCommandActionConfig>(action.Configuration);
                if (config != null)
                {
                    _commandNameTextBox.Text = config.CommandName;
                    _applyToAllFlowsCheckBox.Checked = config.ApplyToAllFlows;
                }
            }
            catch { }
        }

        var label = new System.Windows.Forms.Label
        {
            Text = "Command:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };
        _commandNameTextBox.Left = label.Right + 10;

        _parameterAdviceLabel = new System.Windows.Forms.Label
        {
            Text = string.Empty,
            Top = _applyToAllFlowsCheckBox.Bottom + 10,
            Left = 10,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(500, 0)
        };

        _commandNameTextBox.TextChanged += (_, _) => UpdateParameterAdvice();
        UpdateParameterAdvice();

        Controls.Add(label);
        Controls.Add(_commandNameTextBox);
        Controls.Add(_applyToAllFlowsCheckBox);
        Controls.Add(_parameterAdviceLabel);

        var testButton = new System.Windows.Forms.Button
        {
            Text = "Test",
            Top = _commandNameTextBox.Top,
            Left = _commandNameTextBox.Right + 10,
            AutoSize = true
        };
        testButton.Click += async (_, _) =>
        {
            var commandName = _commandNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(commandName))
                return;

            testButton.Enabled = false;
            try
            {
                await _action.ExecuteAsync(commandName);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Test failed: {ex.Message}",
                    "Run Command – Test",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };
        Controls.Add(testButton);
    }

    public override bool OnActionSave()
    {
        var commandName = _commandNameTextBox.Text.Trim();

        // If the user searched/selected by DisplayName, resolve it to the internal Name
        // so the saved configuration always contains the command identifier Dorico expects.
        var matched = FindMatchingCommand(commandName);
        if (matched != null)
        {
            var commandNameWithoutParams = commandName.Split('?', 2)[0];
            var paramsSuffix = commandName.Contains('?') ? commandName[commandName.IndexOf('?')..] : string.Empty;

            // If the entered text matches the DisplayName (not the Name), replace it with
            // the internal Name while preserving any appended query-string parameters.
            if (!matched.Name.StartsWith(commandNameWithoutParams, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(matched.DisplayName) &&
                matched.DisplayName.StartsWith(commandNameWithoutParams, StringComparison.OrdinalIgnoreCase))
            {
                commandName = matched.Name + paramsSuffix;
            }
        }
        else
        {
            MacroDeckLogger.Warning(Main.Instance, "Command \"{0}\" was not found in Dorico's command list.", commandName);
        }

        var config = new RunCommandActionConfig
        {
            CommandName = commandName,
            ApplyToAllFlows = _applyToAllFlowsCheckBox.Checked
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }

    /// <summary>
    /// Updates the parameter advice label with the required/optional parameters of the
    /// command currently matching the text box, if any.
    /// </summary>
    private void UpdateParameterAdvice()
    {
        var commandInfo = FindMatchingCommand(_commandNameTextBox.Text.Trim());

        if (commandInfo == null)
        {
            _parameterAdviceLabel.Text = string.Empty;
            return;
        }

        var required = commandInfo.RequiredParameters?.ToArray() ?? Array.Empty<string>();
        var optional = commandInfo.OptionalParameters?.ToArray() ?? Array.Empty<string>();

        var lines = new List<string>
        {
            $"Matched command: {commandInfo.Name}" +
                (string.IsNullOrEmpty(commandInfo.DisplayName) ? "" : $" ({commandInfo.DisplayName})"),
            $"Required parameters: {(required.Length > 0 ? string.Join(", ", required) : "none")}",
            $"Optional parameters: {(optional.Length > 0 ? string.Join(", ", optional) : "none")}"
        };

        _parameterAdviceLabel.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Finds the Dorico command matching the entered text
    /// </summary>
    private static CommandInfo? FindMatchingCommand(string commandName)
    {
        if (string.IsNullOrEmpty(commandName)) return null;

        var availableCommands = Main.Instance.AvailableCommands;
        if (availableCommands.Count == 0) return null;

        var commandNameWithoutParams = commandName.Split('?', 2)[0];

        return availableCommands.FirstOrDefault(c =>
            c.Name.StartsWith(commandNameWithoutParams, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(c.DisplayName) &&
                c.DisplayName.StartsWith(commandNameWithoutParams, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(c.DisplayName) &&
                c.DisplayName.Contains(commandNameWithoutParams, StringComparison.OrdinalIgnoreCase))
                );
    }
}
