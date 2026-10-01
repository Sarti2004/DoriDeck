using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace DoriDeck.Syllabifiers;

/// <summary>
/// Detects the language of a lyric line and syllabifies each word in it.
/// </summary>
internal sealed class LyricProcessor(ILogger logger)
{
	private readonly ILogger _logger = logger.ForContext<LyricProcessor>();

	public string Process(string fullText, string language)
	{
		if (string.IsNullOrWhiteSpace(fullText))
		{
			return fullText;
		}

		var firstWord = GetFirstWord(fullText);
		var detectedLanguage = Regex.IsMatch(firstWord, @"^[а-яА-ЯёЁ]") ? "RU" : "EN";
		_logger.Information(
			"Insert Lyrics detected language {DetectedLanguage}, configured language {ConfiguredLanguage}.",
			detectedLanguage,
			language);

		ILyricSyllabifier engine = detectedLanguage == "RU"
			? new RussianSyllabifier()
			: language switch
			{
				"EN" => new EnglishSyllabifier(_logger),
				"FI" => new FinnishSyllabifier(),
				"DE" => new GermanSyllabifier(),
				_ => new LatinSyllabifier(),
			};

		return RunSyllabification(fullText, engine);
	}

	private static string GetFirstWord(string text)
	{
		var match = Regex.Match(text, @"\b[a-zA-Zа-яА-ЯёЁ]+\b");
		return match.Success ? match.Value : "A";
	}

	private static string RunSyllabification(string fullText, ILyricSyllabifier engine)
	{
		var tokens = Regex.Split(fullText, @"(\([^)]*\)|\s+|[.,!?;:""\-])");
		var result = new StringBuilder();

		foreach (var token in tokens)
		{
			if (string.IsNullOrEmpty(token))
			{
				continue;
			}

			var isPunctuationOrWhitespace = Regex.IsMatch(token, @"^[.,!?;:""\-\s]+$");
			var isBracketedText = Regex.IsMatch(token, @"^\([^)]*\)$");

			result.Append(isPunctuationOrWhitespace || isBracketedText ? token : engine.Syllabify(token));
		}

		return result.ToString();
	}
}

internal interface ILyricSyllabifier
{
	string Syllabify(string word);
}
