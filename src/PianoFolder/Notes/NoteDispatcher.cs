using MacroDeck.Localization;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace DoriDeck.PianoFolder.Notes;

public sealed class NoteDispatcher(INoteOutput output, ILogger logger) : INoteDispatcher
{
    private readonly ILogger _logger = logger.ForContext<NoteDispatcher>();

    public async Task<ActionResult> DispatchAsync(string noteId, int transposeSemitones, CancellationToken cancellationToken)
    {
        if (!PianoKeyboardModel.TryGetKey(noteId, out var key))
        {
            _logger.Warning("Rejected unknown note id {NoteId}.", noteId);
            return ActionResult.Failed(
                ActionErrorCodes.InvalidParameter,
                LocalizedText.FromLiteral($"'{noteId}' is not a recognized note id."));
        }

        var midiNumber = key.MidiNumber + PianoKeyboardModel.ClampTranspose(transposeSemitones);

        return await SendAsync(
            ct => output.SendNoteOnAsync(midiNumber, ct),
            $"{noteId} ({midiNumber})",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ActionResult> DispatchDurationAsync(NoteDuration duration, CancellationToken cancellationToken) =>
        SendAsync(ct => output.SendDurationAsync(duration, ct), $"duration {duration}", cancellationToken);

    public Task<ActionResult> DispatchRhythmDotsAsync(int rhythmDots, CancellationToken cancellationToken) =>
        SendAsync(ct => output.SendRhythmDotsAsync(rhythmDots, ct), $"rhythm dots {rhythmDots}", cancellationToken);

    public Task<ActionResult> DispatchReturnAsync(CancellationToken cancellationToken) =>
        SendAsync(output.SendReturnAsync, "Return", cancellationToken);

    public Task<ActionResult> DispatchForwardAsync(CancellationToken cancellationToken) =>
        SendAsync(output.SendForwardAsync, "Forward", cancellationToken);

    public NoteDuration? CurrentDuration => output.CurrentDuration;

    public event Action<NoteDuration>? DurationChanged
    {
        add => output.DurationChanged += value;
        remove => output.DurationChanged -= value;
    }

    public bool? NoteInputActive => output.NoteInputActive;

    public event Action<bool>? NoteInputActiveChanged
    {
        add => output.NoteInputActiveChanged += value;
        remove => output.NoteInputActiveChanged -= value;
    }

    public int? RhythmDots => output.RhythmDots;

    public event Action<int>? RhythmDotsChanged
    {
        add => output.RhythmDotsChanged += value;
        remove => output.RhythmDotsChanged -= value;
    }

    private async Task<ActionResult> SendAsync(
        Func<CancellationToken, Task> send,
        string what,
        CancellationToken cancellationToken)
    {
        try
        {
            await send(cancellationToken).ConfigureAwait(false);
            return ActionResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Note output failed for {What}.", what);
            return ActionResult.Failed(ActionErrorCodes.ProviderError, LocalizedText.FromLiteral(exception.Message));
        }
    }
}
