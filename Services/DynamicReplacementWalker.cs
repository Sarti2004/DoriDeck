using System.Diagnostics;
using System.Text.Json;
using DoricoNet;
using DoricoNet.Responses;
using Lea;
using SuchByte.MacroDeck.Logging;
using WindowsInput;

namespace DoriDeck.Services;

/// <summary>
/// Usage requirements:
/// - Dorico must be the foreground application.
/// - The user must select the first dynamic before RunAsync is called.
/// - The operation walks through the dynamics reachable with Right Arrow.
/// </summary>
public sealed class DynamicReplacementWalker : IDisposable
{
    private readonly Main _plugin;
    private readonly IEventAggregator _eventAggregator;
    private readonly DynamicReplacementWalkerOptions _options;
    private readonly SubscriptionToken _selectionSubscription;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _runCancellationLock = new();
    private readonly SelectionChangeTracker _selectionTracker = new();

    // H.InputSimulator instance used for all keyboard injection.

    private readonly IKeyboardService _keyboard = new KeyboardService();

    private readonly IApplicationFocusService _applicationFocus = new ApplicationFocusService();

    private CancellationTokenSource? _currentRunCancellation;
    private int _runScoreId;
    private bool _isDynamicSelected;
    private bool _isPlayingTechniqueEventSelected;
    private bool _isTextEventSelected;
    private bool _disposed;

    public DynamicReplacementWalker(
        Main plugin,
        IEventAggregator eventAggregator,
        DynamicReplacementWalkerOptions? options = null)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _eventAggregator = eventAggregator
            ?? throw new ArgumentNullException(nameof(eventAggregator));
        _options = options ?? new DynamicReplacementWalkerOptions();

        _selectionSubscription =
            _eventAggregator.Subscribe<SelectionChangedResponse>(
                OnSelectionChanged);
    }

    /// <summary>
    /// Cancels a currently running traversal.
    /// </summary>
    public void Cancel()
    {
        lock (_runCancellationLock)
        {
            _currentRunCancellation?.Cancel();
        }
    }

    /// <summary>
    /// Replaces exact dynamic-popover entries while walking forward.
    /// For example, RunAsync("mp", "f") changes only a plain "mp" entry.
    /// It does not change "mp dolce" or other compound entries.
    /// </summary>
    public async Task<DynamicReplacementResult> RunAsync(
        string sourceDynamic,
        string replacementDynamic,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateDynamicText(sourceDynamic, nameof(sourceDynamic));
        ValidateDynamicText(replacementDynamic, nameof(replacementDynamic));

        if (!await _runGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException(
                "A dynamic replacement operation is already running.");
        }

        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_runCancellationLock)
        {
            _currentRunCancellation = linkedCancellation;
        }

        var token = linkedCancellation.Token;
        ClipboardSnapshot? clipboardSnapshot = null;

        var visited = 0;
        var changed = 0;
        var stopReason = DynamicReplacementStopReason.Completed;
        string? previousNormalizedEntry = null;
        var consecutiveSameEntryCount = 0;

        sourceDynamic = sourceDynamic.Trim();

        try
        {
            if (!await _plugin.EnsureConnectedAsync())
            {
                throw new InvalidOperationException(
                    "DoriDeck is not connected to Dorico.");
            }

            var remote = _plugin.DoricoRemote
                ?? throw new InvalidOperationException(
                    "The Dorico remote service is unavailable.");

            EnsureDoricoIsForeground();

            var status = await remote
                .GetStatusAsync(token)
                .WaitAsync(_options.DoricoRequestTimeout, token);

            if (status is null || !status.HasScore)
            {
                throw new InvalidOperationException(
                    "Dorico does not have an active score.");
            }

            if (!status.HasSelection)
            {
                throw new InvalidOperationException(
                    "Select the first dynamic before running this action.");
            }

            _runScoreId = status.ActiveOpenScoreID;

            if (!await IsDynamicSelectedAsync(remote, token))
            {
                throw new InvalidOperationException(
                    "The current Dorico selection is not a dynamic.");
            }

            clipboardSnapshot = ClipboardSnapshot.Capture(
                _options.ClipboardRetryCount,
                _options.ClipboardRetryDelay);

#if DEBUG
            MacroDeckLogger.Information(
                _plugin,
                "Dynamic replacement started: '{0}' -> '{1}'",
                sourceDynamic,
                replacementDynamic);
#endif

            while (visited < _options.MaximumVisitedDynamics)
            {
                token.ThrowIfCancellationRequested();
                EnsureSameScoreIsActive(remote);
                EnsureDoricoIsForeground();

                // Re-check before opening a popover. This is the main guard
                // against Right Arrow selecting a note at the end.
                if (!await IsDynamicSelectedAsync(remote, token))
                {
                    stopReason =
                        DynamicReplacementStopReason.NextSelectionWasNotDynamic;
                    break;
                }

                string entry;
                try
                {
                    entry = await ReadSelectedDynamicAsync(token);
                }
                catch
                {
                    // Best effort only: close any popover that may still be open.
                    _keyboard.ReleaseModifiersSafely();
                    TrySendKey(VirtualKeyCode.ESCAPE);
                    throw;
                }

                visited++;

                if (string.Equals(
                        entry,
                        previousNormalizedEntry,
                        StringComparison.OrdinalIgnoreCase))
                {
                    consecutiveSameEntryCount++;
                }
                else
                {
                    previousNormalizedEntry = entry;
                    consecutiveSameEntryCount = 1;
                }

                // Dorico can keep selecting the final dynamic when Right Arrow
                // reaches the end of the flow. Stop after reading the same entry
                // three consecutive times instead of looping on that last item.
                if (consecutiveSameEntryCount >= _options.MaximumConsecutiveIdenticalEntries)
                {
                    _keyboard.Press(VirtualKeyCode.ESCAPE);
                    await Task.Delay(_options.PopoverCloseDelay, token);
                    stopReason =
                        DynamicReplacementStopReason.SameEntryRepeated;
                    break;
                }


                if (string.Equals(
                        entry,
                        sourceDynamic,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await ReplaceOpenPopoverTextAsync(
                        replacementDynamic,
                        token);

                    changed++;
                }
                else
                {
                    _keyboard.Press(VirtualKeyCode.ESCAPE);
                    await Task.Delay(_options.PopoverCloseDelay, token);
                }

                await _selectionTracker.WaitForSettleAsync(
                    _options.SelectionQuietPeriod,
                    _options.SelectionSettleMaximum,
                    token);

                var selectionVersionBeforeNavigation =
                    _selectionTracker.CurrentVersion;

                _keyboard.Press(VirtualKeyCode.RIGHT);

                var selectionChanged =
                    await _selectionTracker.WaitForChangeAfterAsync(
                        selectionVersionBeforeNavigation,
                        _options.NavigationTimeout,
                        token);

                if (!selectionChanged)
                {
                    stopReason =
                        DynamicReplacementStopReason.NoFurtherSelection;
                    break;
                }

                await Task.Delay(_options.AfterNavigationDelay, token);
            }

            if (visited >= _options.MaximumVisitedDynamics)
            {
                stopReason =
                    DynamicReplacementStopReason.SafetyLimitReached;
            }

            var result = new DynamicReplacementResult(
                visited,
                changed,
                stopReason);

            MacroDeckLogger.Information(
                _plugin,
                "Dynamic replacement finished. Visited: {0}; changed: {1}; stop reason: {2}",
                result.Visited,
                result.Changed,
                result.StopReason);

            return result;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            TrySendKey(VirtualKeyCode.ESCAPE);
            _keyboard.ReleaseModifiersSafely();

            var result = new DynamicReplacementResult(
                visited,
                changed,
                DynamicReplacementStopReason.Cancelled);

            MacroDeckLogger.Warning(
                _plugin,
                "Dynamic replacement cancelled. Visited: {0}; changed: {1}",
                result.Visited,
                result.Changed);

            return result;
        }
        finally
        {
            _runScoreId = 0;

            lock (_runCancellationLock)
            {
                if (ReferenceEquals(
                        _currentRunCancellation,
                        linkedCancellation))
                {
                    _currentRunCancellation = null;
                }
            }

            if (clipboardSnapshot is not null)
            {
                try
                {
                    clipboardSnapshot.Restore(
                        _options.ClipboardRetryCount,
                        _options.ClipboardRetryDelay);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error(
                        _plugin,
                        "Could not restore the clipboard after dynamic replacement: {0}",
                        ex.Message);
                }
            }

            TrySendKey(VirtualKeyCode.ESCAPE);
            _keyboard.ReleaseModifiersSafely();
            _runGate.Release();
        }
    }

    private async Task<string> ReadSelectedDynamicAsync(
        CancellationToken cancellationToken)
    {
        EnsureDoricoIsForeground();

        _keyboard.Press(VirtualKeyCode.RETURN);
        await Task.Delay(_options.PopoverOpenDelay, cancellationToken);

        // Retry Ctrl+A / Ctrl+C to handle transient focus or clipboard races.
        for (var attempt = 1; attempt <= _options.CopyRetryCount; attempt++)
        {
            EnsureDoricoIsForeground();

            // Select and copy the complete popover entry.
            if (_isTextEventSelected)
            {
                _keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                await Task.Delay(10, cancellationToken);
            }
            //_keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);

            var clipboardSequenceBeforeCopy =
                WindowAndClipboardInterop.GetClipboardSequence();

            _keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

            var copied = await WaitForClipboardChangeAsync(
                clipboardSequenceBeforeCopy,
                _options.ClipboardCopyTimeout,
                cancellationToken);

            if (copied)
            {
                var text = ClipboardSnapshot.ReadUnicodeText(
                    _options.ClipboardRetryCount,
                    _options.ClipboardRetryDelay);

                if (text is not null)
                {
                    return text.Trim();
                }
            }

            if (attempt < _options.CopyRetryCount)
            {
                MacroDeckLogger.Warning(
                    _plugin,
                    "ReadSelectedDynamicAsync: Copy attempt {0} failed; retrying.",
                    attempt);
                await Task.Delay(_options.CopyRetryDelay, cancellationToken);
            }
        }

        throw new TimeoutException(
            "Dorico did not copy popover text before the timeout.");
    }

    private async Task ReplaceOpenPopoverTextAsync(
        string replacementDynamic,
        CancellationToken cancellationToken)
    {
        EnsureDoricoIsForeground();

        _keyboard.PressChord(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
        await Task.Delay(10, cancellationToken);
        _keyboard.EnterText(replacementDynamic);
        await Task.Delay(40, cancellationToken);

        // Return commits the changed existing item.
        _keyboard.Press(VirtualKeyCode.RETURN);
        if (_isPlayingTechniqueEventSelected)
        {
            _keyboard.Press(VirtualKeyCode.RETURN);
            await Task.Delay(20, cancellationToken);
            _keyboard.Press(VirtualKeyCode.RETURN);
        }
        if (_isTextEventSelected)
        {
            _keyboard.Press(VirtualKeyCode.ESCAPE);
        }
        await Task.Delay(_options.PopoverCloseDelay, cancellationToken);
    }

    private async Task<bool> IsDynamicSelectedAsync(
        IDoricoRemote remote,
        CancellationToken cancellationToken)
    {
        var properties = await remote
            .GetPropertiesAsync(cancellationToken)
            .WaitAsync(_options.DoricoRequestTimeout, cancellationToken);


        var eventTypes = properties?.EventTypes?
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .ToList() ?? new List<string>();

        _isDynamicSelected = eventTypes.Any(_options.DynamicEventTypeMatcher);
        _isPlayingTechniqueEventSelected = eventTypes.Any(_options.PlayingTechniqueEventTypeMatcher);
        _isTextEventSelected = eventTypes.Any(_options.TextEventTypeMatcher);

        // PlayingTechniqueEvent doesn't work for some reason yet.
        return _isDynamicSelected ||
            _isPlayingTechniqueEventSelected ||
            _isTextEventSelected;
    }

    private void EnsureSameScoreIsActive(IDoricoRemote remote)
    {
        var currentStatus = remote.CurrentStatus;

        if (currentStatus is not null &&
            currentStatus.ActiveOpenScoreID != 0 &&
            currentStatus.ActiveOpenScoreID != _runScoreId)
        {
            throw new InvalidOperationException(
                "The active Dorico score changed during dynamic replacement.");
        }
    }

    private void OnSelectionChanged(SelectionChangedResponse response)
    {
        var runScoreId = Volatile.Read(ref _runScoreId);

        if (runScoreId != 0 && response.OpenScoreId != runScoreId)
        {
            return;
        }

        _selectionTracker.NotifySelectionChanged();
    }

    private async Task<bool> WaitForClipboardChangeAsync(
        uint previousSequence,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (WindowAndClipboardInterop.GetClipboardSequence() != previousSequence)
            {
                return true;
            }

            await Task.Delay(_options.ClipboardRetryDelay, cancellationToken);
        }

        return false;
    }


    private static void ValidateDynamicText(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A dynamic entry cannot be empty.",
                parameterName);
        }

        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException(
                "A dynamic entry cannot contain a line break.",
                parameterName);
        }
    }

    private void EnsureDoricoIsForeground()
    {
        _applicationFocus.EnsureForeground(
            "Dorico",
            "Dorico must remain the foreground application while dynamic replacement is running.");
    }

    private bool IsDoricoForeground() =>
        _applicationFocus.IsForeground("Dorico");


    // -------------------------------------------------------------------------
    // Keyboard helpers — all injection delegated to H.InputSimulator.
    // -------------------------------------------------------------------------


    private bool TrySendKey(VirtualKeyCode key)
    {
        try
        {
            if (!IsDoricoForeground())
            {
                return false;
            }

            _keyboard.Press(key);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(DynamicReplacementWalker));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();

        _eventAggregator.Unsubscribe<SelectionChangedResponse>(
            _selectionSubscription);

        _runGate.Dispose();

        lock (_runCancellationLock)
        {
            _currentRunCancellation?.Dispose();
            _currentRunCancellation = null;
        }
    }

    // -------------------------------------------------------------------------
    // Public result types
    // -------------------------------------------------------------------------

    public sealed record DynamicReplacementResult(
        int Visited,
        int Changed,
        DynamicReplacementStopReason StopReason);

    public enum DynamicReplacementStopReason
    {
        Completed,
        NoFurtherSelection,
        NextSelectionWasNotDynamic,
        SameEntryRepeated,
        SafetyLimitReached,
        Cancelled
    }

    public sealed record DynamicReplacementWalkerOptions
    {
        /// <summary>
        /// Matches Dorico event-type strings returned by getproperties.
        /// The default accepts event types containing "dynamic". kPlayingTechniqueEvent, kPauseEvent (fermata?), kTrillEvent, kImmediateTempoChangeEvent
        /// </summary>
        public Func<string, bool> DynamicEventTypeMatcher { get; init; } =
            eventType => eventType.Contains(
                "dynamic",
                StringComparison.OrdinalIgnoreCase);
        public Func<string, bool> PlayingTechniqueEventTypeMatcher { get; init; } =
            eventType => eventType.Equals(
                "kPlayingTechniqueEvent",
                StringComparison.OrdinalIgnoreCase);

        public Func<string, bool> TextEventTypeMatcher { get; init; } =
            eventType => eventType.Equals(
                "kTextEvent",
                StringComparison.OrdinalIgnoreCase);

        public int MaximumVisitedDynamics { get; init; } = 300;
        public int MaximumConsecutiveIdenticalEntries { get; init; } = 3;

        public TimeSpan DoricoRequestTimeout { get; init; } =
            TimeSpan.FromSeconds(5);

        public TimeSpan PopoverOpenDelay { get; init; } =
            TimeSpan.FromMilliseconds(40);

        public TimeSpan PopoverCloseDelay { get; init; } =
            TimeSpan.FromMilliseconds(100);

        public TimeSpan AfterNavigationDelay { get; init; } =
            TimeSpan.FromMilliseconds(50);

        public TimeSpan NavigationTimeout { get; init; } =
            TimeSpan.FromSeconds(1);

        /// <summary>
        /// How long to wait for the clipboard to change after Ctrl+C.
        /// Increased to 2 s for real-world reliability.
        /// </summary>
        public TimeSpan ClipboardCopyTimeout { get; init; } =
            TimeSpan.FromSeconds(2);

        public TimeSpan SelectionQuietPeriod { get; init; } =
            TimeSpan.FromMilliseconds(120);

        public TimeSpan SelectionSettleMaximum { get; init; } =
            TimeSpan.FromMilliseconds(600);

        public int ClipboardRetryCount { get; init; } = 3;

        public TimeSpan ClipboardRetryDelay { get; init; } =
            TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Number of Ctrl+A / Ctrl+C attempts per dynamic before giving up.
        /// </summary>
        public int CopyRetryCount { get; init; } = 3;

        /// <summary>
        /// Delay between successive Ctrl+A / Ctrl+C retry attempts.
        /// </summary>
        public TimeSpan CopyRetryDelay { get; init; } =
            TimeSpan.FromMilliseconds(200);
    }

}

