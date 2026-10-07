using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.PianoFolder.Piano;

/// <summary>
/// Styles for keyboard.
/// </summary>
public static class PianoKeyStyle
{
    public const string InstrumentFrame = "#08080A";

    public const string KeyboardChassisBorder = "#343438";

    public const string NaturalIdle = "#F3EFE7";
    public const string NaturalPressed = "#D8D1C6";
    public const string NaturalBorder = "#AAA49A";

    public const string AccidentalIdle = "#1E2023";
    public const string AccidentalPressed = "#34363A";
    public const string AccidentalBorder = "#090A0C";

    public const string PanelBackground = "#111215";
    public const string PanelBorder = "#25272B";
    public const string PanelDivider = "#2C2E33";

    public const string ControlIdle = "#16171A";
    public const string ControlButton = "#1E2023";
    public const string ControlSelected = "#33363C";
    public const string ControlBorder = "#2E3035";
    public const string ControlText = "#ECE9E2";
    public const string ControlLabelText = "#B3B5BA";

    public const string DisplayBackground = "#000000";
    public const string DisplayBorder = "#1B1C1F";
    public const string DisplayText = "#8A8D94";

    /// <summary>
    /// Dark physical housing behind the keybed.
    /// </summary>
    public static readonly UiGradient KeyboardChassis =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#25282C" },
            new UiGradientStop { Offset = 0.30, Color = "#15171A" },
            new UiGradientStop { Offset = 1.00, Color = "#090A0C" });
}
