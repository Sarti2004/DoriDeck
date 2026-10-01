using MacroDeck.Localization;
using MacroDeck.PianoFolder.Notes;
using MacroDeck.PianoFolder.Piano;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using DoriDeck;

namespace MacroDeck.PianoFolder.Actions;

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
