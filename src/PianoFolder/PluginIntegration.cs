using DoriDeck.PianoFolder.Actions;
using DoriDeck.PianoFolder.Notes;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace DoriDeck.PianoFolder;

/// <summary>
/// Mostly copy/pasted from examples.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IFolderViewProvider, IUiProvider
{
    public const string FolderViewId = "piano-keyboard";

    private readonly ILogger _logger;
    private readonly INoteDispatcher _dispatcher;

    public PluginIntegration(ILogger logger, INoteDispatcher dispatcher)
    {
        _logger = logger.ForContext<PluginIntegration>();
        _dispatcher = dispatcher;
        Actions = [new PlayNoteAction(dispatcher)];
    }

    public IReadOnlyList<IActionDefinition> Actions { get; }

    public string ProviderName => "Piano Folder";

    private PianoKeyArtwork? _artwork;

    private IDeckNavigator? _deck;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cameFrom = new(StringComparer.Ordinal);

    public async Task InitializeAsync(IIntegrationContext context)
    {
        if (_deck is not null)
        {
            _deck.ClientChanged -= OnClientChanged;
        }

        _deck = context.Deck;
        _deck.ClientChanged += OnClientChanged;

        try
        {
            _artwork = await PianoKeyArtwork.RegisterAsync(context.UiResources);
        }
        catch (Exception exception) when (exception is UiResourceException or IOException)
        {
            _artwork = null;
            _logger.Warning(exception, "Images unavailable; use gradient keys.");
        }

        _logger.Information("Initialized.");
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    private static FolderViewDescriptor Descriptor { get; } = new(
        FolderViewId,
        Strings.FolderViews.PianoKeyboard.Name(),
        Strings.FolderViews.PianoKeyboard.Description(),
        HasConfiguration: true,Navigation: FolderViewNavigation.Hidden);

    private string? _qualifiedFolderViewId;

    public IReadOnlyList<FolderViewDescriptor> GetFolderViews() => [Descriptor];

    public async Task InitializeAsync(IFolderViewProviderContext context, CancellationToken cancellationToken = default)
    {
        var registration = await context.RegisterFolderViewAsync(Descriptor, cancellationToken);
        _qualifiedFolderViewId = registration.FolderViewId;
        _logger.Information(
            "Registered folder view {LocalId} as {QualifiedId}.",
            FolderViewId,
            _qualifiedFolderViewId);
    }

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Shared },
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Folder)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        request.Surface.Attributes.TryGetValue(UiFolderSurfaceAttributes.ViewId, out var viewIdElement);
        var viewId = viewIdElement.ValueKind == System.Text.Json.JsonValueKind.String ? viewIdElement.GetString() : null;
        var isOurs = viewId == FolderViewId || (viewId is not null && viewId == _qualifiedFolderViewId);

        _logger.Information(
            "Folder view session requested: viewId={ViewId}, matched={Matched} (local={LocalId}, qualified={QualifiedId}).",
            viewId,
            isOurs,
            FolderViewId,
            _qualifiedFolderViewId);

        if (!isOurs)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        request.Surface.Attributes.TryGetValue(UiFolderSurfaceAttributes.FolderId, out var folderIdElement);
        var folderId = folderIdElement.ValueKind == System.Text.Json.JsonValueKind.String ? folderIdElement.GetString() : null;
        Func<CancellationToken, Task>? goBack = _deck is { } deck && folderId is not null
            ? ct => GoBackAsync(deck, folderId, ct)
            : null;

        var model = new PianoKeyboardViewModel(_dispatcher, _artwork, goBack);
        if (_dispatcher.CurrentDuration is { } currentDuration)
        {
            model.Duration.Value = currentDuration;
        }

        // Keeps the note input row on the duration Dorico is using, including changes made in Dorico itself.
        void OnDurationChanged(NoteDuration duration) => model.Duration.Value = duration;
        _dispatcher.DurationChanged += OnDurationChanged;

        if (_dispatcher.NoteInputActive is { } noteInputActive)
        {
            model.NoteInputActive.Value = noteInputActive;
        }

        void OnNoteInputActiveChanged(bool active) => model.NoteInputActive.Value = active;
        _dispatcher.NoteInputActiveChanged += OnNoteInputActiveChanged;

        if (_dispatcher.RhythmDots is { } rhythmDots)
        {
            model.RhythmDots.Value = rhythmDots;
        }

        // Keeps the rhythm dots toggle on the value Dorico is using, including changes made in Dorico itself.
        void OnRhythmDotsChanged(int dots) => model.RhythmDots.Value = dots;
        _dispatcher.RhythmDotsChanged += OnRhythmDotsChanged;

        var view = new UiView(request.Surface, PianoKeyboardView.Build(model));
        return Task.FromResult<IUiSession?>(new PianoKeyboardSession(
            view,
            onDispose: () =>
            {
                _dispatcher.DurationChanged -= OnDurationChanged;
                _dispatcher.NoteInputActiveChanged -= OnNoteInputActiveChanged;
                _dispatcher.RhythmDotsChanged -= OnRhythmDotsChanged;
            }));
    }

    private void OnClientChanged(object? sender, DeckClientChangedEventArgs e)
    {
        if (e.PreviousFolderId is { Length: > 0 } previous &&
            !string.Equals(previous, e.Client.FolderId, StringComparison.Ordinal))
        {
            _cameFrom[e.Client.ClientId] = previous;
        }
    }

    /// <summary>
    /// Back button custom implementation, Macro decks own bakc button is covered , lazy to investigate;
    /// </summary>
    private async Task GoBackAsync(IDeckNavigator deck, string folderId, CancellationToken cancellationToken)
    {
        var clients = deck.GetClients();
        var viewers = clients
            .Where(client => string.Equals(client.FolderId, folderId, StringComparison.Ordinal))
            .ToList();

        if (viewers.Count == 0)
        {
            return;
        }

        foreach (var client in viewers)
        {
            if (_cameFrom.TryGetValue(client.ClientId, out var previous) &&
                !string.Equals(previous, folderId, StringComparison.Ordinal))
            {
                await deck.ChangeFolderAsync(previous, client.ClientId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await deck.GoToParentAsync(client.ClientId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
