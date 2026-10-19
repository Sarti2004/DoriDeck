using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.NoteDurationPad;


public static class NoteDurationPadStyle
{

    public const string Panel = "#3A3B3E";

    public const string FaceRim = "#5E6168";
    public const string Legend = "#E6E4DE";
    public const string Transparent = "#00000000";

    public const string LedAmber = "#FFB547";
    public const string LedRed = "#FF4B3E";

    /// Satin key cap, lit from above.
    public static readonly UiGradient Face =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#4C4F55" },
            new UiGradientStop { Offset = 0.50, Color = "#303237" },
            new UiGradientStop { Offset = 1.00, Color = "#1E1F23" });


    public static readonly UiGradient FaceLit =
        UiGradient.Linear(
            180,
            new UiGradientStop { Offset = 0.00, Color = "#5E6168" },
            new UiGradientStop { Offset = 0.50, Color = "#3B3D43" },
            new UiGradientStop { Offset = 1.00, Color = "#26272C" });
}
