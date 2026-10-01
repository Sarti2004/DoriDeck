using MacroDeck.PianoFolder.Actions;
using MacroDeck.PianoFolder.Notes;
using MacroDeck.PianoFolder.Piano;
using DoriDeck;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace MacroDeck.PianoFolder;

/// <summary>
/// The plugin's one integration: declares the <c>play-note</c> action, registers the piano keyboard
/// folder view, and serves its UI session. Folder-view registration (<see cref="IFolderViewProvider"/>)
/// is deliberately separate from view composition (<see cref="Piano.PianoKeyboardView"/>) and from note
/// dispatch (<see cref="INoteDispatcher"/>): this class only wires the three together.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IFolderViewProvider, IUiProvider
{

    public const string FolderViewId = "piano-keyboard";

    private readonly ILogger _logger;
    private readonly INoteDispatcher _dispatcher;
    private IFolderViewProviderContext? _folderViewContext;

    public PluginIntegration(ILogger logger, INoteDispatcher dispatcher)
    {
        _logger = logger.ForContext<PluginIntegration>();
        _dispatcher = dispatcher;
        Actions = [new PlayNoteAction(dispatcher)];
    }

    public IReadOnlyList<IActionDefinition> Actions { get; }

    public string ProviderName => "Piano Folder";

    public Task InitializeAsync(IIntegrationContext context)
    {
        _logger.Information("Initialized.");
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync()
    {
        if (_folderViewContext is null || string.IsNullOrEmpty(_qualifiedFolderViewId))
        {
            return;
        }

        await _folderViewContext.UnregisterFolderViewAsync(_qualifiedFolderViewId);
        _folderViewContext = null;
        _qualifiedFolderViewId = null;
    }

    private static FolderViewDescriptor Descriptor { get; } = new(
        FolderViewId,
        Strings.FolderViews.PianoKeyboard.Name(),
        Strings.FolderViews.PianoKeyboard.Description(),
        HasConfiguration: true);


    private string? _qualifiedFolderViewId;

    /// <summary>Static declaration, so the folder view can be discovered without a running session.</summary>
    public IReadOnlyList<FolderViewDescriptor> GetFolderViews() => [Descriptor];

    /// <summary>Runtime registration, per the SDK's folder-view registration contract.</summary>
    public async Task InitializeAsync(IFolderViewProviderContext context, CancellationToken cancellationToken = default)
    {
        var registration = await context.RegisterFolderViewAsync(Descriptor, cancellationToken);
        _folderViewContext = context;
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

        var model = new PianoKeyboardViewModel(_dispatcher);
        var view = new UiView(request.Surface, PianoKeyboardView.Build(model));
        return Task.FromResult<IUiSession?>(new PianoKeyboardSession(view));
    }
}
