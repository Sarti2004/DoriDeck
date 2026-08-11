using System.Text;
using System.Text.RegularExpressions;
using SuchByte.MacroDeck.Logging;
using DoriDeck.Syllabifiers;

/// <summary>
/// Processes lyrics by detecting language and syllabifying words.
/// string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
// string pathToMoby = Path.Combine(pluginFolder, "mhyph.txt");

//var processor = new LyricProcessor(pathToMoby);
/// </summary>

public class LyricProcessor
{
    private ILyricSyllabifier? _currentEngine;

    public LyricProcessor()
    {
    }

    public string Process(string fullText, string language)
    {
        if (string.IsNullOrWhiteSpace(fullText)) return fullText;

        string firstWord = GetFirstWord(fullText);
        string _detectedLanguage = Regex.IsMatch(firstWord, @"^[а-яА-ЯёЁ]") ? "RU" : "EN";
        MacroDeckLogger.Information("DoriDeck", "Insert Lyrics detected language: {0}, configured language: {1}", _detectedLanguage, language);

        if (_detectedLanguage != "RU")
        {
            if (language == "EN")
            {
                _currentEngine = new EnglishSyllabifier();
            }
            else if (language == "FI")
            {
                _currentEngine = new FinnishSyllabifier();
            }
            else if (language == "DE")
            {
                _currentEngine = new GermanSyllabifier();
            }
            else
            {
                _currentEngine = new LatinSyllabifier();
            }
        }
        else
        {
            _currentEngine = new RussianSyllabifier();
        }


        return RunSyllabification(fullText); 
    }

    private string GetFirstWord(string text)
    {
        var match = Regex.Match(text, @"\b[a-zA-Zа-яА-ЯёЁ]+\b");
        return match.Success ? match.Value : "A"; // Default English
    }

    private string RunSyllabification(string fullText)
    {
        //string[] tokens = Regex.Split(fullText, @"(\s+|[.,!?;:()""\-])");
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






