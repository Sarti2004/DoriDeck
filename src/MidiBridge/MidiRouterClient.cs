using System.Threading.Channels;
using MacroDeck.Sdk.Messaging;
using Serilog;

namespace DoriDeck.MidiBridge;

/// <summary>What the editor shows about the router.</summary>
public sealed record RouterStatus(bool Available, RouterState? State, string? Error)
{
	public static RouterStatus Unknown { get; } = new(false, null, null);
}

public sealed class MidiRouterClient(ILogger logger) : IAsyncDisposable
{
	public static readonly TimeSpan RenewInterval = TimeSpan.FromMinutes(2);

	private const int MaxAttemptsWithoutAnswer = 2;

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger = logger.ForContext<MidiRouterClient>();
	private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

	private IMessageChannel? _channel;
	private Func<SubscribeRequest>? _buildRequest;
	private CancellationTokenSource? _stopping;
	private Task? _loop;
	private bool _failureLogged;
	private string? _lastError;

	public event Action? StatusChanged;

	public RouterStatus Status { get; private set; } = RouterStatus.Unknown;

	public void Start(IMessageChannel channel, Func<SubscribeRequest> buildRequest)
	{
		_channel = channel;
		_buildRequest = buildRequest;
		_stopping = new CancellationTokenSource();
		_loop = Task.Run(() => RunAsync(_stopping.Token));
	}

	public void RequestSync() => _wake.Writer.TryWrite(true);

	public void OnStateChanged(RouterState? state)
	{
		if (state is null)
		{
			return;
		}

		var wasAvailable = Status.Available;
		SetStatus(Status with { Available = true, State = state });
		if (!wasAvailable)
		{
			RequestSync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_stopping is null)
		{
			return;
		}

		await _stopping.CancelAsync().ConfigureAwait(false);
		if (_loop is { } loop)
		{
			await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}

		if (_channel is { } channel && Status.Available)
		{
			try
			{
				var release = RouterProtocol.BuildSubscribeRequest([], string.Empty);
				await channel.RequestAsync<SubscribeRequest, SubscribeResponse>(
					RouterProtocol.SubscribeTopic, release, RouterProtocol.Json, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				_logger.Debug(exception, "Could not release the MIDI subscription on shutdown.");
			}
		}

		_stopping.Dispose();
		_stopping = null;
	}

	private async Task RunAsync(CancellationToken stopping)
	{
		var failures = 0;
		while (!stopping.IsCancellationRequested)
		{
			await SendAsync(stopping).ConfigureAwait(false);
			failures = Status.Available ? 0 : failures + 1;

			using var wait = CancellationTokenSource.CreateLinkedTokenSource(stopping);
			if (failures < MaxAttemptsWithoutAnswer)
			{
				wait.CancelAfter(RenewInterval);
			}

			try
			{
				await _wake.Reader.ReadAsync(wait.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Renewal time, or shutting down - the loop condition tells.
			}
		}
	}

	private async Task SendAsync(CancellationToken stopping)
	{
		if (_channel is not { } channel || _buildRequest is not { } buildRequest)
		{
			return;
		}

		try
		{
			var request = buildRequest();
			var response = await channel.RequestAsync<SubscribeRequest, SubscribeResponse>(
				RouterProtocol.SubscribeTopic, request, RouterProtocol.Json, RequestTimeout, stopping).ConfigureAwait(false);

			if (_failureLogged)
			{
				_logger.Information("MIDI Router is available again.");
				_failureLogged = false;
			}

			var error = response?.Error;
			if (error is not null && !string.Equals(error, _lastError, StringComparison.Ordinal))
			{
				_logger.Warning("MIDI Router rejected the subscription: {Error}", error);
			}

			_lastError = error;
			SetStatus(new RouterStatus(true, response?.State ?? Status.State, error));
		}
		catch (OperationCanceledException) when (stopping.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			if (!_failureLogged)
			{
				_logger.Information("MIDI Router not available ({Reason}); retrying once in {Minutes} min.", exception.Message, RenewInterval.TotalMinutes);
				_failureLogged = true;
			}
			else
			{
				_logger.Debug(exception, "MIDI Router still not available.");
			}

			SetStatus(new RouterStatus(false, null, null));
		}
	}

	private void SetStatus(RouterStatus status)
	{
		if (status == Status)
		{
			return;
		}

		Status = status;
		StatusChanged?.Invoke();
	}
}
