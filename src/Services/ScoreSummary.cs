using System.Text.Json;

namespace DoriDeck.Services;

public sealed record ScoreSummary(string Title, string Composer, string Duration)
{
	public static ScoreSummary FromJson(string json, string scorePath)
	{
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;

		// Dorico 5 and earlier prefix the keys with "k".
		var title = Read(root, "title", "kTitle");
		return new ScoreSummary(
			title.Length > 0 ? title : Path.GetFileNameWithoutExtension(scorePath),
			Read(root, "composer", "kComposer"),
			Read(root, "projectDuration"));
	}

	private static string Read(JsonElement root, string name, string fallbackName)
	{
		var value = Read(root, name);
		return value.Length > 0 ? value : Read(root, fallbackName);
	}

	private static string Read(JsonElement root, string name) =>
		root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()!.Trim()
			: string.Empty;
}
