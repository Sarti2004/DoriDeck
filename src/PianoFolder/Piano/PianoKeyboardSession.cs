using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.PianoFolder.Piano;

/// <summary>
/// Adapts a <see cref="UiView"/> (the reactive DSL runtime) to the wire-level <see cref="IUiSession"/>
/// contract the host talks to - forwarding <c>BuildTree</c>/<c>DrainPatches</c>/<c>Changed</c>/<c>Dispatch</c>
/// straight through, and translating the view's <c>HandlerFaulted</c> event into <c>IUiSession.Faulted</c>.
/// One instance is created per folder-view session by <see cref="DoriDeck.PianoFolder.PluginIntegration"/>.
/// </summary>
public sealed class PianoKeyboardSession : IUiSession
{
    private readonly UiView _view;
    private EventHandler<UiSessionFaultedEventArgs>? _faulted;

    public PianoKeyboardSession(UiView view)
    {
        _view = view;
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
        return ValueTask.CompletedTask;
    }

    private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
        _faulted?.Invoke(this, new UiSessionFaultedEventArgs(
            $"The '{e.EventName}' handler on '{e.NodeId}' faulted.", e.Exception));
}
