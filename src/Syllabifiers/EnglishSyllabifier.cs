using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DoriDeck.Services;
using Serilog;

namespace DoriDeck.Syllabifiers;

internal sealed class EnglishSyllabifier(ILogger logger) : ILyricSyllabifier
{
	private static readonly Regex EnglishWordRegex = new(
		@"[A-Za-z]+(?:['’][A-Za-z]+)*",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private readonly Lazy<Dictionary<string, string>> _mobyDictionary =
		new(() => LoadMobyDictionary(logger.ForContext<EnglishSyllabifier>()));
	private readonly UserDictionary _userDictionary = new("_Words_EN.txt");

	private static Dictionary<string, string> LoadMobyDictionary(ILogger logger)
	{
		var pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		var pathToMobyTxt = Path.Combine(pluginFolder ?? string.Empty, "mhyph.txt");

		var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		if (!File.Exists(pathToMobyTxt))
		{
			logger.Warning("English syllabification dictionary was not found at {Path}.", pathToMobyTxt);
			return dictionary;
		}

		foreach (var line in File.ReadLines(pathToMobyTxt, Encoding.UTF8))
		{
			var trimmed = line.Trim();
			if (string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			var key = RemoveSyllableSeparators(trimmed);
			if (key.Length <= 3)
			{
				continue;
			}

			dictionary[key] = ReplaceSyllableSeparators(trimmed);
		}

		return dictionary;
	}

	public string Syllabify(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}

		var dictionary = _mobyDictionary.Value;
		if (dictionary.Count == 0)
		{
			return text;
		}

		return EnglishWordRegex.Replace(text, match => SyllabifyWord(match.Value, dictionary));
	}

	private string SyllabifyWord(string word, IReadOnlyDictionary<string, string> dictionary)
	{
		if (_userDictionary.TryGetValue(word, out var userSyllabified))
		{
			return UserDictionary.MatchCase(word, userSyllabified);
		}

		if (dictionary.TryGetValue(word, out var syllabified))
		{
			return UserDictionary.MatchCase(word, syllabified);
		}

		if (word.EndsWith('s'))
		{
			var stem = word[..^1];
			if (dictionary.TryGetValue(stem, out var stemSyllabified))
			{
				return UserDictionary.MatchCase(stem, stemSyllabified) + "s";
			}
		}

		if (word.Contains('\'') || word.Contains('’'))
		{
			return Regex.Replace(word, @"[A-Za-z]+", match => SyllabifySimpleWord(match.Value, dictionary));
		}

		return word;
	}

	private static string SyllabifySimpleWord(string word, IReadOnlyDictionary<string, string> dictionary) =>
		dictionary.TryGetValue(word, out var syllabified) ? UserDictionary.MatchCase(word, syllabified) : word;

	private static string RemoveSyllableSeparators(string value) =>
		value.Replace("·", string.Empty).Replace("•", string.Empty).Replace("-", string.Empty);

	private static string ReplaceSyllableSeparators(string value) =>
		value.Replace("·", "-").Replace("•", "-");
}
