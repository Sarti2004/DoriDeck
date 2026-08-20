using System.Text;
using System.Text.RegularExpressions;
using SuchByte.MacroDeck.Logging;
using DoriDeck.Syllabifiers;

/// <summary>
/// Detects the language of a lyric line and syllabifies each word in it.
/// </summary>
public sealed class LyricProcessor
{
    private ILyricSyllabifier? _currentEngine;

    public string Process(string fullText, string language)
    {
        if (string.IsNullOrWhiteSpace(fullText)) return fullText;

        string firstWord = GetFirstWord(fullText);
        string detectedLanguage = Regex.IsMatch(firstWord, @"^[а-яА-ЯёЁ]") ? "RU" : "EN";
        MacroDeckLogger.Information("DoriDeck", "Insert Lyrics detected language: {0}, configured language: {1}", detectedLanguage, language);

        _currentEngine = detectedLanguage == "RU"
            ? new RussianSyllabifier()
            : language switch
            {
                "EN" => new EnglishSyllabifier(),
                "FI" => new FinnishSyllabifier(),
                "DE" => new GermanSyllabifier(),
                _ => new LatinSyllabifier()
            };

        return RunSyllabification(fullText);
    }

    private string GetFirstWord(string text)
    {
        var match = Regex.Match(text, @"\b[a-zA-Zа-яА-ЯёЁ]+\b");
        return match.Success ? match.Value : "A"; // Default English
    }

    private string RunSyllabification(string fullText)
    {
        string[] tokens = Regex.Split(fullText, @"(\([^)]*\)|\s+|[.,!?;:""\-])");
        StringBuilder result = new StringBuilder();

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token))
                continue;

            bool isPunctuationOrWhitespace =
                Regex.IsMatch(token, @"^[.,!?;:""\-\s]+$");

            bool isBracketedText =
                Regex.IsMatch(token, @"^\([^)]*\)$");

            if (isPunctuationOrWhitespace || isBracketedText)
            {
                result.Append(token);
            }
            else
            {
                result.Append(_currentEngine?.Syllabify(token));
            }
        }

        return result.ToString();
    }
}

public interface ILyricSyllabifier
{
    string Syllabify(string word);
}
