using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables;
using DoricoNet;
using DoricoNet.Enums;
using DoricoNet.Commands;
using DoricoNet.Responses;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DoriDeck.Actions;

public abstract class DoriDeckPluginAction : PluginAction
{
    protected static readonly Regex VariablePattern = new(
        @"\$\{(?<name>[a-zA-Z0-9_.\-]+)\}",
        RegexOptions.Compiled);
    /// <summary>
    /// Returns the connected <see cref="IDoricoRemote"/>, auto-connecting once if needed.
    /// Returns <c>null</c> when the connection cannot be established.
    /// </summary>
    protected async Task<IDoricoRemote?> GetConnectedRemoteAsync(string actionLabel)
    {
        var dorico = Main.Instance.DoricoRemote;

        if (dorico?.IsConnected != true)
        {
            MacroDeckLogger.Information(Main.Instance, "Not connected. Attempting auto-connect...", Array.Empty<object>());
            var connected = await Main.Instance.EnsureConnectedAsync();
            if (!connected)
            {
                MacroDeckLogger.Warning(Main.Instance, "Auto-connect failed. Skipping {0}.", actionLabel);
                return null;
            }
            dorico = Main.Instance.DoricoRemote;
        }

        return dorico;
    }

    protected void HasSelection()
    {
        var HasScore = VariableManager.GetVariable(
                Main.Instance,
                Main.HasScoreVariableName)?.Value?.ToString()?.Equals("True") ?? false;

        var HasSelection = VariableManager.GetVariable(
                Main.Instance,
                Main.HasSelectionVariableName)?.Value?.ToString()?.Equals("True") ?? false;

        if (!HasScore)
        {
            throw new InvalidOperationException(
                "Dorico does not have an active score.");
        }

        if (!HasSelection)
        {
            throw new InvalidOperationException(
                "Select the first note before running this action.");
        }
    }

    protected static (string actionName, List<CommandParameter> parameters) ParseCommand(string commandName)
    {
        var parts = commandName.Split('?', 2);

        string actionName = parts[0];

        var parameters = new List<CommandParameter>();

        if (parts.Length > 1)
        {
            foreach (var param in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = param.Split("=", 2);

                if (keyValue.Length == 2)
                {
                    parameters.Add(new CommandParameter(keyValue[0], keyValue[1]));
                }
            }
        }

        return (actionName, parameters);
    }  

    /// <summary>
    /// Deserializes the action's JSON <see cref="PluginAction.Configuration"/> and extracts a string value.
    /// Returns an empty string on any failure.
    /// </summary>
    protected string GetConfigValue<TConfig>(Func<TConfig, string?> selector)
    {
        if (string.IsNullOrEmpty(Configuration))
            return string.Empty;

        try
        {
            var config = JsonSerializer.Deserialize<TConfig>(Configuration);
            return config is null ? string.Empty : selector(config) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    protected bool GetConfigBoolValue<TConfig>(Func<TConfig, bool> selector)
    {
        if (string.IsNullOrEmpty(Configuration))
            return false;

        try
        {
            var config = JsonSerializer.Deserialize<TConfig>(Configuration);
            return config is null ? false : selector(config);
        }
        catch
        {
            return false;
        }
    }

    public static WindowMode GetTargetMode(string actionName)
    {
        if (actionName.StartsWith("Setup."))
        {
            return WindowMode.kSetupMode;
        }

        if (actionName.StartsWith("Project."))
        {
            return WindowMode.kSetupMode;
        }

        if (actionName.StartsWith("NoteInput.") || actionName.StartsWith("EventEdit."))
        {
            return WindowMode.kWriteMode;
        }

        if (actionName.StartsWith("Page."))
        {
            return WindowMode.kEngraveMode;
        }

        if (actionName.StartsWith("Print."))
        {
            return WindowMode.kPrintMode;
        }

        return WindowMode.Undefined;
    }

    protected static string ResolveVariables(string content)
    {
        return VariablePattern.Replace(content, match =>
        {
            var variableName = match.Groups["name"].Value;
            var variable = VariableManager.GetVariable(
                Main.Instance,
                variableName);

            if (variable == null)
            {
                MacroDeckLogger.Warning(
                    Main.Instance,
                    "Custom Script: variable '{0}' was not found.",
                    variableName);

                // Preserve unresolved placeholders.
                return match.Value;
            }

            return variable.Value?.ToString() ?? string.Empty;
        });
    }
}
