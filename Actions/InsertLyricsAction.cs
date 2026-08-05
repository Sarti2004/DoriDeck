using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Logging;
using DoriDeck.Services;
using WindowsInput;
using DoricoNet.Commands;

using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Diagnostics;


namespace DoriDeck.Actions;

public class InsertLyricsAction : DoriDeckPluginAction
{
    public override string Name => "Insert Lyrics";
    public override string Description => "Inserts lyrics into Dorico.";
    public override bool CanConfigure => true;

    // H.InputSimulator instance used for all keyboard injection.
    private readonly IKeyboardService _keyboard = new KeyboardService();

    private const int RetryCount = 5;
    private const int OperationDelay = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly LyricProcessor _lyricProcessor = new LyricProcessor();

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new InsertLyricsConfigurator(this, actionConfigurator);
    }

    public override void Trigger(string clientId, ActionButton actionButton)
    {
        var language = GetConfigValue<InsertLyricsActionConfig>(c => c.Language);
        var line = GetConfigValue<InsertLyricsActionConfig>(c => c.Verse);
        LyricsMode lyricsMode = Enum.Parse<LyricsMode>(GetConfigValue<InsertLyricsActionConfig>(c => c.LyricsMode.ToString()));

        try
        {
            string processedText = string.Empty;
            string? rawText = ClipboardSnapshot.ReadUnicodeText(RetryCount, RetryDelay)?.Trim();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return;
            }

            // Extract verse token if present, e.g., "(Verse 1)" or "(Chorus)"
            var match = Regex.Match(
                rawText,
                @"^\(Verse\s+(\d+)\)",
                RegexOptions.IgnoreCase);

            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var verseNumber))
            {
                line = verseNumber.ToString();
                rawText = rawText[match.Length..].TrimStart();
            }
            else if (rawText.StartsWith("(Chorus)", StringComparison.OrdinalIgnoreCase))
            {
                line = "Chorus";
                rawText = rawText.Substring("(Chorus)".Length);
            }

            // Sillabify the lyrics if the mode is SillabifyAndInsert or SillabifyOnly
            if (lyricsMode == LyricsMode.SillabifyAndInsert || lyricsMode == LyricsMode.SillabifyOnly)
            {
                processedText = _lyricProcessor.Process(rawText, language);
                MacroDeckLogger.Information(Main.Instance, "Insert Lyrics processed text: {0}", processedText);

                ClipboardSnapshot.WriteUnicodeText(processedText, RetryCount, RetryDelay);
            }
            else
            {
                processedText = rawText;
            }

            // Inert the lyrics into Dorico if the mode is SillabifyAndInsert or InsertOnly
            if (lyricsMode == LyricsMode.SillabifyAndInsert || lyricsMode == LyricsMode.InsertOnly)
            {
                _ = ExecuteAsync(processedText, line);
            }
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Insert Lyrics failed. Exception: {0}", ex.Message);
        }
    }

    private async Task ExecuteAsync(string text, string line)
    {
        try
        {
            var dorico = await GetConnectedRemoteAsync("command execution");
            if (dorico == null) return;

            /* var status = await dorico
                .GetStatusAsync(token)
                .WaitAsync(_options.DoricoRequestTimeout, token);
                */

            HasSelection();

            var visited = 0;
            var currentLyricLine = 1;

            text = text.Normalize(NormalizationForm.FormC);
            text = text.ReplaceLineEndings(" ");

            text = text
                .Replace('\u00A0', ' ')
                .Replace('\u202F', ' ')
                .Replace("+", " ")
                .Replace(@"\", @"\\\\")
                .Replace("&", @"\\&")
                .Replace(",", @"\\,");

            // bracketed text first then split by whitespace and hyphen
            string[] parts = Regex.Matches(text.Trim(), @"\([^)]*\)|[^\s-]+|-|\s")
                                .Select(match => match.Value)
                                .ToArray();

            if (parts.Length == 0)
            {
                throw new InvalidOperationException(
                    "No lyrics found in the clipboard.");
            }

            await dorico.SendRequestAsync(new Command("NoteInput.StartLyricInput"));

            //var properties = await dorico.GetPropertiesAsync();
            //MacroDeckLogger.Information(Main.Instance, "Properties: {0}", JsonSerializer.Serialize(properties));

            MacroDeckLogger.Information(Main.Instance, "text: {0};", text);

            if (int.TryParse(line, out var lyricLine) && lyricLine > 0)
            {
                var diff = lyricLine - currentLyricLine;
                var direction = diff > 0 ? "kIncrementVerseNumber" : "kDecrementVerseNumber";
                int count = Math.Abs(diff);
                for (int i = 0; i < count; i++)
                {
                    var entry = await ReadSelectedTextAsync();
                    await Task.Delay(20);
                    await dorico.SendRequestAsync(new Command("NoteInput.AcceptCurrentLyricInput?LyricText=" + entry + "&LyricsEntryAdvanceType=" + direction));
                    await Task.Delay(20);
                }
            }
            if (!string.IsNullOrWhiteSpace(line) && line.Equals("Chorus", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(20);
                await dorico.SendRequestAsync(new Command($"NoteInput.AcceptCurrentLyricInput?LyricsEntryAdvanceType=kDecrementVerseNumber"));
                await Task.Delay(20);
            }

            for (visited = 0; visited < parts.Length; visited++)
            {
                await Task.Delay(OperationDelay); // Small delay to ensure Dorico processes the previous command

                string advanceType;
                string lyricText = parts[visited];

                if (lyricText == "-" || string.IsNullOrWhiteSpace(lyricText))
                {
                    advanceType = GetAdvanceType(lyricText);
                    await dorico.SendRequestAsync(new Command("NoteInput.AcceptCurrentLyricInput?LyricText=&LyricsEntryAdvanceType=" + advanceType));
                    MacroDeckLogger.Information(Main.Instance, "extra {0}; advance type: {1}: {2}", visited, advanceType, lyricText);
                }
                else
                {
                    if (visited + 1 == parts.Length)
                    {
                        advanceType = GetAdvanceType(lyricText, true);
                    }
                    else
                    {
                        advanceType = GetAdvanceType(parts[visited + 1]);
                    }

                    if (!string.IsNullOrWhiteSpace(lyricText))
                    {
                        await dorico.SendRequestAsync(new Command("NoteInput.AcceptCurrentLyricInput",
                            new CommandParameter("LyricText", lyricText),
                            new CommandParameter("LyricsEntryAdvanceType", advanceType)
                        ));
                    }

                    visited++;
                }

                if (advanceType != "kEndInput")
                {
                    await Task.Delay(OperationDelay);
                    await dorico.SendRequestAsync(new Command("NoteInput.AdvanceLyricInput"));
                }
            }
        }
        catch (OperationCanceledException)
        {
            MacroDeckLogger.Warning(Main.Instance, "Lyrics insert cancelled.{0}", string.Empty);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(Main.Instance, "Lyrics insert failed: {0}", ex.Message);

        }
    }

    private async Task<string> ReadSelectedTextAsync()
    {

        // Retry Ctrl+A / Ctrl+C to handle transient focus or clipboard races.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // Select and copy the complete popover entry.
            _keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);

            var clipboardSequenceBeforeCopy =
                WindowAndClipboardInterop.GetClipboardSequence();

            _keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

            var copied = await WaitForClipboardChangeAsync(
                clipboardSequenceBeforeCopy,
                TimeSpan.FromMilliseconds(200));

            if (copied)
            {
                var text = ClipboardSnapshot.ReadUnicodeText(
                    2,
                    TimeSpan.FromMilliseconds(200));

                if (text is not null)
                {
                    return text;
                }
            }

            if (attempt < 2)
            {
                MacroDeckLogger.Warning(
                    Main.Instance,
                    "ReadSelectedTextAsync: Copy attempt {0} failed; retrying.",
                    attempt);
                await Task.Delay(20);
            }
        }

        throw new TimeoutException(
            "Dorico did not copy popover text before the timeout.");
    }

    private async Task<bool> WaitForClipboardChangeAsync(
        uint previousSequence,
        TimeSpan timeout
        )
    {
        var started = Stopwatch.StartNew();

        while (started.Elapsed < timeout)
        {

            if (WindowAndClipboardInterop.GetClipboardSequence() != previousSequence)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }


    private string GetAdvanceType(string text, bool isLastPart = false)
    {
        if (isLastPart)
        {
            return "kEndInput";
        }
        return (text == "-") ? "kHyphenateCurrentWord" : "kEndOrExtendCurrentWord";;
    }
}


public class InsertLyricsActionConfig
{
    public string Language { get; set; } = string.Empty;
    public string Verse { get; set; } = string.Empty;

    public LyricsMode LyricsMode { get; set; } = LyricsMode.SillabifyAndInsert;
}

public class InsertLyricsConfigurator : ActionConfigControl
{
    private readonly InsertLyricsAction _action;
    private readonly System.Windows.Forms.ComboBox _languageComboBox;
    private readonly System.Windows.Forms.ComboBox _verseComboBox;
    private readonly RadioButton _sillabifyAndInsertModeRadioButton;
    private readonly RadioButton _sillabifyOnlyModeRadioButton;
    private readonly RadioButton _insertOnlyModeRadioButton;

    public InsertLyricsConfigurator(InsertLyricsAction action, ActionConfigurator actionConfigurator)
    {
        _action = action;

        var modeGroupBox = new GroupBox
        {
            Text = "Mode",
            Top = 0,
            Left = 10,
            Width = 500,
            Height = 140,
            ForeColor = Color.White,
        };

        _sillabifyAndInsertModeRadioButton = new RadioButton
        {
            Text = "Syllabify and Insert",
            Left = 10,
            Top = 25,
            AutoSize = true
        };

        _sillabifyOnlyModeRadioButton = new RadioButton
        {
            Text = "Syllabify Only",
            Left = 10,
            Top = _sillabifyAndInsertModeRadioButton.Bottom + 10,
            AutoSize = true
        };

        _insertOnlyModeRadioButton = new RadioButton
        {
            Text = "Insert Only",
            Left = 10,
            Top = _sillabifyOnlyModeRadioButton.Bottom + 10,
            AutoSize = true
        };

        modeGroupBox.Controls.Add(_sillabifyAndInsertModeRadioButton);
        modeGroupBox.Controls.Add(_sillabifyOnlyModeRadioButton);
        modeGroupBox.Controls.Add(_insertOnlyModeRadioButton);

        _sillabifyAndInsertModeRadioButton.Checked = true;

        var languageLabel = new Label
        {
            Text = "Language:",
            Top = modeGroupBox.Bottom + 15,
            Left = 10,
            Width = 150
        };

        _languageComboBox = new System.Windows.Forms.ComboBox
        {
            Left = languageLabel.Right + 10,
            Top = modeGroupBox.Bottom + 10,
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _languageComboBox.Items.AddRange(new object[]
        {
            new dropDownOption
            {
                Value = "EN",
                Text = "English/Russian"
            },
            new dropDownOption
            {
                Value = "Latin",
                Text = "Latin"
            },
            new dropDownOption
            {

                Value = "FI",
                Text = "Finnish"
            },
            new dropDownOption
            {
                Value = "DE",
                Text = "German"

            }
        });

        _languageComboBox.SelectedIndex = 0;

        var verseLabel = new Label
        {
            Text = "Insert to:",
            Top = _languageComboBox.Bottom + 15,
            Left = 10,
            Width = 150
        };
        _verseComboBox = new System.Windows.Forms.ComboBox
        {
            Left = verseLabel.Right + 10,
            Top = _languageComboBox.Bottom + 10,
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _verseComboBox.Items.AddRange(new object[]
        {
            new dropDownOption
            {
                Value = "",
                Text = "Auto"
            },
            new dropDownOption
            {
                Value = "1",
                Text = "Verse 1"
            },
            new dropDownOption
            {
                Value = "2",
                Text = "Verse 2"
            },
            new dropDownOption
            {
                Value = "3",
                Text = "Verse 3"
            },
            new dropDownOption
            {
                Value = "4",
                Text = "Verse 4"
            },
            new dropDownOption
            {
                Value = "Chorus",
                Text = "Chorus"
            },
        });

        _verseComboBox.SelectedIndex = 0;

        // Load existing configuration
        if (!string.IsNullOrEmpty(action.Configuration))
        {
            try
            {
                var config =
                    JsonSerializer.Deserialize<InsertLyricsActionConfig>(
                        action.Configuration);

                if (config != null)
                {
                    var matchingOption = _languageComboBox.Items.OfType<dropDownOption>()
                        .FirstOrDefault(option => option.Value == config.Language);

                    _languageComboBox.SelectedItem = matchingOption ?? _languageComboBox.Items[0];

                    var matchingVerseOption = _verseComboBox.Items.OfType<dropDownOption>()
                        .FirstOrDefault(option => option.Value == config.Verse);

                    _verseComboBox.SelectedItem = matchingVerseOption ?? _verseComboBox.Items[0];

                    switch (config.LyricsMode)
                    {
                        case LyricsMode.InsertOnly:
                            _insertOnlyModeRadioButton.Checked = true;
                            break;

                        case LyricsMode.SillabifyOnly:
                            _sillabifyOnlyModeRadioButton.Checked = true;
                            break;

                        case LyricsMode.SillabifyAndInsert:
                        default:
                            _sillabifyAndInsertModeRadioButton.Checked = true;
                            break;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        Controls.Add(modeGroupBox);
        Controls.Add(languageLabel);
        Controls.Add(_languageComboBox);
        Controls.Add(verseLabel);
        Controls.Add(_verseComboBox);
    }

    public override bool OnActionSave()
    {
        var selectedLanguage =
            (_languageComboBox.SelectedItem as dropDownOption)?.Value ?? "EN";
        var selectedVerse =
            (_verseComboBox.SelectedItem as dropDownOption)?.Value ?? "";
        var config = new InsertLyricsActionConfig
        {
            Language = selectedLanguage,
            LyricsMode = GetSelectedMode(),
            Verse = selectedVerse
        };

        _action.Configuration = JsonSerializer.Serialize(config);
        return true;
    }

    private LyricsMode GetSelectedMode()
    {
        if (_sillabifyOnlyModeRadioButton.Checked)
            return LyricsMode.SillabifyOnly;

        if (_insertOnlyModeRadioButton.Checked)
            return LyricsMode.InsertOnly;

        return LyricsMode.SillabifyAndInsert;
    }

    public sealed class dropDownOption
    {
        public string Value { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;

        public override string ToString() => Text;
    }
}

public enum LyricsMode
{
    SillabifyAndInsert,
    SillabifyOnly,
    InsertOnly,
}
