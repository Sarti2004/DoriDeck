using System.Text;
using System.Text.RegularExpressions;

using DoriDeck.Services;

namespace DoriDeck.Syllabifiers;

/// <summary>
/// Splits Finnish words into syllables.
/// This is intended as a lyrics helper, not as a complete morphological or pronunciation dictionary.
/// see https://kielikello.fi/tavun-rajat/ https://elon.io/grammar/finnish/gradation/open-closed-syllable
/// </summary>
public class FinnishSyllabifier : ILyricSyllabifier
{
    private const string Vowels = "aeiouyäöAEIOUYÄÖ";

    // Diphthongs that may occur in any syllable.
    private static readonly HashSet<string> GeneralDiphthongs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai", "ei", "oi", "ui", "yi", "äi", "öi",
        "au", "eu", "iu", "iy", "ou",
        "ey", "äy", "öy"
    };

    // These opening diphthongs normally occur only in the first syllable.
    private static readonly HashSet<string> FirstSyllableDiphthongs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ie", "uo", "yö"
    };

    private static readonly HashSet<string> LongVowels = new(StringComparer.OrdinalIgnoreCase)
    {
        "aa", "ee", "ii", "oo", "uu", "yy", "ää", "öö"
    };

    private readonly UserDictionary _userDictionary = new(fileName: "_Words_FI.txt");

    // apostrophe cases: vaa'an, rei'ittää and ruo'on.
    private static readonly Regex FinnishWordRegex = new(
        @"\p{L}+(?:['’]\p{L}+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly FinnishUySequenceMode _uySequenceMode;

    public FinnishSyllabifier(
        FinnishUySequenceMode uySequenceMode = FinnishUySequenceMode.SplitCommonSuffixes)
    {
        _uySequenceMode = uySequenceMode;
    }

    public string Syllabify(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return FinnishWordRegex.Replace(text, match => SyllabifyWord(match.Value));
    }

    private string SyllabifyWord(string word)
    {
        if (word.Length <= 1)
        {
            return word;
        }

        // First try the complete word, including an apostrophe.
        if (_userDictionary.TryGetValue(word, out string? syllabified))
        {
            return UserDictionary.MatchCase(word, syllabified);
        }

        // Preserve apostrophe
        int apostropheIndex = FindApostrophe(word);
        if (apostropheIndex >= 0)
        {
            string left = word[..apostropheIndex];
            string right = word[(apostropheIndex + 1)..];
            char apostrophe = word[apostropheIndex];

            return SyllabifyWord(left) + apostrophe + "-" + SyllabifyWord(right);
        }

        if (_userDictionary.TryGetValue(word, out string? syllabified_))
        {
            return UserDictionary.MatchCase(word, syllabified_);
        }

        // 1. Identify all vowel nuclei units
        var nuclei = new List<(int Start, int Length)>();
        int index = 0;
        while (index < word.Length)
        {
            if (!IsVowel(word[index]))
            {
                index++;
                continue;
            }

            bool isFirstNucleus = nuclei.Count == 0;
            if (index + 1 < word.Length &&
                IsVowel(word[index + 1]) &&
                IsInseparablePair(word, index, isFirstNucleus))
            {
                nuclei.Add((index, 2));
                index += 2;
            }
            else
            {
                nuclei.Add((index, 1));
                index += 1;
            }
        }

        if (nuclei.Count <= 1)
        {
            return word;
        }

        // 2. Calculate cuts based on Finnish consonant rules
        var sb = new StringBuilder();
        int cursor = 0;

        for (int i = 0; i < nuclei.Count - 1; i++)
        {
            var (currentStart, currentLength) = nuclei[i];
            int currentNucleusEnd = currentStart + currentLength;
            var (nextNucleusStart, _) = nuclei[i + 1];

            // Count consonants between this nucleus and the next
            int consonantCount = nextNucleusStart - currentNucleusEnd;
            int breakPosition;

            if (consonantCount == 0)
            {
                // No consonants (hiatus / split diphthongs like e-u): cut between vowels
                breakPosition = currentNucleusEnd;
            }
            else
            {
                // 1 or more consonants: always cut right before the LAST consonant
                breakPosition = nextNucleusStart - 1;
            }

            sb.Append(word, cursor, breakPosition - cursor);
            sb.Append('-');
            cursor = breakPosition;
        }

        sb.Append(word, cursor, word.Length - cursor);
        return sb.ToString();
    }


    private bool IsInseparablePair(string word, int index, bool isFirstNucleus)
    {
        string pair = word.Substring(index, 2);

        if (LongVowels.Contains(pair))
        {
            return true;
        }

        if (FirstSyllableDiphthongs.Contains(pair))
        {
            return isFirstNucleus;
        }

        if (!GeneralDiphthongs.Contains(pair))
        {
            return false;
        }

        if (_uySequenceMode == FinnishUySequenceMode.SplitCommonSuffixes &&
            !isFirstNucleus &&
            IsLikelyUySuffixSequence(word, index))
        {
            return false;
        }

        return true;
    }

    private static bool IsLikelyUySuffixSequence(string word, int index)
    {
        char suffixVowel = char.ToLowerInvariant(word[index + 1]);
        if (suffixVowel is not ('u' or 'y'))
        {
            return false;
        }

        string remainder = word[(index + 1)..].ToLowerInvariant();

        // Common forms of the productive -us/-ys type suffix. Keeping the
        // patterns specific avoids splitting ordinary later diphthongs such
        // as the "au" in palautus.
        string[] continuations = suffixVowel == 'u'
            ? new[] { "us", "uden", "utta", "uteen", "udessa", "udesta", "udella", "udelta",
                      "udelle", "udeksi", "utena", "uks", "uksine" }
            : new[] { "ys", "yden", "yttä", "yteen", "ydessä", "ydestä", "ydellä", "ydeltä",
                      "ydelle", "ydeksi", "ytenä", "yks", "yksine" };

        foreach (string continuation in continuations)
        {
            if (remainder.StartsWith(continuation, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindApostrophe(string word)
    {
        int straight = word.IndexOf('\'');
        int curly = word.IndexOf('’');

        if (straight < 0)
        {
            return curly;
        }

        if (curly < 0)
        {
            return straight;
        }

        return Math.Min(straight, curly);
    }

    private static bool IsVowel(char c)
    {
        return Vowels.IndexOf(c) >= 0;
    }
}

public enum FinnishUySequenceMode
{
    /// <summary>
    /// Split common suffix-like sequences, for example rakkaus -> rak-ka-us
    /// and rakkauden -> rak-ka-u-den. 
    /// </summary>
    SplitCommonSuffixes,

    /// <summary>
    /// Keep all otherwise valid diphthongs together.
    /// </summary>
    KeepDiphthongs
}
