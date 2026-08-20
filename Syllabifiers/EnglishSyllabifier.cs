using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SuchByte.MacroDeck.Logging;

namespace DoriDeck.Syllabifiers;

public class EnglishSyllabifier : ILyricSyllabifier
{
    private static readonly Regex EnglishWordRegex = new(
        @"[A-Za-z]+(?:['’][A-Za-z]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Lazy<Dictionary<string, string>> _mobyDictionary = new(LoadMobyDictionary);

    private readonly UserDictionary _userDictionary = new(fileName: "_Words_EN.txt");

    private static Dictionary<string, string> LoadMobyDictionary()
    {
        string? pluginFolder =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        string pathToMobyTxt =
            Path.Combine(pluginFolder ?? string.Empty, "mhyph.txt");

        var dictionary = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        MacroDeckLogger.Information(
            "Insert Lyrics English dictionary: {0}",
            pathToMobyTxt);

        if (!File.Exists(pathToMobyTxt))
        {
            MacroDeckLogger.Warning(
                "English syllabification dictionary was not found: {0}",
                pathToMobyTxt);

            return dictionary;
        }

        foreach (string line in File.ReadLines(pathToMobyTxt, Encoding.UTF8))
        {
            string trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            string key = RemoveSyllableSeparators(trimmed);

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

        Dictionary<string, string> dictionary = _mobyDictionary.Value;

        if (dictionary.Count == 0)
        {
            return text;
        }

        return EnglishWordRegex.Replace(
            text,
            match => SyllabifyWord(match.Value, dictionary));
    }

    private string SyllabifyWord(
        string word,
        IReadOnlyDictionary<string, string> dictionary)
    {

        if (_userDictionary.TryGetValue(word, out string? syllabified_))
        {
            return UserDictionary.MatchCase(word, syllabified_);
        }
        
        // First try the complete word, including an apostrophe.
        if (dictionary.TryGetValue(word, out string? syllabified))
        {
            return MatchCase(word, syllabified);
        }
        if (word.EndsWith('s'))
        {
            word = word[..^1];
            if (dictionary.TryGetValue(word, out string? syllabified2))
            {
                return MatchCase(word, syllabified2) + "s";
            }
        }

        
        // Dictionaries often do not contain contractions such as
        // "don't" or "we're". Process the alphabetic parts while
        // preserving the apostrophe exactly.
        if (word.IndexOf('\'') >= 0 || word.IndexOf('’') >= 0)
        {
            return Regex.Replace(
                word,
                @"[A-Za-z]+",
                match => SyllabifySimpleWord(match.Value, dictionary));
        }

        return word;
    }

    private static string SyllabifySimpleWord(
        string word,
        IReadOnlyDictionary<string, string> dictionary)
    {
        if (dictionary.TryGetValue(word, out string? syllabified))
            return MatchCase(word, syllabified);

        return word;
    }

    private static string RemoveSyllableSeparators(string value)
    {
        return value
            .Replace("·", string.Empty)
            .Replace("•", string.Empty)
            .Replace("-", string.Empty);
    }

    private static string ReplaceSyllableSeparators(string value)
    {
        return value
            .Replace("·", "-")
            .Replace("•", "-");
    }

    private static string MatchCase(
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
}