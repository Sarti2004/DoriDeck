using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace DoriDeck.Services;

/// A score Dorico opened, keyed by the OCF session id it logged, which is the openScoreID Remote Control reports.
public sealed record OpenedScore(int SessionId, string Path);

public sealed class ScoreOpenLogReader
{
	private const int ChunkSize = 64 * 1024;
	private const int MaxLineLength = 8 * 1024;
	private const string OpenMarker = "Create new OCF session ";
	private const string PathStartMarker = " for ";
	private const string ScoreExtension = ".dorico";

	private static ReadOnlySpan<byte> OpenMarkerUtf8 => "Create new OCF session "u8;

	private readonly ILogger _logger;
	private readonly object _lock = new();
	private readonly Dictionary<int, string> _scorePaths = new();

	private string _logPath = string.Empty;

	// Always the start of a line: a line Dorico is still writing is left for the next call.
	private long _offset;

	public ScoreOpenLogReader(ILogger logger)
	{
		_logger = logger.ForContext<ScoreOpenLogReader>();
	}

	public void Reset()
	{
		lock (_lock)
		{
			_logPath = string.Empty;
			_scorePaths.Clear();
		}
	}

	/// Path of the score Dorico opened in OCF session <paramref name="sessionId"/>, from the entries read so far.
	public string? GetScorePath(int sessionId)
	{
		lock (_lock)
		{
			return _scorePaths.GetValueOrDefault(sessionId);
		}
	}

	/// Caches every score opened since the last call and returns the most recent one, or null if none was opened.
	public OpenedScore? ReadNewlyOpenedScores(string logPath)
	{
		lock (_lock)
		{
			long length;
			try
			{
				var info = new FileInfo(logPath);
				if (!info.Exists)
				{
					return null;
				}

				length = info.Length;
			}
			catch
			{
				return null;
			}

			if (!string.Equals(logPath, _logPath, StringComparison.Ordinal) || length < _offset)
			{
				_logPath = logPath;
				_offset = 0;
				_scorePaths.Clear();
			}

			if (length == _offset)
			{
				return null;
			}

			// Room for a chunk plus the overlap FindOpenedScores reads past it.
			var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + OpenMarkerUtf8.Length);
			try
			{
				using var handle = File.OpenHandle(
					logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

				var completeEnd = FindCompleteEnd(handle, _offset, length, buffer);
				if (completeEnd == _offset)
				{
					return null;
				}

				var openedScores = FindOpenedScores(handle, _offset, completeEnd, buffer);
				_offset = completeEnd;

				// Newest first, so walk backwards to let a later open of the same session id win.
				for (var i = openedScores.Count - 1; i >= 0; i--)
				{
					_scorePaths[openedScores[i].SessionId] = openedScores[i].Path;
				}

				return openedScores.Count > 0 ? openedScores[0] : null;
			}
			catch (Exception ex)
			{
				_logger.Information(ex, "Failed to read Dorico log {Path}.", logPath);
				return null;
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	/// Position just past the last newline in [start, end), or start if Dorico hasn't finished a line there yet.
	private static long FindCompleteEnd(SafeFileHandle handle, long start, long end, byte[] buffer)
	{
		for (var chunkEnd = end; chunkEnd > start;)
		{
			var chunkStart = Math.Max(start, chunkEnd - ChunkSize);
			var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)(chunkEnd - chunkStart)), chunkStart);

			var newline = buffer.AsSpan(0, read).LastIndexOf((byte)'\n');
			if (newline >= 0)
			{
				return chunkStart + newline + 1;
			}

			chunkEnd = chunkStart;
		}

		return start;
	}

	/// Searches [start, end) chunk by chunk from the end, so the result is ordered newest first.
	private static List<OpenedScore> FindOpenedScores(SafeFileHandle handle, long start, long end, byte[] buffer)
	{
		var marker = OpenMarkerUtf8;
		var openedScores = new List<OpenedScore>();

		for (var chunkEnd = end; chunkEnd > start;)
		{
			var chunkStart = Math.Max(start, chunkEnd - ChunkSize);

			// Read a little into the following chunk so a marker split across the boundary is still found. A marker
			// starting inside that overlap can't fit in it, so nothing is matched twice.
			var readEnd = Math.Min(end, chunkEnd + marker.Length - 1);
			var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)(readEnd - chunkStart)), chunkStart);
			var chunk = buffer.AsSpan(0, read);

			int index;
			while ((index = chunk.LastIndexOf(marker)) >= 0)
			{
				if (ReadOpenedScoreAt(handle, chunkStart + index, end) is { } openedScore)
				{
					openedScores.Add(openedScore);
				}

				chunk = chunk[..index];
			}

			chunkEnd = chunkStart;
		}

		return openedScores;
	}

	private static OpenedScore? ReadOpenedScoreAt(SafeFileHandle handle, long markerPosition, long end)
	{
		var length = (int)Math.Min(MaxLineLength, end - markerPosition);
		var buffer = ArrayPool<byte>.Shared.Rent(length);
		try
		{
			var line = buffer.AsSpan(0, RandomAccess.Read(handle, buffer.AsSpan(0, length), markerPosition));

			var newline = line.IndexOf((byte)'\n');
			if (newline >= 0)
			{
				line = line[..newline];
			}

			return TryGetOpenedScore(Encoding.UTF8.GetString(line));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	/// e.g. "... [info] Create new OCF session 0 for /path/Score in C.dorico in /var/folders/.../0-4c4e5"
	private static OpenedScore? TryGetOpenedScore(string line)
	{
		var markerIndex = line.IndexOf(OpenMarker, StringComparison.Ordinal);
		if (markerIndex < 0)
		{
			return null;
		}

		var idStart = markerIndex + OpenMarker.Length;
		var forIndex = line.IndexOf(PathStartMarker, idStart, StringComparison.Ordinal);
		if (forIndex < 0 ||
			!int.TryParse(line.AsSpan(idStart, forIndex - idStart), NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId))
		{
			return null;
		}

		// Anchor on the extension rather than " in ", which can legitimately appear in a score's file name.
		var start = forIndex + PathStartMarker.Length;
		var end = line.IndexOf(ScoreExtension + " in ", start, StringComparison.OrdinalIgnoreCase);

		return end < 0 ? null : new OpenedScore(sessionId, line[start..(end + ScoreExtension.Length)]);
	}
}
