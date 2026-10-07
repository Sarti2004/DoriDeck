using MacroDeck.Localization;
using DoriDeck.PianoFolder.Notes;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.PianoFolder.Actions;

/// <summary>
/// The one action this plugin declares. It is bindable directly to any Macro Deck button (its
/// <c>note</c> parameter is a fixed choice list built from <see cref="PianoKeyboardModel.AllPlayableKeys"/>),
/// and it is the same action every key of the piano keyboard folder view maps to - each key simply
/// supplies its own note id as the parameter value rather than each key needing its own action.
/// </summary>
public sealed class PlayNoteAction : IActionDefinition
{
    private const string NoteParameter = "note";

    private readonly INoteDispatcher _dispatcher;

    public PlayNoteAction(INoteDispatcher dispatcher) => _dispatcher = dispatcher;

    public string Id => "play-note";

    public LocalizedText Name => Strings.Actions.PlayNote.Name();

    public LocalizedText Description => Strings.Actions.PlayNote.Description();

    public IReadOnlyList<ActionParameter> Parameters { get; } =
    [
        ActionParameter.Choice(
            NoteParameter,
            options: BuildNoteOptions(),
            label: Strings.Actions.PlayNote.Note.Label(),
            description: Strings.Actions.PlayNote.Note.Description(),
            required: true),
    ];

    public MacroDeckPlatform Platforms => MacroDeckPlatform.All;

    public IActionExecutor CreateExecutor() => new Executor(_dispatcher);

    private static IReadOnlyList<ActionParameterOption> BuildNoteOptions() =>
        PianoKeyboardModel.AllPlayableKeys()
            .Select(key => new ActionParameterOption { Value = key.NoteId, Label = LocalizedText.FromLiteral(key.NoteId.ToUpperInvariant()) })
            .ToList();

    private sealed class Executor(INoteDispatcher dispatcher) : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            var note = context.Parameters.TryGetValue(NoteParameter, out var value)
                ? value?.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(note))
            {
                return Task.FromResult(ActionResult.Failed(
                    ActionErrorCodes.InvalidParameter,
                    MacroDeckStrings.Validation.Required(Strings.Actions.PlayNote.Note.Label())));
            }

            // A button bound directly to this action plays the note exactly as configured; transpose is a
            // folder-view-only convenience the keyboard applies before calling the dispatcher directly.
            return dispatcher.DispatchAsync(note, transposeSemitones: 0, context.CancellationToken);
        }
    }
}
