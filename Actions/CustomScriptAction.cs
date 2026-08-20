using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using ScoreInterface.Commands;
using System.Text.Json;
namespace DoriDeck.Actions;

public class CustomScriptAction : DoriDeckPluginAction
{

    public override string Name => "Custom Script";
    public override string Description => "Runs inline Lua commands in Dorico.";
    public override bool CanConfigure => true;

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new CustomScriptActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var luaContent = GetConfigValue<CustomScriptActionConfig>(c => c.LuaScript);

        if (string.IsNullOrWhiteSpace(luaContent))
        {
            MacroDeckLogger.Warning(Main.Instance, "CustomScriptAction: no Lua script configured.", Array.Empty<object>());
            return;
        }

        _ = ExecuteAsync(luaContent);
    }

    internal async Task ExecuteAsync(string luaContent)
    {
        var dorico = await GetConnectedRemoteAsync("custom script execution");
        if (dorico == null) return;

        string resolvedLuaContent;


        try
        {
            resolvedLuaContent = ResolveVariables(luaContent);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(
                Main.Instance,
                "Custom Script variable resolution failed: {0}",
                ex.Message);

            return;
        }

        /* var scriptDir = Path.Combine(Main.Instance.ScriptPath, "DoriDeck");
        Directory.CreateDirectory(scriptDir);
        var tmpPath = Path.Combine(scriptDir, $"custom_{Guid.NewGuid():N}.lua"); */

        var tmpBase = Path.GetTempFileName();
        File.Delete(tmpBase);
        var tmpPath = Path.ChangeExtension(tmpBase, ".lua");

        try
        {
            await File.WriteAllTextAsync(tmpPath, resolvedLuaContent);
            var scriptPath = tmpPath.Replace("\\", "/");

            await dorico.SendRequestAsync(new Command("Script.RunScript", new CommandParameter("ScriptPath", scriptPath)));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Custom Script failed: {0}", ex.Message);
        }
        finally
        {
            try { File.Delete(tmpPath); }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "Custom Script: failed to delete temp file '{0}': {1}", tmpPath, ex.Message);
            }
        }
    }

}

public class CustomScriptActionConfig
{
    public string LuaScript { get; set; } = string.Empty;
}

public class CustomScriptActionConfigurator : ActionConfigControl
{
    private readonly CustomScriptAction _action;
    private readonly System.Windows.Forms.TextBox _luaScriptTextBox;

    public CustomScriptActionConfigurator(CustomScriptAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        var label = new System.Windows.Forms.Label
        {
            Text = "Lua Script:",
            Top = 12,
            Left = 10,
            AutoSize = true
        };

        _luaScriptTextBox = new System.Windows.Forms.TextBox
        {
            Multiline = true,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
            Width = 480,
            Height = 200,
            Top = label.Bottom + 6,
            Left = 10,
            Font = new System.Drawing.Font("Consolas", 9f),
            AcceptsReturn = true,
            AcceptsTab = true,
            Text = "local app=DoApp.DoApp()"
        };

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<CustomScriptActionConfig>(action.Configuration);
                if (config != null)
                    _luaScriptTextBox.Text = config.LuaScript;
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "CustomScriptAction: failed to load configuration: {0}", ex.Message);
            }
        }

        var testButton = new System.Windows.Forms.Button
        {
            Text = "Test",
            Top = _luaScriptTextBox.Bottom + 10,
            Left = 10,
            AutoSize = true
        };
        testButton.Click += async (_, _) =>
        {
            var content = _luaScriptTextBox.Text;
            if (string.IsNullOrWhiteSpace(content))
                return;

            testButton.Enabled = false;
            try
            {
                await _action.ExecuteAsync(content);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Test failed: {ex.Message}",
                    "Custom Script – Test",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };

        Controls.Add(label);
        Controls.Add(_luaScriptTextBox);
        Controls.Add(testButton);
    }

    public override bool OnActionSave()
    {
        var config = new CustomScriptActionConfig
        {
            LuaScript = _luaScriptTextBox.Text
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }
}
