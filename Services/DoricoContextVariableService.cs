using DoricoNet;
using DoricoNet.Responses;
using SuchByte.MacroDeck.Variables;

namespace DoriDeck.Services;

public sealed class DoricoContextVariableService
{
    private readonly Main _plugin;
    private readonly IFlowResolver _flowResolver;

    public DoricoContextVariableService(Main plugin, IFlowResolver flowResolver)
    {
        _plugin = plugin;
        _flowResolver = flowResolver;
    }

    public void SaveStatusVariables(StatusResponse status)
    {
        SetBoolVariable(Main.HasScoreVariableName, status.HasScore);
        SetBoolVariable(Main.HasSelectionVariableName, status.HasSelection);
        SetIntVariable(Main.ActiveOpenScoreIDVariableName, status.ActiveOpenScoreID);
        var noteInputModeRaw = status.NoteInputMode.ToString() ?? string.Empty;
        SetStringVariable(Main.NoteInputModeVariableName, noteInputModeRaw.Trim().TrimStart('k').Replace("Mode", string.Empty));
        SetStringVariable(Main.AccidentalVariableName, status.Accidental.ToString() ?? string.Empty);

        var rawMode = status.WindowMode.ToString();
        SetStringVariable(Main.ModeRawVariableName, rawMode);
        SetStringVariable(Main.ModeVariableName, FriendlyDoricoMode(rawMode));
    }

    public void SaveDisconnectedContextVariables(ref int flowsCount)
    {
        SetBoolVariable(Main.HasScoreVariableName, false);
        SetBoolVariable(Main.HasSelectionVariableName, false);
        SetStringVariable(Main.ModeVariableName, string.Empty);
        SetStringVariable(Main.ModeRawVariableName, string.Empty);
        SetStringVariable(Main.CurrentFlowIdVariableName, string.Empty);
        SetStringVariable(Main.CurrentFlowNameVariableName, string.Empty);
        SetStringVariable(Main.ActiveOpenScoreIDVariableName, string.Empty);
        SetStringVariable(Main.NoteInputModeVariableName, string.Empty);
        SetStringVariable(Main.AccidentalVariableName, string.Empty);
        SetStringVariable(Main.FlowCountVariableName, "0");
        flowsCount = 0;
    }

    public async Task SaveFlowVariablesAsync(IDoricoRemote remote, StatusResponse? status, Action<int> setFlowsCount)
    {
        if (status?.HasScore != true)
        {
            SetStringVariable(Main.CurrentFlowIdVariableName, string.Empty);
            SetStringVariable(Main.CurrentFlowNameVariableName, string.Empty);
            SetStringVariable(Main.FlowCountVariableName, "0");
            setFlowsCount(0);
            return;
        }

        var flowsResponse = await remote.GetFlowsAsync();
        var flows = flowsResponse?.Flows?.Cast<object>().ToList() ?? new List<object>();

        setFlowsCount(flows.Count);
        SetStringVariable(Main.FlowCountVariableName, flows.Count.ToString());

        var activeWindowTitle = ActiveWindowReader.GetActiveDoricoWindowTitle();
        var currentFlow = _flowResolver.ResolveCurrentFlow(flows, activeWindowTitle);

        SetStringVariable(Main.CurrentFlowIdVariableName, _flowResolver.GetFlowId(currentFlow));
        SetStringVariable(Main.CurrentFlowNameVariableName, _flowResolver.GetFlowName(currentFlow));
    }

    public void UpdateConnectionVariable(bool isConnected)
    {
        VariableManager.SetValue(Main.ConnectionVariableName, isConnected, VariableType.Bool, _plugin, Array.Empty<string>());
        VariableManager.SetValue(Main.TupletModeVariableName, false, VariableType.Bool, _plugin, Array.Empty<string>());
    }
    private static string FriendlyDoricoMode(string rawMode)
        => rawMode.Trim().TrimStart('k').Replace("Mode", string.Empty);

    private void SetStringVariable(string name, string value)
        => VariableManager.SetValue(name, value ?? string.Empty, VariableType.String, _plugin, Array.Empty<string>());

    private void SetIntVariable(string name, int value)
        => VariableManager.SetValue(name, value, VariableType.Integer, _plugin, Array.Empty<string>());

    private void SetBoolVariable(string name, bool value)
        => VariableManager.SetValue(name, value, VariableType.Bool, _plugin, Array.Empty<string>());
}
