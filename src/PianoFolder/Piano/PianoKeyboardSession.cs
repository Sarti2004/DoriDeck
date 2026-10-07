using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.PianoFolder.Piano;

public sealed class PianoKeyboardSession : IUiSession
{
    private readonly UiView _view;
    private readonly Action? _onDispose;
    private EventHandler<UiSessionFaultedEventArgs>? _faulted;

    /// <param name="onDispose">Releases anything the session's view model subscribed to outside the view.</param>
    public PianoKeyboardSession(UiView view, Action? onDispose = null)
    {
        _view = view;
        _onDispose = onDispose;
        _view.HandlerFaulted += OnHandlerFaulted;
    }

    public event EventHandler? Changed
    {
        add => _view.Changed += value;
        remove => _view.Changed -= value;
    }

    public event EventHandler<UiSessionFaultedEventArgs>? Faulted
    {
        add => _faulted += value;
        remove => _faulted -= value;
    }

    public UiTree BuildTree() => _view.Tree;

    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

    public ValueTask DisposeAsync()
    {
        _view.HandlerFaulted -= OnHandlerFaulted;
        _onDispose?.Invoke();
        return ValueTask.CompletedTask;
    }

    private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
        _faulted?.Invoke(this, new UiSessionFaultedEventArgs(
            $"The '{e.EventName}' handler on '{e.NodeId}' faulted.", e.Exception));
}
