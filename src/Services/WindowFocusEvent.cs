using System.Text;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace DoriDeck.Services;

internal static class WindowFocusEvent
{
	public const string Id = "daw-window-focused";

	public const string ApplicationParameter = "application";
	public const string TitleParameter = "title";

	// Process-name prefixes, also published as the application name.
	public static readonly string[] Applications = ["Dorico", "Cubase", "Nuendo"];

	public static EventDefinition Definition { get; } = new()
	{
		Id = Id,
		Name = Strings.Events.WindowFocused.Name(),
		Description = Strings.Events.WindowFocused.Description(),
		ConfigurationParameters =
		[
			ApplicationChoice(),
			ActionParameter.Text(
				TitleParameter,
				label: Strings.Events.WindowFocused.Title.Label(),
				description: Strings.Events.WindowFocused.Title.Description(),
				placeholder: Strings.Events.WindowFocused.Title.Placeholder()),
		],
		PayloadParameters =
		[
			ApplicationChoice(),
			ActionParameter.Text(TitleParameter, label: Strings.Events.WindowFocused.Title.Label()),
		],
	};

	public static Dictionary<string, object?> Payload(string application, string title) => new()
	{
		[ApplicationParameter] = application,
		[TitleParameter] = title,
	};

	private static ActionParameter ApplicationChoice() =>
		ActionParameter.Choice(
			ApplicationParameter,
			options:
			[
				.. Applications.Select(app => new ActionParameterOption { Value = app, Label = LocalizedText.FromLiteral(app) }),
			],
			label: Strings.Events.WindowFocused.Application.Label(),
			description: Strings.Events.WindowFocused.Application.Description());
}

internal sealed class WindowFocusRules
{
	private readonly IReadOnlyList<(EventBindingValue? Application, EventBindingValue? Title)> _rules;

	private WindowFocusRules(IReadOnlyList<(EventBindingValue?, EventBindingValue?)> rules) => _rules = rules;

	public bool IsEmpty => _rules.Count == 0;

	public static WindowFocusRules From(IReadOnlyList<EventBinding> bindings) => new(
	[
		.. bindings
			.Where(binding => string.Equals(binding.EventId, WindowFocusEvent.Id, StringComparison.Ordinal))
			.Select(binding => (
				binding.Parameters.GetValueOrDefault(WindowFocusEvent.ApplicationParameter),
				binding.Parameters.GetValueOrDefault(WindowFocusEvent.TitleParameter))),
	]);

	// Key of the matching triggers (null for none); equal keys mean nothing to publish.
	public string? Match(string application, string title)
	{
		StringBuilder? key = null;
		for (var i = 0; i < _rules.Count; i++)
		{
			var (applicationCondition, titleCondition) = _rules[i];
			var applicationMatch = Matches(applicationCondition, application);
			var titleMatch = Matches(titleCondition, title);
			if (applicationMatch == false || titleMatch == false)
			{
				continue;
			}

			key ??= new StringBuilder();
			key.Append(i).Append(';');
			if (applicationMatch is null || titleMatch is null)
			{
				// Can't tell whether the host would fire it for this title, so every title counts as a change.
				key.Append(title).Append(';');
			}
		}

		return key?.ToString();
	}

	// Null for an operator this doesn't understand.
	private static bool? Matches(EventBindingValue? condition, string candidate)
	{
		if (condition is null)
		{
			return true;
		}

		switch (condition.Operator)
		{
			case "isEmpty":
				return candidate.Length == 0;
			case "isNotEmpty":
				return candidate.Length > 0;
			case "isAvailable":
				return true;
			case "isNotAvailable":
				return false;
		}

		if (ExpectedText(condition.Value) is not { } expected)
		{
			// A comparison without a value filters nothing.
			return true;
		}

		return (condition.Operator ?? "==") switch
		{
			"==" => candidate.Equals(expected, StringComparison.OrdinalIgnoreCase),
			"!=" => !candidate.Equals(expected, StringComparison.OrdinalIgnoreCase),
			"contains" => candidate.Contains(expected, StringComparison.OrdinalIgnoreCase),
			"startsWith" => candidate.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
			_ => null,
		};
	}

	private static string? ExpectedText(JsonElement? value) => value switch
	{
		{ ValueKind: JsonValueKind.String } text => text.GetString(),
		{ ValueKind: JsonValueKind.Number } number => number.GetRawText(),
		_ => null,
	};
}
