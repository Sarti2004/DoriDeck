using System.IO.Compression;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace DoriDeck.MidiBridge;


internal sealed class BundledIcons
{
	public const string PackKey = "music-notation";

	private const int MaxAttempts = 5;
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

	private static readonly string PackPath =
		Path.Combine(AppContext.BaseDirectory, "icon-packs", $"{PackKey}.macroDeckIconPack");

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly Lock _queueLock = new();
	private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
	private readonly ILogger _logger;
	private IUiResourceRegistry? _registry;
	private Task _queue = Task.CompletedTask;

	public BundledIcons(ILogger logger)
	{
		_logger = logger;
	}

	public IReadOnlyList<string> Names { get; private set; } = [];


	public UiState<IReadOnlyDictionary<string, UiResource>> Resources { get; } =
		new(new Dictionary<string, UiResource>(StringComparer.Ordinal));

	public static string Reference(string name) => $"{PackKey}/{name}";

	public static bool IsReference(string? icon) => icon?.StartsWith($"{PackKey}/", StringComparison.Ordinal) == true;

	public async Task LoadAsync(IUiResourceRegistry registry, CancellationToken cancellationToken = default)
	{
		_registry = registry;
		try
		{
			Names = await ReadNamesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
		{
			_logger.Warning(exception, "Bundled icon pack {Path} unreadable; only built-in icons are offered.", PackPath);
			Names = [];
		}
	}


	public Task EnsureAsync(IEnumerable<string?> icons, CancellationToken cancellationToken = default)
	{
		string[] wanted = [.. icons.Where(IsReference).Distinct(StringComparer.Ordinal)!];
		lock (_queueLock)
		{
			_queue = EnsureAfterAsync(_queue, wanted, cancellationToken);
			return _queue;
		}
	}

	private async Task EnsureAfterAsync(Task previous, IReadOnlyList<string> icons, CancellationToken cancellationToken)
	{
		try
		{
			await previous.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Already logged by whoever awaited it.
		}

		if (_registry is not { } registry)
		{
			return;
		}

		foreach (var icon in icons)
		{
			if (Resources.Peek().ContainsKey(icon) || _missing.Contains(icon))
			{
				continue;
			}

			if (await ResolveAsync(registry, icon, cancellationToken).ConfigureAwait(false) is { } resource)
			{
				Resources.Set(new Dictionary<string, UiResource>(Resources.Peek(), StringComparer.Ordinal) { [icon] = resource });
			}
		}
	}

	private async Task<UiResource?> ResolveAsync(IUiResourceRegistry registry, string icon, CancellationToken cancellationToken)
	{
		var name = icon[(PackKey.Length + 1)..];
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await registry.GetPluginIconAsync(PackKey, name, cancellationToken).ConfigureAwait(false);
			}
			catch (UiResourceException exception) when (exception.ErrorCode == UiResourceErrorCode.RateLimited && attempt < MaxAttempts)
			{
				await Task.Delay(RetryDelay * attempt, cancellationToken).ConfigureAwait(false);
			}
			catch (UiResourceException exception)
			{
				_logger.Warning(exception, "Bundled icon {Name} unavailable.", name);
				if (exception.ErrorCode != UiResourceErrorCode.RateLimited)
				{
					_missing.Add(icon);
				}

				return null;
			}
		}
	}

	private static async Task<IReadOnlyList<string>> ReadNamesAsync(CancellationToken cancellationToken)
	{
		using var archive = ZipFile.OpenRead(PackPath);
		var entry = archive.GetEntry("pack.json") ?? throw new InvalidDataException("pack.json missing.");
		await using var stream = entry.Open();
		var pack = await JsonSerializer.DeserializeAsync<PackDocument>(stream, Json, cancellationToken).ConfigureAwait(false);

		return
		[
			.. (pack?.Icons ?? [])
				.Select(icon => icon.Name)
				.OfType<string>()
				.Where(name => !string.IsNullOrWhiteSpace(name) && !name.Contains('/', StringComparison.Ordinal))
				.Distinct(StringComparer.Ordinal),
		];
	}

	private sealed record PackDocument(IReadOnlyList<PackIcon>? Icons);

	private sealed record PackIcon(string? Name);
}
