using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DoriDeck.Syllabifiers;

public class RussianSyllabifier : ILyricSyllabifier
{
    private const string Vowels = "аеёиоуыэюяАЕЁИОУЫЭЮЯ";

    public string Syllabify(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Process Russian words while preserving spaces,
        // punctuation, line breaks, and existing separators.
        return Regex.Replace(
            text,
            @"[А-Яа-яЁё]+",
            match => SyllabifyWord(match.Value));
    }

    private static string SyllabifyWord(string word)
    {
        var vowelPositions = new List<int>();

        for (int i = 0; i < word.Length; i++)
        {
            if (IsVowel(word[i]))
                vowelPositions.Add(i);
        }

        if (vowelPositions.Count <= 1)
            return word;

        var splitPositions = new HashSet<int>();

        for (int i = 0; i < vowelPositions.Count - 1; i++)
        {
            int currentVowel = vowelPositions[i];
            int nextVowel = vowelPositions[i + 1];

            int consonantCount = nextVowel - currentVowel - 1;
            int splitPosition;

            if (consonantCount == 0)
            {
                // Two adjacent vowels: по-ёт.
                splitPosition = nextVowel;
            }
            else if (consonantCount == 1)
            {
                // One consonant goes to the next syllable
                splitPosition = currentVowel + 1;
            }
            else
            {
                // For a consonant cluster, keep the first consonant
                // with the preceding syllable
                splitPosition = currentVowel + 2;

                // й, ь and ъ cannot begin a Russian syllable.
                while (splitPosition < nextVowel &&
                       IsNonStartingLetter(word[splitPosition]))
                {
                    splitPosition++;
                }
            }

            splitPositions.Add(splitPosition);
        }

        var result = new StringBuilder(word.Length + splitPositions.Count);

        for (int i = 0; i < word.Length; i++)
        {
            if (splitPositions.Contains(i))
                result.Append('-');

            result.Append(word[i]);
        }

        return result.ToString();
    }

    private static bool IsVowel(char character)
    {
        return Vowels.IndexOf(character) >= 0;
    }

    private static bool IsNonStartingLetter(char character)
    {
        return character is 'й' or 'Й'
            or 'ь' or 'Ь'
            or 'ъ' or 'Ъ';
    }
}