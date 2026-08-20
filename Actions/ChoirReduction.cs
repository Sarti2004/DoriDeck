using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using ScoreInterface.Commands;
using ScoreInterface.Enums;
using DoriDeck.Services;
using WindowsInput;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ScoreInterface.Requests;
using ScoreInterface.Responses;
using ScoreInterface.DataStructures;

namespace DoriDeck.Actions;

public class ChoirReduction : DoriDeckPluginAction
{
    public override string Name => "Choir Reduction";
    public override string Description => "Create piano version of Choir score.";
    public override bool CanConfigure => true;

    private readonly IKeyboardService _keyboard = new KeyboardService();

    // Voices whose names start with any of these prefixes (case-insensitive) get stems up.
    private static readonly string[] DownStemPrefixes = ["alto", "bass"];
    private static readonly string[] UpperStaffPrefixes = ["soprano", "alto"];

    // Voices whose names start with "tenor" (case-insensitive) get an octave shift of -1.
    private static bool IsTenorVoice(string name) =>
        name.StartsWith("tenor", StringComparison.OrdinalIgnoreCase);

    private static bool IsStemsDown(string name) =>
        Array.Exists(DownStemPrefixes, p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    private static bool IsUpperStaff(string name) =>
        Array.Exists(UpperStaffPrefixes, p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new ChoirReductionActionConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        _ = ExecuteAsync();  
    }

    internal async Task ExecuteAsync()
    {
        var dorico = await GetConnectedRemoteAsync("command execution");
        if (dorico == null) return;

        ChoirReductionActionConfig cfg;
        try
        {
            cfg = string.IsNullOrEmpty(Configuration)
                ? new ChoirReductionActionConfig()
                : JsonSerializer.Deserialize<ChoirReductionActionConfig>(Configuration) ?? new ChoirReductionActionConfig();
        }
        catch
        {
            cfg = new ChoirReductionActionConfig();
        }
        var voices = cfg.Voices;

        try
        {
            // Reset settings to defaults
            await dorico.SetLayoutOptionsAsync([
                    new OptionValue("cueLayoutOptions.showCues", "false"),
                ]); 
            await Task.Delay(300);
            await dorico.SetNotationOptionsAsync([
                    new OptionValue("omitBarRestsBeneathCues", "false"),
                ]);
            await Task.Delay(300);

            await dorico.SetEngravingOptionsAsync([
                    new OptionValue("cueEngravingOptions.showLyrics", "false"),
                    new OptionValue("cueEngravingOptions.defaultCueClefValue", "1"),
                    new OptionValue("cueEngravingOptions.cueVisualScale", "1"),
                ]);
            await Task.Delay(300);

            for (int i = 0; i < voices.Count; i++)
            {
                var voice = voices[i];
                if (string.IsNullOrEmpty(voice.Name)) continue;

                await dorico.SendRequestAsync(new Command($"NoteInput.CreateCue?Definition={voice.Name}&UseLocalOverride=0"));
                await Task.Delay(200);

                var stemDir = IsStemsDown(voice.Name) ? "kForceStemsDown" : "kForceStemsUp";
                await dorico.SendRequestAsync(new Command($"UI.InvokePropertyChangeValue?Type=kCueVoiceDirection&Value={stemDir}"));
                await Task.Delay(100);

                if (IsTenorVoice(voice.Name))
                {
                    await dorico.SendRequestAsync(new Command("UI.InvokePropertyChangeValue?Type=kCueOctaveShift&Value=-1"));
                    await Task.Delay(100);
                }

                bool isLast = (i == voices.Count - 1);
                if (!isLast)
                {
                    _keyboard.PressChord(VirtualKeyCode.SHIFT, VirtualKeyCode.DOWN);
                    await Task.Delay(100);
                    _keyboard.Press(VirtualKeyCode.DOWN);
                    await Task.Delay(100);

                    var nextVoice = voices[i + 1];

                    if (i == 0 || IsUpperStaff(nextVoice.Name))
                    {
                        _keyboard.Press(VirtualKeyCode.UP);
                        await Task.Delay(100);
                    }
                }

            }

            // Enable cue options
            await dorico.SetLayoutOptionsAsync([
                    new OptionValue("cueLayoutOptions.showCues", "true"),
                ]); 
            await Task.Delay(300);
            await dorico.SetNotationOptionsAsync([
                    new OptionValue("omitBarRestsBeneathCues", "true"),
                ]);
            await Task.Delay(300);

        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Command failed: {0}", ex.Message);
        }
    }
}

public class VoiceConfig
{
    public string Name { get; set; } = "";
}

public class ChoirReductionActionConfig
{
    public List<VoiceConfig> Voices { get; set; } =
    [
        new VoiceConfig { Name = "Soprano" },
        new VoiceConfig { Name = "Alto"    },
        new VoiceConfig { Name = "Tenor"   },
        new VoiceConfig { Name = "Bass"    },
    ];
}

public class ChoirReductionActionConfigurator : ActionConfigControl
{
    private const int RowHeight = 32;
    private const int LabelLeft = 10;
    private const int NameLeft = 40;
    private const int NameWidth = 200;
    private const int RemoveLeft = 250;
    private const int TopStart = 10;

    private readonly ChoirReduction _action;
    private readonly Panel _voicesPanel;
    private readonly List<VoiceRow> _rows = [];

    private class VoiceRow
    {
        public Label RowLabel { get; set; } = null!;
        public TextBox NameBox { get; set; } = null!;
        public Button RemoveButton { get; set; } = null!;
    }

    public ChoirReductionActionConfigurator(ChoirReduction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        _voicesPanel = new Panel
        {
            Left = 0,
            Top = TopStart,
            Width = 360,
            AutoSize = true
        };
        Controls.Add(_voicesPanel);

        // Load configuration
        var config = new ChoirReductionActionConfig();
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ChoirReductionActionConfig>(action.Configuration);
                if (loaded != null)
                {
                    config = loaded;
                }
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(Main.Instance, "ChoirReductionAction: failed to load configuration: {0}", ex.Message);
            }
        }

        foreach (var v in config.Voices)
            AddRow(v);

        var addButton = new Button
        {
            Text = "＋ Add voice",
            AutoSize = true,
            Left = LabelLeft,
            Top = 0
        };
        addButton.Click += (s, e) =>
        {
            AddRow(new VoiceConfig());
            UpdateAddButtonPosition(addButton);
        };

        _voicesPanel.Layout += (s, e) => UpdateAddButtonPosition(addButton);
        Controls.Add(addButton);
        UpdateAddButtonPosition(addButton);
    }

    private void UpdateAddButtonPosition(Button addButton)
    {
        addButton.Top = _voicesPanel.Bottom + 8;
    }

    private void AddRow(VoiceConfig voice)
    {
        int top = _rows.Count * RowHeight;

        var nameBox = new TextBox
        {
            Text = voice.Name,
            Left = NameLeft,
            Top = top + 4,
            Width = NameWidth
        };

        var removeButton = new Button
        {
            Text = "✕",
            Left = RemoveLeft,
            Top = top + 3,
            Width = 30,
            Height = 24
        };

        var row = new VoiceRow
        {
            RowLabel = new Label
            {
                Text = $"{_rows.Count + 1}.",
                Left = LabelLeft,
                Top = top + 4,
                AutoSize = true
            },
            NameBox = nameBox,
            RemoveButton = removeButton
        };

        removeButton.Click += (s, e) => RemoveRow(row);

        _voicesPanel.Controls.Add(row.RowLabel);
        _voicesPanel.Controls.Add(nameBox);
        _voicesPanel.Controls.Add(removeButton);
        _rows.Add(row);

        _voicesPanel.Height = _rows.Count * RowHeight;
    }

    private void RemoveRow(VoiceRow row)
    {
        _voicesPanel.Controls.Remove(row.RowLabel);
        _voicesPanel.Controls.Remove(row.NameBox);
        _voicesPanel.Controls.Remove(row.RemoveButton);
        _rows.Remove(row);
        RebuildRowPositions();
    }

    private void RebuildRowPositions()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            int top = i * RowHeight;
            var r = _rows[i];
            r.RowLabel.Text = $"{i + 1}.";
            r.RowLabel.Top  = top + 4;
            r.NameBox.Top   = top + 4;
            r.RemoveButton.Top = top + 3;
        }
        _voicesPanel.Height = _rows.Count * RowHeight;
    }

    public override bool OnActionSave()
    {
        var config = new ChoirReductionActionConfig
        {
            Voices = _rows.Select(r => new VoiceConfig
            {
                Name = r.NameBox.Text.Trim()
            }).ToList()
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }
}
