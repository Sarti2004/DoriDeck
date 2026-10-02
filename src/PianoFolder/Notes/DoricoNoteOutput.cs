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
    public const int DefaultGroupingWindowMilliseconds = 50;

    private readonly DoricoSession _session;
    private readonly TimeSpan _groupingWindow;
    private readonly object _sync = new();
    private readonly SortedSet<int> _pendingMidiNumbers = [];
    private readonly object _sendSync = new();

    private Task _sendTail = Task.CompletedTask;

    private TaskCompletionSource? _currentBatch;

    public DoricoNoteOutput(DoricoSession session)
        : this(session, TimeSpan.FromMilliseconds(DefaultGroupingWindowMilliseconds))
    {
    }

    internal DoricoNoteOutput(DoricoSession session, TimeSpan groupingWindow)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (groupingWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groupingWindow),
                groupingWindow,
                "The note-grouping window must be greater than zero.");
        }

        _session = session;
        _groupingWindow = groupingWindow;
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

            await QueueBatchSendAsync(midiNumbers).ConfigureAwait(false);
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

    private Task QueueBatchSendAsync(int[] midiNumbers)
    {
        lock (_sendSync)
        {
            Task sendTask = SendBatchAfterAsync(_sendTail, midiNumbers);
            _sendTail = sendTask;
            return sendTask;
        }
    }

    private async Task SendBatchAfterAsync(Task previousSend, int[] midiNumbers)
    {
        try
        {
            await previousSend.ConfigureAwait(false);
        }
        catch
        {
            // A failed batch has already reported its error; it must not block later notes.
        }

        if (!await _session.EnsureConnectedAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("Could not connect to Dorico.");
        }

        string midiPitches = string.Join("\\\\,", midiNumbers.Select(
            midi => midi.ToString(CultureInfo.InvariantCulture)));

        await _session.Remote.SendRequestAsync(
            new Command(
                "NoteInput.MIDINoteInput",
                new CommandParameter("MIDIPitches", midiPitches))).ConfigureAwait(false);
    }
}