using System.Text.RegularExpressions;
using DoriDeck.PianoFolder.Notes;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.ScoreWidget;

public sealed partial class ScoreWidgetViewModel : IDisposable
{
	private static readonly Func<CancellationToken, Task> NoOp = _ => Task.CompletedTask;

	private static readonly TimeSpan OpeningTimeout = TimeSpan.FromSeconds(90);

	private CancellationTokenSource? _openingTimeout;

	public UiState<bool> HasScore { get; } = new(false);

	public UiState<string> Title { get; } = new(string.Empty);

	public UiState<string> Composer { get; } = new(string.Empty);

	public UiState<string> Duration { get; } = new(string.Empty);

	/// First letter of the Dorico mode, e.g. "W" for Write, or empty when unknown.
	public UiState<string> ModeLetter { get; } = new(string.Empty);

	public UiState<bool> InPlayback { get; } = new(false);

	public UiState<bool> IsRecording { get; } = new(false);

	public UiState<bool> NoteInputActive { get; } = new(false);

	/// Rhythmic grid resolution as a fraction, e.g. "1/16" or "1/8." when dotted, or empty when unknown.
	public UiState<string> GridResolution { get; } = new(string.Empty);

	/// Note icon for the rhythmic grid resolution, or null to show <see cref="GridResolution"/> as text.
	public UiState<UiResource?> GridIcon { get; } = new(null);

	public IReadOnlyDictionary<NoteDuration, UiResource> DurationIcons { get; init; } =
		new Dictionary<NoteDuration, UiResource>();

	public IReadOnlyDictionary<NoteDuration, UiResource> DottedDurationIcons { get; init; } =
		new Dictionary<NoteDuration, UiResource>();

	public UiState<UiResource?> Preview { get; } = new(null);

	public UiState<string> RecentFile { get; } = new(string.Empty);

	public Func<CancellationToken, Task> Play { get; init; } = NoOp;

	public Func<CancellationToken, Task> Stop { get; init; } = NoOp;

	public Func<CancellationToken, Task> Record { get; init; } = NoOp;

	/// Open the most recent project
	public Func<CancellationToken, Task<bool>> OpenRecent { get; init; } = _ => Task.FromResult(false);

	/// True from pressing "Open Score"
	public UiState<bool> IsOpening { get; } = new(false);

	public async Task OpenScoreAsync(CancellationToken cancellationToken)
	{
		if (IsOpening.Value)
		{
			return;
		}

		IsOpening.Value = true;
		var timeout = new CancellationTokenSource(OpeningTimeout);
		timeout.Token.Register(() => IsOpening.Value = false);
		Interlocked.Exchange(ref _openingTimeout, timeout)?.Dispose();

		var started = false;
		try
		{
			started = await OpenRecent(cancellationToken);
		}
		finally
		{
			if (!started)
			{
				StopOpening();
			}
		}
	}

	private void StopOpening()
	{
		if (Interlocked.Exchange(ref _openingTimeout, null) is { } timeout)
		{
			timeout.Cancel();
			timeout.Dispose();
		}

		IsOpening.Value = false;
	}

	public void Dispose() => StopOpening();

	public void Update(DoricoSession session, UiResource? preview)
	{
		if (session.HasScore)
		{
			StopOpening();
		}

		Preview.Value = session.HasScore ? preview : null;
		var summary = session.ScoreSummary;

		HasScore.Value = session.HasScore;
		Title.Value = summary?.Title ?? string.Empty;
		Composer.Value = summary?.Composer ?? string.Empty;
		Duration.Value = summary?.Duration ?? string.Empty;
		ModeLetter.Value = session.WindowMode.Length > 0
			? char.ToUpperInvariant(session.WindowMode[0]).ToString()
			: string.Empty;
		InPlayback.Value = session.InPlayback;
		IsRecording.Value = session.IsRecording;
		NoteInputActive.Value = session.NoteInputActive;

		var gridValue = UndottedNoteValue(session.RhythmicGridResolution, out var dotted);
		var gridFraction = FriendlyGridResolution(gridValue);
		GridResolution.Value = dotted && gridFraction.Length > 0 ? $"{gridFraction}." : gridFraction;
		GridIcon.Value = DoricoNoteOutput.TryParseDoricoNoteValue(gridValue) is { } grid
			? (dotted ? DottedDurationIcons : DurationIcons).GetValueOrDefault(grid)
			: null;
		RecentFile.Value = session.RecentFileName(0);
	}

	public static ScoreWidgetViewModel Sample()
	{
		var model = new ScoreWidgetViewModel();
		model.HasScore.Value = true;
		model.Title.Value = "Liebesträume";
		model.Composer.Value = "Franz Liszt";
		model.Duration.Value = "4′ 20.4″";
		model.ModeLetter.Value = "W";
		model.GridResolution.Value = "1/8";
		return model;
	}

	/// name without its "dotted" marker, e.g. "kDottedQuaver" as "kQuaver".
	private static string UndottedNoteValue(string doricoNoteValue, out bool dotted)
	{
		var undotted = DottedMarker().Replace(doricoNoteValue, string.Empty);
		dotted = undotted.Length != doricoNoteValue.Length;
		return undotted;
	}

	[GeneratedRegex("dotted", RegexOptions.IgnoreCase)]
	private static partial Regex DottedMarker();

	///name as a fraction, e.g. "kCrotchet" as "q".</summary>
	private static string FriendlyGridResolution(string doricoNoteValue) => doricoNoteValue.ToLowerInvariant() switch
	{
		"" => string.Empty,
		"kbreve" => "wh",
		"ksemibreve" => "sb",
		"kminim" => "hf",
		"kcrotchet" => "q",
		"kquaver" => "1/8",
		"ksemiquaver" => "1/16",
		"kdemisemiquaver" => "1/32",
		"khemidemisemiquaver" => "1/64",
		_ => doricoNoteValue.TrimStart('k'),
	};
}
