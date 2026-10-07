using DoriDeck.PianoFolder.Notes;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace DoriDeck.PianoFolder.Piano;

public sealed record PianoKeyArtwork(
    UiResource NaturalIdle,
    UiResource NaturalPressed,
    UiResource AccidentalIdle,
    UiResource AccidentalPressed,
    IReadOnlyDictionary<NoteDuration, UiResource> DurationIcons,
    IReadOnlyDictionary<NoteDuration, UiResource> DottedDurationIcons)
{
    private const string MediaType = "image/png";

    private static readonly IReadOnlyDictionary<NoteDuration, string> DurationFiles = new Dictionary<NoteDuration, string>
    {
        [NoteDuration.Whole] = "whole",
        [NoteDuration.Half] = "half",
        [NoteDuration.Quarter] = "quarter",
        [NoteDuration.Eighth] = "eighth",
        [NoteDuration.Sixteenth] = "sixteenth",
        [NoteDuration.ThirtySecond] = "thirty-second",
    };

    public static string AssetDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    public static async Task<PianoKeyArtwork> RegisterAsync(
        IUiResourceRegistry resources,
        CancellationToken cancellationToken = default)
    {
        async Task<UiResource> Register(string folder, string file, string name)
        {
            var content = await File.ReadAllBytesAsync(
                Path.Combine(AssetDirectory, folder, $"{file}.png"),
                cancellationToken).ConfigureAwait(false);
            return await resources.RegisterAsync(name, content, MediaType, cancellationToken)
                .ConfigureAwait(false);
        }

        Task<UiResource> Key(string name) => Register("Keys", name, name);

        var durationIcons = new Dictionary<NoteDuration, UiResource>();
        var dottedDurationIcons = new Dictionary<NoteDuration, UiResource>();
        foreach (var (duration, file) in DurationFiles)
        {
            durationIcons[duration] = await Register("Durations", file, $"duration-{file}").ConfigureAwait(false);
            dottedDurationIcons[duration] = await Register(
                "Durations", $"{file}_dotted", $"duration-{file}-dotted").ConfigureAwait(false);
        }

        return new PianoKeyArtwork(
            await Key("natural-idle").ConfigureAwait(false),
            await Key("natural-pressed").ConfigureAwait(false),
            await Key("accidental-idle").ConfigureAwait(false),
            await Key("accidental-pressed").ConfigureAwait(false),
            durationIcons,
            dottedDurationIcons);
    }
}
