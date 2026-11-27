using System.ComponentModel;
using System.Diagnostics;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Decks;
using Serilog;

namespace DoriDeck.Services;

internal sealed class FocusedWindowTracker : IDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	private readonly ILogger _logger;
	private readonly Lock _gate = new();

	private IIntegrationContext? _context;
	private CancellationTokenSource? _polling;

	public FocusedWindowTracker(ILogger logger)
	{
		_logger = logger.ForContext<FocusedWindowTracker>();
	}

	public void Attach(IIntegrationContext context)
	{
		Detach();

		lock (_gate)
		{
			_context = context;
		}

		context.Events.BindingsChanged += ApplyBindings;
		ApplyBindings();
	}

	public void Detach()
	{
		IIntegrationContext? context;
		lock (_gate)
		{
			context = _context;
			_context = null;
			StopPolling();
		}

		if (context is not null)
		{
			context.Events.BindingsChanged -= ApplyBindings;
		}
	}

	public void Dispose() => Detach();

	private void ApplyBindings()
	{
		lock (_gate)
		{
			if (_context is not { } context)
			{
				return;
			}

			StopPolling();

			var rules = WindowFocusRules.From(context.Events.GetBindings());
			if (rules.IsEmpty)
			{
				return;
			}

			_polling = new CancellationTokenSource();
			_ = PollAsync(context, rules, _polling.Token);
		}
	}

	private void StopPolling()
	{
		_polling?.Cancel();
		_polling?.Dispose();
		_polling = null;
	}

	private async Task PollAsync(IIntegrationContext context, WindowFocusRules rules, CancellationToken cancellationToken)
	{
		var lastWindow = IntPtr.Zero;
		string? lastTitle = null;
		string? lastMatch = null;
		uint processId = 0;
		string? application = null;
		IReadOnlyList<DeckClient>? decksBefore = null;

		try
		{
			using var timer = new PeriodicTimer(PollInterval);
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				var window = WindowAndClipboardInterop.GetForegroundWindowHandle();

				// On macOS the handle is the frontmost app, so window switches inside it show only in the title.
				if (window == IntPtr.Zero || (window == lastWindow && !OperatingSystem.IsMacOS()))
				{
					continue;
				}

				var windowProcessId = WindowAndClipboardInterop.GetForegroundWindowProcessId(window);
				if (windowProcessId != processId)
				{
					processId = windowProcessId;
					application = ResolveApplication(processId);
				}

				string? title = null;
				if (application is not null)
				{
					title = WindowAndClipboardInterop.GetForegroundWindowTitle(window);
					if (string.IsNullOrEmpty(title))
					{
						// A new window may not have its title yet.
						continue;
					}
				}

				lastWindow = window;
				if (string.Equals(title, lastTitle, StringComparison.Ordinal))
				{
					continue;
				}

				lastTitle = title;
				var match = title is null ? null : rules.Match(application!, title);
				if (string.Equals(match, lastMatch, StringComparison.Ordinal))
				{
					continue;
				}

				lastMatch = match;
				if (match is null)
				{
					if (decksBefore is not null)
					{
						await RestoreAsync(context.Deck, decksBefore, cancellationToken).ConfigureAwait(false);
						decksBefore = null;
					}

					continue;
				}

				decksBefore ??= context.Deck.GetClients();
				_logger.Debug("{Application} window focused: {Title}", application, title);
				context.Events.Publish(WindowFocusEvent.Id, WindowFocusEvent.Payload(application!, title!));
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Window focus tracking stopped.");
		}
	}

	private async Task RestoreAsync(IDeckNavigator deck, IReadOnlyList<DeckClient> before, CancellationToken cancellationToken)
	{
		var current = deck.GetClients();
		foreach (var client in before)
		{
			var now = current.FirstOrDefault(c => string.Equals(c.ClientId, client.ClientId, StringComparison.Ordinal));
			if (now is null)
			{
				continue;
			}

			try
			{
				if (!string.IsNullOrEmpty(client.ProfileId) && !string.Equals(now.ProfileId, client.ProfileId, StringComparison.Ordinal))
				{
					await deck.ChangeProfileAsync(client.ProfileId, client.ClientId, cancellationToken).ConfigureAwait(false);
				}

				if (!string.IsNullOrEmpty(client.FolderId) && !string.Equals(now.FolderId, client.FolderId, StringComparison.Ordinal))
				{
					await deck.ChangeFolderAsync(client.FolderId, client.ClientId, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Couldn't switch deck {ClientId} back.", client.ClientId);
			}
		}
	}

	private static string? ResolveApplication(uint processId)
	{
		try
		{
			using var process = Process.GetProcessById(checked((int)processId));
			return WindowFocusEvent.FindApplication(process.ProcessName);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
		{
			return null;
		}
	}
}
