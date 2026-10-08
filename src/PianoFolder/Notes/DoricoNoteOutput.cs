using System.Globalization;
using DoriDeck;
using ScoreInterface.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoriDeck.PianoFolder.Notes;

public sealed class DoricoNoteOutput : INoteOutput
{
    public const int DefaultGroupingWindowMilliseconds = 50; // must be > 0

    private readonly DoricoSession _session;
    private readonly TimeSpan _groupingWindow;
    private readonly object _sync = new();
    private readonly SortedSet<int> _pendingMidiNumbers = [];
    private readonly object _sendSync = new();

    private Task _sendTail = Task.CompletedTask;

    private TaskCompletionSource? _currentBatch;

    private bool _reconnectHandled;

    /// <summary>Whether the first note send has already checked note input. Only touched inside the send queue.</summary>
    private bool _noteInputStartHandled;

    public DoricoNoteOutput(DoricoSession session)
        : this(session, TimeSpan.FromMilliseconds(DefaultGroupingWindowMilliseconds))
    {
    }

    internal DoricoNoteOutput(DoricoSession session, TimeSpan groupingWindow)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _groupingWindow = groupingWindow;
        _session.DurationChanged += OnDoricoDurationChanged;
        _session.NoteInputActiveChanged += OnDoricoNoteInputActiveChanged;
        _session.Disconnected += OnDoricoDisconnected;
    }

    public NoteDuration? CurrentDuration => TryParseDoricoNoteValue(_session.Duration);

    public event Action<NoteDuration>? DurationChanged;

    public bool? NoteInputActive => _session.NoteInputActive;

    public event Action<bool>? NoteInputActiveChanged
    {
        add => _session.NoteInputActiveChanged += value;
        remove => _session.NoteInputActiveChanged -= value;
    }

    public int? RhythmDots => _session.RhythmDots;

    public event Action<int>? RhythmDotsChanged
    {
        add => _session.RhythmDotsChanged += value;
        remove => _session.RhythmDotsChanged -= value;
    }

    private void OnDoricoDurationChanged(string doricoNoteValue)
    {
        // leave the selection as is for unknown durations.
        if (TryParseDoricoNoteValue(doricoNoteValue) is { } duration)
        {
            DurationChanged?.Invoke(duration);
        }
    }

    private void OnDoricoDisconnected()
    {
        // A confirmed disconnect clears the flag so the next disconnected send tries to reconnect again.
        _ = QueueSendAsync(() =>
        {
            _reconnectHandled = false;
            return Task.CompletedTask;
        });
    }

    private void OnDoricoNoteInputActiveChanged(bool active)
    {
        // Once Dorico leaves note input, the next note send has to enter it again. Queued so the flag is
        // still only touched inside the send queue.
        if (!active)
        {
            _ = QueueSendAsync(() =>
            {
                _noteInputStartHandled = false;
                return Task.CompletedTask;
            });
        }
    }

    public async Task SendNoteOnAsync(int midiNumber, CancellationToken cancellationToken)
    {
        Task batchTask;

        lock (_sync)
        {
            _pendingMidiNumbers.Add(midiNumber);

            if (_currentBatch is null)
            {
                _currentBatch = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                _ = FlushBatchAsync(_currentBatch);
            }

            batchTask = _currentBatch.Task;
        }

        await batchTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushBatchAsync(TaskCompletionSource batch)
    {
        try
        {
            await Task.Delay(_groupingWindow).ConfigureAwait(false);

            int[] midiNumbers;

            lock (_sync)
            {
                midiNumbers = [.. _pendingMidiNumbers];
                _pendingMidiNumbers.Clear();

                if (ReferenceEquals(_currentBatch, batch))
                {
                    _currentBatch = null;
                }
            }

            await QueueSendAsync(() => SendMidiNoteInputAsync(midiNumbers)).ConfigureAwait(false);
            batch.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_currentBatch, batch))
                {
                    _currentBatch = null;
                    _pendingMidiNumbers.Clear();
                }
            }

            batch.TrySetException(exception);
        }
    }

    public Task SendDurationAsync(NoteDuration duration, CancellationToken cancellationToken) =>
        QueueSendAsync(() => SendCommandAsync(
            new Command(
                "NoteInput.NoteValue",
                new CommandParameter("LogDuration", DoricoNoteValue(duration)),new CommandParameter("Set", "true")))).WaitAsync(cancellationToken);

    public Task SendReturnAsync(CancellationToken cancellationToken) =>
        QueueSendAsync(() => SendCommandAsync(new Command("NoteInput.MoveLeft"))).WaitAsync(cancellationToken);

    public Task SendForwardAsync(CancellationToken cancellationToken) =>
        QueueSendAsync(() => SendCommandAsync(new Command("NoteInput.MoveRight"))).WaitAsync(cancellationToken);

    public Task SendRhythmDotsAsync(int rhythmDots, CancellationToken cancellationToken) =>
        QueueSendAsync(() => SendCommandAsync(
            new Command(
                "NoteInput.SetDotted",
                new CommandParameter("Value", "1"),
                new CommandParameter("Set", rhythmDots > 0 ? "true" : "false")))).WaitAsync(cancellationToken);

    /// <summary>The Dorico note value name for a duration selected in the note input row.</summary>
    private static string DoricoNoteValue(NoteDuration duration) => duration switch
    {
        NoteDuration.Whole => "kSemibreve",
        NoteDuration.Half => "kMinim",
        NoteDuration.Quarter => "kCrotchet",
        NoteDuration.Eighth => "kQuaver",
        NoteDuration.Sixteenth => "kSemiQuaver",
        NoteDuration.ThirtySecond => "kDemiSemiQuaver",
        _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "Not implemented."),
    };

    /// <summary>The note input row duration for a Dorico note value name, or <c>null</c> when it has none.</summary>
    private static NoteDuration? TryParseDoricoNoteValue(string doricoNoteValue) =>
        Enum.GetValues<NoteDuration>()
            .Cast<NoteDuration?>()
            .FirstOrDefault(duration => string.Equals(
                DoricoNoteValue(duration!.Value), doricoNoteValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Queues one Dorico send behind every earlier note batch and command, so the score sees them in the
    /// order they were pressed.
    /// </summary>
    private Task QueueSendAsync(Func<Task> send)
    {
        lock (_sendSync)
        {
            Task sendTask = SendAfterAsync(_sendTail, send);
            _sendTail = sendTask;
            return sendTask;
        }
    }

    private async Task SendAfterAsync(Task previousSend, Func<Task> send)
    {
        try
        {
            await previousSend.ConfigureAwait(false);
        }
        catch
        {
            // A failed send has already reported its error; it must not block later ones.
        }

        // No connection attempt here: while Dorico is not connected, SendCommandAsync drops the send.
        await send().ConfigureAwait(false);
    }

    private async Task SendMidiNoteInputAsync(int[] midiNumbers)
    {
        // Returning before the start check keeps a disconnected send from using up the one NoteInput.Start.
        if (!_session.Remote.IsConnected && !_reconnectHandled)
        {
            try
            {
                await _session.EnsureConnectedAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            _reconnectHandled = true;
        }
        if (!_session.Remote.IsConnected)
        {
            return;
        }

        // Only the first note send since Dorico last left note input (or ever) starts note input.
        if (!_noteInputStartHandled)
        {
            if (!_session.NoteInputActive)
            {
                await SendCommandAsync(new Command("NoteInput.Enter")).ConfigureAwait(false);
            }

            _noteInputStartHandled = true;
        }

        string midiPitches = string.Join("\\\\,", midiNumbers.Select(
            midi => midi.ToString(CultureInfo.InvariantCulture)));

        await SendCommandAsync(
            new Command(
                "NoteInput.MIDINoteInput",
                new CommandParameter("MIDIPitches", midiPitches))).ConfigureAwait(false);
    }

    /// <summary>Sends a command to Dorico, or does nothing while Dorico is not connected.</summary>
    private Task SendCommandAsync(Command command) =>
        _session.Remote.IsConnected ? _session.Remote.SendRequestAsync(command) : Task.CompletedTask;
}