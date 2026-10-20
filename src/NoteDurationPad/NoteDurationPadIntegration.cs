using DoriDeck.PianoFolder.Notes;
using DoriDeck.PianoFolder.Piano;
using DoriDeck.Services;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using ScoreInterface.Commands;
using Serilog;

namespace DoriDeck.NoteDurationPad;


public sealed class NoteDurationPadIntegration : IPluginIntegration, IFolderViewProvider, IUiProvider
{
    public const string FolderViewId = "note-duration-pad";

    private static readonly TimeSpan ConnectWaitLimit = TimeSpan.FromSeconds(2);

    private readonly DoricoSession _session;
    private readonly INoteDispatcher _dispatcher;
    private readonly IKeyboardService _keyboard;
    private readonly IApplicationFocusService _applicationFocus;
    private readonly ILogger _logger;
    private string? _qualifiedFolderViewId;
    private IReadOnlyDictionary<NoteDuration, UiResource> _durationIcons = new Dictionary<NoteDuration, UiResource>();
    private IReadOnlyDictionary<NoteDuration, UiResource> _dottedDurationIcons = new Dictionary<NoteDuration, UiResource>();
    private UiResource? _background;
    private UiResource? _noteInputIcon;

    public NoteDurationPadIntegration(
        DoricoSession session,
        INoteDispatcher dispatcher,
        IKeyboardService keyboard,
        IApplicationFocusService applicationFocus,
        ILogger logger)
    {
        _session = session;
        _dispatcher = dispatcher;
        _keyboard = keyboard;
        _applicationFocus = applicationFocus;
        _logger = logger.ForContext<NoteDurationPadIntegration>();
    }

    public IReadOnlyList<IActionDefinition> Actions { get; } = [];

    public string ProviderName => "Note Duration Pad";

    public async Task InitializeAsync(IIntegrationContext context)
    {
        try
        {
            (_durationIcons, _dottedDurationIcons) = await PianoKeyArtwork.RegisterDurationIconsAsync(context.UiResources);
        }
        catch (Exception)
        {
        }

        try
        {
            var background = await File.ReadAllBytesAsync(
                Path.Combine(PianoKeyArtwork.AssetDirectory, "Backgrounds", "brushed-metal.jpg"));
            _background = await context.UiResources.RegisterAsync("note-pad-background", background, "image/jpeg", CancellationToken.None);
        }
        catch (Exception exception) when (exception is UiResourceException or IOException)
        {
            _logger.Warning(exception, "Background image unavailable; the pad uses a plain panel.");
        }

        try
        {
            var noteInputIcon = await File.ReadAllBytesAsync(
                Path.Combine(PianoKeyArtwork.AssetDirectory, "Durations", "8th plus.png"));
            _noteInputIcon = await context.UiResources.RegisterAsync("note-pad-note-input", noteInputIcon, "image/png", CancellationToken.None);
        }
        catch (Exception exception) when (exception is UiResourceException or IOException)
        {
            _logger.Warning(exception, "Note input icon unavailable; the pad shows a music note.");
        }

        _logger.Information("Initialized.");
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    private static FolderViewDescriptor Descriptor { get; } = new(
        FolderViewId,
        Strings.FolderViews.NoteDurationPad.Name(),
        Strings.FolderViews.NoteDurationPad.Description());

    public IReadOnlyList<FolderViewDescriptor> GetFolderViews() => [Descriptor];

    public async Task InitializeAsync(IFolderViewProviderContext context, CancellationToken cancellationToken = default)
    {
        var registration = await context.RegisterFolderViewAsync(Descriptor, cancellationToken);
        _qualifiedFolderViewId = registration.FolderViewId;
        _logger.Information("Registered folder view {LocalId} as {QualifiedId}.", FolderViewId, _qualifiedFolderViewId);
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
        if (viewId != FolderViewId && (viewId is null || viewId != _qualifiedFolderViewId))
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var model = new NoteDurationPadViewModel
        {
            Dispatcher = _dispatcher,
            DurationIcons = _durationIcons,
            DottedDurationIcons = _dottedDurationIcons,
            Background = _background,
            NoteInputIcon = _noteInputIcon,
            Delete = ct => SendCommandAsync("Edit.Delete", ct),
            Play = ct => SendCommandAsync("Play.StartOrStop", ct),
            PlayFromSelected = ct => SendCommandAsync("Play.StartOrStop?PlayFromLocation=kSelection", ct),
            Record = ct => SendCommandAsync("Play.Record", ct),
            EnterNoteInput = ct => SendCommandAsync("NoteInput.Enter", ct),
            Up = _ => PressKeyAsync(DoriDeckKey.UpArrow),
            Down = _ => PressKeyAsync(DoriDeckKey.DownArrow),
            Left = ct => SendCommandAsync("EventEdit.NavigateLeft", ct),
            Right = ct => SendCommandAsync("EventEdit.NavigateRight", ct),
            LeftBar = ct => SendCommandAsync("EventEdit.NavigateLeftBar", ct),
            RightBar = ct => SendCommandAsync("EventEdit.NavigateRightBar", ct),
        };

        if (_dispatcher.CurrentDuration is { } currentDuration)
        {
            model.Duration.Value = currentDuration;
        }

        if (_dispatcher.RhythmDots is { } rhythmDots)
        {
            model.RhythmDots.Value = rhythmDots;
        }

        // Keeps the pad on the duration and dots Dorico is using, including changes made in Dorico itself.
        void OnDurationChanged(NoteDuration duration) => model.Duration.Value = duration;
        void OnRhythmDotsChanged(int dots) => model.RhythmDots.Value = dots;
        _dispatcher.DurationChanged += OnDurationChanged;
        _dispatcher.RhythmDotsChanged += OnRhythmDotsChanged;

        // Play and Rec show a stop square while Dorico is playing or recording.
        void OnDisplayStateChanged()
        {
            model.InPlayback.Value = _session.InPlayback;
            model.IsRecording.Value = _session.IsRecording;
        }

        OnDisplayStateChanged();
        _session.DisplayStateChanged += OnDisplayStateChanged;

        // The note input key lights up while Dorico is in note input.
        void OnNoteInputActiveChanged(bool active) => model.NoteInputActive.Value = active;
        model.NoteInputActive.Value = _session.NoteInputActive;
        _session.NoteInputActiveChanged += OnNoteInputActiveChanged;

        var view = new UiView(request.Surface, NoteDurationPadView.Build(model));
        return Task.FromResult<IUiSession?>(new PianoKeyboardSession(
            view,
            onDispose: () =>
            {
                _dispatcher.DurationChanged -= OnDurationChanged;
                _dispatcher.RhythmDotsChanged -= OnRhythmDotsChanged;
                _session.DisplayStateChanged -= OnDisplayStateChanged;
                _session.NoteInputActiveChanged -= OnNoteInputActiveChanged;
            }));
    }

    private async Task SendCommandAsync(string commandName, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _session.EnsureConnectedAsync().WaitAsync(ConnectWaitLimit, cancellationToken))
            {
                return;
            }
        }
        catch (TimeoutException)
        {
            return;
        }

        await _session.Remote.SendRequestAsync(new Command(commandName), cancellationToken);
    }

    private Task PressKeyAsync(DoriDeckKey key)
    {
        if (_applicationFocus.IsForeground("Dorico"))
        {
            _keyboard.Press(key);
        }

        return Task.CompletedTask;
    }
}
