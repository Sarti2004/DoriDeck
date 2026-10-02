using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.PianoFolder.Piano;

/// Visual system for the virtual piano.  
/// Macro Deck UI modifier gradients used for now, need to be replaced with images later.
public static class PianoKeyStyle
{
    public const string InstrumentFrame = "#08080A";

    public const string KeyboardChassisBorder = "#343438";
    public const string NaturalBorder = "#AAA49A";
    public const string AccidentalBorder = "#090A0C";

    public const string ControlIdle = "#17181B";
    public const string ControlSelected = "#34363B";
    public const string ControlBorder = "#45474C";
    public const string ControlText = "#EDEAE2";
    public const string ControlLabelText = "#9A9CA3";

    public static readonly UiGradient KeyboardChassis =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#25282C" },
            new UiGradientStop { Offset = 0.30, Color = "#15171A" },
            new UiGradientStop { Offset = 1.00, Color = "#090A0C" });

    public static readonly UiGradient NaturalIdle =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#FFFDF8" },
            new UiGradientStop { Offset = 0.68, Color = "#F3EFE7" },
            new UiGradientStop { Offset = 0.90, Color = "#E7E0D5" },
            new UiGradientStop { Offset = 1.00, Color = "#D4CCBF" });

    public static readonly UiGradient NaturalPressed =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#E9E3D9" },
            new UiGradientStop { Offset = 0.72, Color = "#D8D1C6" },
            new UiGradientStop { Offset = 1.00, Color = "#BDB5A8" });

    public static readonly UiGradient AccidentalIdle =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#3B3E42" },
            new UiGradientStop { Offset = 0.20, Color = "#292B2F" },
            new UiGradientStop { Offset = 0.78, Color = "#17181B" },
            new UiGradientStop { Offset = 1.00, Color = "#0B0C0E" });

    public static readonly UiGradient AccidentalPressed =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#303236" },
            new UiGradientStop { Offset = 0.30, Color = "#292B2E" },
            new UiGradientStop { Offset = 1.00, Color = "#17181A" });
}
