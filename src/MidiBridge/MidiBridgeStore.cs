using MacroDeck.Sdk.Storage;
using Serilog;

namespace DoriDeck.MidiBridge;

public sealed class MidiBridgeStore(ILogger logger)
{
	public const string StorageKey = "midi-bridge.json";

	private readonly ILogger _logger = logger.ForContext<MidiBridgeStore>();
	private readonly Lock _sync = new();
	private Task _writes = Task.CompletedTask;
	private IIntegrationStorage? _storage;
	private MidiBridgeDocument _document = new();
	private IReadOnlyDictionary<int, MidiBinding> _bindings = new Dictionary<int, MidiBinding>();

	public event Action? Changed;

	public IReadOnlyDictionary<int, MidiBinding> Bindings => _bindings;

	public string Mode => _document.Mode;

	public async Task LoadAsync(IIntegrationStorage storage, CancellationToken cancellationToken = default)
	{
		_storage = storage;

		MidiBridgeDocument document;
		try
		{
			var result = await storage.ReadAsync(StorageKey, cancellationToken: cancellationToken).ConfigureAwait(false);
			document = result.Status switch
			{
				IntegrationStorageReadStatus.Found when result.Content is { } content => MidiBridgeSerializer.Deserialize(content),
				IntegrationStorageReadStatus.Unreadable => LogUnreadable(),
				_ => new MidiBridgeDocument(),
			};
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "no mappings");
			document = new MidiBridgeDocument();
		}

		Apply(document);
		Changed?.Invoke();
		_logger.Information("Loaded {Count} MIDI mapping(s) {Mode}", _bindings.Count, document.Mode);
	}

	public Task SetAsync(MidiBinding binding) =>
		UpdateAsync(document => document with
		{
			Bindings = [.. document.Bindings.Where(existing => existing.Note != binding.Note), binding],
		});

	public Task RemoveAsync(int note) =>
		UpdateAsync(document => document with
		{
			Bindings = [.. document.Bindings.Where(existing => existing.Note != note)],
		});

	public Task SetModeAsync(string mode) =>
		string.IsNullOrWhiteSpace(mode) || string.Equals(mode, Mode, StringComparison.Ordinal)
			? Task.CompletedTask
			: UpdateAsync(document => document with { Mode = mode });

	private async Task UpdateAsync(Func<MidiBridgeDocument, MidiBridgeDocument> change)
	{
		lock (_sync)
		{
			Apply(change(_document));
		}

		Changed?.Invoke();
		await PersistAsync().ConfigureAwait(false);
	}

	private void Apply(MidiBridgeDocument document)
	{
		lock (_sync)
		{
			var ordered = document.Bindings.OrderBy(binding => binding.Note).ToList();
			_document = document with { Version = MidiBridgeDocument.CurrentVersion, Bindings = ordered };
			_bindings = ordered.ToDictionary(binding => binding.Note);
		}
	}

	private Task PersistAsync()
	{
		if (_storage is not { } storage)
		{
			return Task.CompletedTask;
		}

		lock (_sync)
		{
			_writes = _writes.ContinueWith(_ => WriteLatestAsync(storage), TaskScheduler.Default).Unwrap();
			return _writes;
		}
	}

	private async Task WriteLatestAsync(IIntegrationStorage storage)
	{
		try
		{
			string json;
			lock (_sync)
			{
				json = MidiBridgeSerializer.Serialize(_document);
			}

			await storage.WriteAsync(StorageKey, json).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "MIDI mappings could not be saved.");
		}
	}

	private MidiBridgeDocument LogUnreadable()
	{
		_logger.Warning("Can not read MIDI mappings");
		return new MidiBridgeDocument();
	}
}
