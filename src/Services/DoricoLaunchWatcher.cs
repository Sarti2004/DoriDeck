using System.Diagnostics;
using Serilog;

namespace DoriDeck.Services;

public sealed class DoricoLaunchWatcher : IDisposable
{
	// to prevent multiple events firing.
	private static readonly TimeSpan LaunchDebounce = TimeSpan.FromSeconds(30);

	private readonly FileSystemWatcher? _watcher;
	private long _lastLaunchTimestamp = long.MinValue;

	public event Action? Launched;

	public DoricoLaunchWatcher(string logPath, ILogger logger)
	{
		var directory = Path.GetDirectoryName(logPath);
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
		{
			logger.ForContext<DoricoLaunchWatcher>()
				.Warning("Dorico settings folder {Path} not found;", directory);
			return;
		}

		try
		{
			_watcher = new FileSystemWatcher(directory, Path.GetFileName(logPath))
			{
				NotifyFilter = NotifyFilters.FileName,
				IncludeSubdirectories = false,
			};
			_watcher.Created += OnLogReplaced;
			_watcher.Renamed += OnLogReplaced;
			_watcher.EnableRaisingEvents = true;
		}
		catch (Exception ex)
		{
			logger.ForContext<DoricoLaunchWatcher>().Warning(ex, "Failed to track {Path} for Dorico starts.", directory);
			_watcher?.Dispose();
			_watcher = null;
		}
	}

	private void OnLogReplaced(object sender, FileSystemEventArgs e)
	{
		var now = Stopwatch.GetTimestamp();
		var last = Interlocked.Read(ref _lastLaunchTimestamp);

		if (last != long.MinValue && Stopwatch.GetElapsedTime(last, now) < LaunchDebounce)
		{
			return;
		}

		if (Interlocked.CompareExchange(ref _lastLaunchTimestamp, now, last) == last)
		{
			Launched?.Invoke();
		}
	}

	public void Dispose() => _watcher?.Dispose();
}
