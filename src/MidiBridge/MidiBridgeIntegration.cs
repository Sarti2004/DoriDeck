using System.Text.Json;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace DoriDeck.MidiBridge;

public sealed class MidiBridgeIntegration : IPluginIntegration, IUiProvider
{
	public const string PageId = "midi-keys";

	private readonly ILogger _logger;
	private readonly MidiBridgeActions _actions;
	private readonly MidiBridgeStore _store;
	private readonly List<IAsyncDisposable> _subscriptions = [];
	private MidiRouterClient? _router;
	private MidiActionRunner? _runner;

	public MidiBridgeIntegration(DoricoSession session, ILogger logger)
	{
		_logger = logger.ForContext<MidiBridgeIntegration>();
		_actions = new MidiBridgeActions(session, logger);
		_store = new MidiBridgeStore(logger);
		_store.Changed += OnMappingChanged;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive },
	];

	public IReadOnlyList<UiPageDeclaration> Pages { get; } =
	[
		new UiPageDeclaration { Id = PageId, Title = Strings.Pages.MidiBridge.Title(), IconName = UiIcons.MusicNote },
	];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		// Initialization can run again (after reconnect); start it clean.
		await StopAsync().ConfigureAwait(false);

		await _store.LoadAsync(context.Storage).ConfigureAwait(false);

		var runner = new MidiActionRunner(_actions, _logger);
		var router = new MidiRouterClient(_logger);
		_runner = runner;
		_router = router;

		try
		{
			_subscriptions.Add(await context.Messages.SubscribeAsync(
				RouterProtocol.DeliveryTopic,
				(message, _) =>
				{
					if (RouterProtocol.FindTriggeredBinding(_store.Bindings, Read<DeliveredMessage>(message)) is { } binding)
					{
						runner.Enqueue(binding);
					}

					return Task.CompletedTask;
				}).ConfigureAwait(false));

			_subscriptions.Add(await context.Messages.SubscribeAsync(
				RouterProtocol.StateChangedTopic,
				(message, _) =>
				{
					router.OnStateChanged(Read<RouterState>(message));
					return Task.CompletedTask;
				}).ConfigureAwait(false));
		}
		catch (Exception exception) when (exception is MessageChannelException or NotSupportedException or JsonException)
		{
			_logger.Warning(exception, "The plugin message channel is unavailable;");
			return;
		}

		router.Start(context.Messages, () => RouterProtocol.BuildSubscribeRequest(_store.Bindings.Values, _store.Mode));
		_logger.Information("Initialized.");
	}

	public Task ShutdownAsync() => StopAsync();

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		if (request.Surface.Kind != UiSurfaceKinds.Page ||
			!request.Surface.Attributes.TryGetValue(UiPageSurfaceAttributes.PageId, out var pageId) ||
			pageId.ValueKind != JsonValueKind.String ||
			pageId.GetString() != PageId)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var model = new MidiBridgeEditorViewModel(_store, _router ?? new MidiRouterClient(_logger), _actions);
		var view = new UiView(request.Surface, MidiBridgeEditorView.Build(model));
		return Task.FromResult<IUiSession?>(new PianoKeyboardSession(view, onDispose: model.Detach));
	}

	private void OnMappingChanged() => _router?.RequestSync();

	private T? Read<T>(ChannelMessage message) where T : class
	{
		try
		{
			return message.PayloadAs<T>(RouterProtocol.Json);
		}
		catch (JsonException exception)
		{
			_logger.Debug(exception, "Ignored {Topic} message.", message.Topic);
			return null;
		}
	}

	private async Task StopAsync()
	{
		foreach (var subscription in _subscriptions)
		{
			try
			{
				await subscription.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				_logger.Debug(exception, "Disposing a message subscription failed.");
			}
		}

		_subscriptions.Clear();

		if (_router is { } router)
		{
			_router = null;
			await router.DisposeAsync().ConfigureAwait(false);
		}

		if (_runner is { } runner)
		{
			_runner = null;
			await runner.DisposeAsync().ConfigureAwait(false);
		}
	}
}
