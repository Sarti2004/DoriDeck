using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DoriDeck.Syllabifiers;

/// <summary>
/// Splits German words into syllables.
/// Optimized for lyrics input, not a complete morphological or pronunciation dictionary.
/// See https://de.wiktionary.org/wiki/Silbentrennung
/// </summary>
public class GermanSyllabifier : ILyricSyllabifier
{
    private static readonly Regex GermanWordRegex = new(
        @"[A-Za-zäöüÄÖÜß]+(?:['’][A-Za-zäöüÄÖÜß]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
        
    private const string Vowels = "aeiouyäöüAEIOUYÄÖÜ";

    // Two-letter sequences that form a single vowel sound and must never
    // be split. "ie" is included because it represents a long /i:/ sound
    // rather than two separate vowel sounds.
    private static readonly string[] Diphthongs =
    {
        "au", "ei", "ai", "eu", "äu", "ie"
    };

    // Consonant sequences (longest first) that spell a single sound and
    // must move together to the following syllable whenever they appear
    // at the end of a consonant cluster between two vowel sounds.
    private static readonly string[] UnsplittableConsonantGroups =
    {
        "tsch", "sch", "ck", "ch", "ph", "th", "sh", "ng", "qu"
    };

    // Consonant + liquid combinations that behave like a single onset
    // and move together to the following syllable, e.g. "Fe-der" but
    // "Ta-fel" keeps "fl" together, "Rast-platz" keeps "pl" together.
    private static readonly string[] ConsonantLiquidGroups =
    {
        "pl", "pr", "bl", "br", "tr", "dr", "kl", "kr", "gl", "gr",
        "fl", "fr", "vl", "vr"
    };

    private readonly UserDictionary _userDictionary = new(fileName: "_Words_DE.txt");

    public string Syllabify(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return GermanWordRegex.Replace(
            text,
            match => SyllabifyWord(match.Value));
    }

    private string SyllabifyWord(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length <= 1)
        {
            return word;
        }

        if (_userDictionary.TryGetValue(word, out string? syllabified))
        {
            return UserDictionary.MatchCase(word, syllabified);
        }


        string lowerWord = word.ToLowerInvariant();
        int length = word.Length;

        List<int> nucleusStarts = FindNucleusStarts(lowerWord, out int[] nucleusEnds);

        if (nucleusStarts.Count <= 1)
        {
            return word;
        }

        var hyphenPositions = new HashSet<int>();

        for (int k = 0; k < nucleusStarts.Count - 1; k++)
        {
            int clusterStart = nucleusEnds[k] + 1;
            int clusterEnd = nucleusStarts[k + 1] - 1;
            int clusterLength = clusterEnd - clusterStart + 1;

            int onsetLength = DetermineOnsetLength(lowerWord, clusterStart, clusterLength);
            hyphenPositions.Add(clusterStart + (clusterLength - onsetLength));
        }

        var result = new StringBuilder(length + hyphenPositions.Count);

        for (int i = 0; i < length; i++)
        {
            if (hyphenPositions.Contains(i) && i > 0)
            {
                result.Append('-');
            }

            result.Append(word[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Finds the start index of each vowel-sound nucleus, merging
    /// diphthongs into a single nucleus, and reports the end index of
    /// each nucleus via <paramref name="nucleusEnds"/>.
    /// </summary>
    private static List<int> FindNucleusStarts(string lowerWord, out int[] nucleusEnds)
    {
        var starts = new List<int>();
        var ends = new List<int>();
        int length = lowerWord.Length;

        int i = 0;
        while (i < length)
        {
            if (!IsVowel(lowerWord[i]))
            {
                i++;
                continue;
            }

            int start = i;
            int end = i;

            if (i + 1 < length && IsVowel(lowerWord[i + 1]) &&
                Diphthongs.Contains(lowerWord.Substring(i, 2)))
            {
                end = i + 1;
            }

            starts.Add(start);
            ends.Add(end);
            i = end + 1;
        }

        nucleusEnds = ends.ToArray();
        return starts;
    }

    /// <summary>
    /// Determines how many characters at the end of a consonant cluster
    /// (between two vowel nuclei) form the onset of the next syllable.
    /// </summary>
    private static int DetermineOnsetLength(string lowerWord, int clusterStart, int clusterLength)
    {
        if (clusterLength == 0)
        {
            return 0;
        }

        foreach (string group in UnsplittableConsonantGroups)
        {
            if (group.Length <= clusterLength &&
                EndsWith(lowerWord, clusterStart, clusterLength, group))
            {
                return group.Length;
            }
        }

        if (clusterLength >= 2)
        {
            string lastTwo = lowerWord.Substring(clusterStart + clusterLength - 2, 2);

            if (ConsonantLiquidGroups.Contains(lastTwo))
            {
                return 2;
            }
        }

        // Default: a single consonant forms the onset of the next
        // syllable, the rest stays with the coda of the previous one.
        return 1;
    }

    private static bool EndsWith(
        string lowerWord,
        int clusterStart,
        int clusterLength,
        string group)
    {
        int suffixStart = clusterStart + clusterLength - group.Length;
        return string.CompareOrdinal(lowerWord, suffixStart, group, 0, group.Length) == 0;
    }

    private static bool IsVowel(char character)
    {
        return Vowels.IndexOf(character) >= 0;
    }
}
