using System.Text.Json;
using DoriDeck.Actions;
using DoriDeck.PianoFolder.Notes;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using ScoreInterface.Commands;
using Serilog;

namespace DoriDeck.ScoreWidget;

public sealed class ScoreWidgetIntegration : IPluginIntegration, IWidgetTypeProvider, IUiProvider
{
	public const string WidgetTypeId = "score-info";

	private const string PreviewResourceName = "score-preview";
	private const string PreviewMediaType = "image/png";

	private static readonly TimeSpan ConnectWaitLimit = TimeSpan.FromSeconds(2);

	private readonly DoricoSession _session;
	private readonly ILogger _logger;
	private readonly object _previewLock = new();
	private string? _qualifiedWidgetTypeId;
	private IUiResourceRegistry? _resources;
	private byte[]? _previewSource;
	private UiResource? _preview;
	private IReadOnlyDictionary<NoteDuration, UiResource> _durationIcons = new Dictionary<NoteDuration, UiResource>();
	private IReadOnlyDictionary<NoteDuration, UiResource> _dottedDurationIcons = new Dictionary<NoteDuration, UiResource>();

	private event Action? PreviewChanged;

	public ScoreWidgetIntegration(DoricoSession session, ILogger logger)
	{
		_session = session;
		_logger = logger.ForContext<ScoreWidgetIntegration>();
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public string ProviderName => "DoriDeck";

	private static WidgetTypeDescriptor Descriptor { get; } = new(
		WidgetTypeId,
		Strings.WidgetTypes.ScoreInfo.Name(),
		Strings.WidgetTypes.ScoreInfo.Description());

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [Descriptor];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_resources = context.UiResources;
		try
		{
			(_durationIcons, _dottedDurationIcons) = await PianoKeyArtwork.RegisterDurationIconsAsync(context.UiResources);
		}
		catch (Exception ex) when (ex is UiResourceException or IOException)
		{
			_logger.Warning(ex, "Duration icons unavailable; the grid indicator shows a fraction.");
		}

		_session.DisplayStateChanged += OnSessionDisplayStateChanged;
		OnSessionDisplayStateChanged();
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		var registration = await context.RegisterWidgetTypeAsync(Descriptor, cancellationToken);
		_qualifiedWidgetTypeId = registration.WidgetTypeId;
		_logger.Information("Registered widget type {LocalId} as {QualifiedId}.", WidgetTypeId, _qualifiedWidgetTypeId);
	}

	public Task ShutdownAsync()
	{
		_session.DisplayStateChanged -= OnSessionDisplayStateChanged;
		return Task.CompletedTask;
	}

	/// Registers the preview once per score, not once per widget: every tile then refers to the same resource.
	private void OnSessionDisplayStateChanged()
	{
		var png = _session.ScorePreviewPng;
		lock (_previewLock)
		{
			if (ReferenceEquals(png, _previewSource))
			{
				return;
			}

			_previewSource = png;
		}

		_ = RegisterPreviewAsync(png);
	}

	private async Task RegisterPreviewAsync(byte[]? png)
	{
		UiResource? preview = null;
		if (png is not null && _resources is not null)
		{
			try
			{
				// Same name every time: registering again replaces the picture and changes the handle's hash.
				preview = await _resources.RegisterAsync(PreviewResourceName, png, PreviewMediaType, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to register the score preview; showing the placeholder.");
			}
		}

		lock (_previewLock)
		{
			// A newer score may have been opened while this one registered; only the latest may win.
			if (!ReferenceEquals(png, _previewSource))
			{
				return;
			}

			_preview = preview;
		}

		PreviewChanged?.Invoke();
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind != UiSurfaceKinds.Widget && surface.Kind != UiSurfaceKinds.Preview)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var widgetType = ReadString(surface, UiWidgetSurfaceAttributes.WidgetType);
		if (widgetType is not null && widgetType != WidgetTypeId && widgetType != _qualifiedWidgetTypeId)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample))
		{
			var sample = new UiView(surface, ScoreWidgetView.Build(ScoreWidgetViewModel.Sample()));
			return Task.FromResult<IUiSession?>(new PianoKeyboardSession(sample));
		}

		var model = new ScoreWidgetViewModel
		{
			Play = ct => SendCommandAsync("Play.StartOrStop", ct),
			Stop = ct => SendCommandAsync("Play.Stop", ct),
			Record = ct => SendCommandAsync("Play.Record", ct),
			OpenRecent = async ct =>
				(await OpenRecentFileAction.OpenRecentFileAsync(_session, 0, ct)).Status != ActionResultStatus.Failed,
			DurationIcons = _durationIcons,
			DottedDurationIcons = _dottedDurationIcons,
		};
		void OnDisplayStateChanged() => model.Update(_session, _preview);
		void OnNoteInputActiveChanged(bool _) => OnDisplayStateChanged();
		OnDisplayStateChanged();
		_session.DisplayStateChanged += OnDisplayStateChanged;
		_session.NoteInputActiveChanged += OnNoteInputActiveChanged;
		PreviewChanged += OnDisplayStateChanged;

		var view = new UiView(surface, ScoreWidgetView.Build(model));
		return Task.FromResult<IUiSession?>(new PianoKeyboardSession(
			view,
			onDispose: () =>
			{
				_session.DisplayStateChanged -= OnDisplayStateChanged;
				_session.NoteInputActiveChanged -= OnNoteInputActiveChanged;
				PreviewChanged -= OnDisplayStateChanged;
				model.Dispose();
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

	private static string? ReadString(UiSurface surface, string attribute) =>
		surface.Attributes.TryGetValue(attribute, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool ReadBool(UiSurface surface, string attribute) =>
		surface.Attributes.TryGetValue(attribute, out var value) && value.ValueKind == JsonValueKind.True;
}
