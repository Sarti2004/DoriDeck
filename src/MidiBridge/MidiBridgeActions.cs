using DoriDeck.Actions;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace DoriDeck.MidiBridge;

internal sealed class MidiBridgeActions
{
	public const string DefaultActionId = "run-command";

	public MidiBridgeActions(DoricoSession session, ILogger logger)
	{
		IActionDefinition[] candidates =
		[
			new RunCommandAction(session),
			new RunScriptAction(session, logger),
			new TupletAction(session),
			new RespellNoteAction(session),
			new PickupMeasureAction(session),
			new OpenRecentFileAction(session),
			new OpenPreviousFileAction(session),
		];

		All = [.. candidates.Where(action => action.Parameters.All(IsSupported))];
		ById = All.ToDictionary(action => action.Id, StringComparer.Ordinal);
	}

	public IReadOnlyList<IActionDefinition> All { get; }

	public IReadOnlyDictionary<string, IActionDefinition> ById { get; }

	/// Parameter types the editor can render.
	public static bool IsSupported(ActionParameter parameter) => parameter.Type is
		ActionParameterType.String or
		ActionParameterType.Autocomplete or
		ActionParameterType.Choice or
		ActionParameterType.Boolean or
		ActionParameterType.Number;
}

internal sealed class MidiActionRunner : IAsyncDisposable
{
	private const int QueueCapacity = 32;

	private static readonly TimeSpan ExecutionLimit = TimeSpan.FromSeconds(30);

	private readonly MidiBridgeActions _actions;
	private readonly ILogger _logger;
	private readonly System.Threading.Channels.Channel<MidiBinding> _queue =
		System.Threading.Channels.Channel.CreateBounded<MidiBinding>(
			new System.Threading.Channels.BoundedChannelOptions(QueueCapacity)
			{
				SingleReader = true,
				FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
			});

	private readonly CancellationTokenSource _stopping = new();
	private readonly Task _loop;

	public MidiActionRunner(MidiBridgeActions actions, ILogger logger)
	{
		_actions = actions;
		_logger = logger.ForContext<MidiActionRunner>();
		_loop = Task.Run(RunAsync);
	}

	public void Enqueue(MidiBinding binding)
	{
		if (!_queue.Writer.TryWrite(binding))
		{
			_logger.Warning("MIDI action queue is full; dropped the press on note {Note}.", binding.Note);
		}
	}

	public async ValueTask DisposeAsync()
	{
		_queue.Writer.TryComplete();
		await _stopping.CancelAsync().ConfigureAwait(false);
		await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		_stopping.Dispose();
	}

	private async Task RunAsync()
	{
		try
		{
			await foreach (var binding in _queue.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
			{
				await ExecuteAsync(binding).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
		}
	}

	private async Task ExecuteAsync(MidiBinding binding)
	{
		if (!_actions.ById.TryGetValue(binding.ActionId, out var action))
		{
			_logger.Warning("Note {Note} is mapped to unknown action {ActionId}.", binding.Note, binding.ActionId);
			return;
		}

		using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
		limit.CancelAfter(ExecutionLimit);

		try
		{
			var parameters = binding.Parameters
				.Where(pair => pair.Value is not null)
				.ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

			var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters,
				CancellationToken = limit.Token,
			}).ConfigureAwait(false);

			if (result.Status == ActionResultStatus.Failed)
			{
				_logger.Warning(
					"MIDI note {Note} → {ActionId} failed: {ErrorCode} {Message}",
					binding.Note,
					binding.ActionId,
					result.ErrorCode,
					result.ErrorMessage.ToString());
			}
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "MIDI note {Note} → {ActionId} threw.", binding.Note, binding.ActionId);
		}
	}
}
