using System.Reflection;

namespace DoriDeck.Services;

public interface IFlowResolver
{
    object? ResolveCurrentFlow(IReadOnlyList<object> flows, string? activeWindowTitle);
    string GetFlowId(object? flow);
    string GetFlowName(object? flow);
}

public sealed class FlowResolver : IFlowResolver
{
    public object? ResolveCurrentFlow(IReadOnlyList<object> flows, string? activeWindowTitle)
    {
        if (flows.Count == 0) return null;
        if (flows.Count == 1) return flows[0];

        if (string.IsNullOrWhiteSpace(activeWindowTitle)) return null;

        var expectedFlowTitle = activeWindowTitle.Split(" in ")[0].Trim();

        return flows.FirstOrDefault(flow =>
        {
            var flowTitle = GetPropertyString(flow, "flowName");
            return !string.IsNullOrWhiteSpace(flowTitle) &&
                   flowTitle.StartsWith(expectedFlowTitle, StringComparison.OrdinalIgnoreCase);
        });
    }

    public string GetFlowId(object? flow)
        => flow == null ? string.Empty : GetPropertyString(flow, "FlowID") ?? string.Empty;

    public string GetFlowName(object? flow)
        => flow == null ? string.Empty : GetPropertyString(flow, "flowName") ?? flow.ToString() ?? string.Empty;

    private static string? GetPropertyString(object target, string propertyName)
    {
        var prop = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var value = prop?.GetValue(target);
        return value?.ToString();
    }
}