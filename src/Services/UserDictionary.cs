using System.Text;

namespace DoriDeck.Services;

public sealed class UserDictionary
{
	private readonly Lazy<IReadOnlyDictionary<string, string>> _words;

	public UserDictionary(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

		_words = new Lazy<IReadOnlyDictionary<string, string>>(
			() => Load(fileName));
	}

	public IReadOnlyDictionary<string, string> Words => _words.Value;

	public static string DataDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"MacroDeck",
		"plugins",
		"com.sarti2004.dorideck",
		"data");

	public bool TryGetValue(string word, out string value)
	{
		return _words.Value.TryGetValue(word, out value!);
	}

	public static string MatchCase(
		string original,
		string syllabified)
	{
		if (string.IsNullOrEmpty(original) ||
			string.IsNullOrEmpty(syllabified))
		{
			return syllabified;
		}

		var result = new StringBuilder(syllabified.Length);
		int originalLetterIndex = 0;

		foreach (char character in syllabified)
		{
			if (!char.IsLetter(character))
			{
				result.Append(character);
				continue;
			}

			while (originalLetterIndex < original.Length &&
				   !char.IsLetter(original[originalLetterIndex]))
			{
				originalLetterIndex++;
			}

			if (originalLetterIndex >= original.Length)
			{
				result.Append(character);
				continue;
			}

			char originalCharacter = original[originalLetterIndex];

			result.Append(
				char.IsUpper(originalCharacter)
					? char.ToUpperInvariant(character)
					: char.ToLowerInvariant(character));

			originalLetterIndex++;
		}

		return result.ToString();
	}

	private static IReadOnlyDictionary<string, string> Load(string fileName)
	{
		string pluginDataFolder = DataDirectory;

		Directory.CreateDirectory(pluginDataFolder);
		string filePath = Path.Combine(pluginDataFolder, fileName);

		var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		if (!File.Exists(filePath))
		{
			File.WriteAllText(
				filePath,
				"## Format: One hyphenated word per line" + Environment.NewLine,
				Encoding.UTF8);
		}

		foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
		{
			string value = line.Trim();

			if (string.IsNullOrWhiteSpace(value) || value.StartsWith("##", StringComparison.Ordinal))
			{
				continue;
			}

			string key = RemoveSeparators(value);

			if (!string.IsNullOrWhiteSpace(key))
			{
				dictionary[key] = value;
			}
		}

		return dictionary;
	}

	private static string RemoveSeparators(string value)
	{
		return value.Replace("-", string.Empty);
	}
}