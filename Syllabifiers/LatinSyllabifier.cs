using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DoriDeck.Syllabifiers;

public sealed class LatinSyllabifier : ILyricSyllabifier
{
    private enum TokenType
    {
        Consonant,
        VowelNucleus,
        VowelExtension,
        ConsonantalGlide,
        Separator
    }

    private static readonly HashSet<string> Diphthongs =
        new(StringComparer.Ordinal)
        {
            "ae",
            "oe",
            "au",
            "eu"
        };

    private static readonly HashSet<string> MutaCumLiquida =
        new(StringComparer.Ordinal)
        {
            "pl", "pr",
            "bl", "br",
            "tl", "tr",
            "dl", "dr",
            "cl", "cr",
            "gl", "gr",
            "fl", "fr"
        };

    private static readonly HashSet<string> ConsonantDigraphs =
        new(StringComparer.Ordinal)
        {
            "ch",
            "ph",
            "th"
        };

    private static readonly HashSet<string> EuHiatusWords =
        new(StringComparer.Ordinal)
        {
            "deus",
            "meus",
            "reus"
        };

    public string Syllabify(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (string.IsNullOrWhiteSpace(word) || word.Length <= 1)
        {
            return word;
        }

        AnalysisText analysis = CreateAnalysisText(word);

        if (analysis.Letters.Count <= 1)
        {
            return word;
        }

        TokenType[] tokenTypes = ParseTokenTypes(analysis);
        List<int> nuclei = FindNuclei(tokenTypes);

        if (nuclei.Count <= 1)
        {
            return word;
        }

        HashSet<int> splitAfterOriginalIndices = FindSplitPositions(
            analysis,
            tokenTypes,
            nuclei);

        return InsertHyphens(word, splitAfterOriginalIndices);
    }

    private static TokenType[] ParseTokenTypes(AnalysisText analysis)
    {
        int length = analysis.Letters.Count;
        var tokenTypes = new TokenType[length];

        for (int i = 0; i < length; i++)
        {
            char current = analysis.Letters[i].Normalized;

            if (!IsVowel(current))
            {
                tokenTypes[i] = IsLetter(current)
                    ? TokenType.Consonant
                    : TokenType.Separator;

                continue;
            }

            if (IsConsonantalUAfterQ(analysis, i))
            {
                tokenTypes[i] = TokenType.ConsonantalGlide;
                continue;
            }

            if (CanExtendPreviousNucleus(analysis, tokenTypes, i))
            {
                tokenTypes[i] = TokenType.VowelExtension;
                continue;
            }

            tokenTypes[i] = TokenType.VowelNucleus;
        }

        return tokenTypes;
    }

    private static bool IsConsonantalUAfterQ(
        AnalysisText analysis,
        int index)
    {
        if (analysis.Letters[index].Normalized != 'u')
        {
            return false;
        }

        if (index == 0 || index >= analysis.Letters.Count - 1)
        {
            return false;
        }

        char previous = analysis.Letters[index - 1].Normalized;
        char next = analysis.Letters[index + 1].Normalized;

        return previous == 'q' && IsVowel(next);
    }

    private static bool CanExtendPreviousNucleus(
        AnalysisText analysis,
        TokenType[] tokenTypes,
        int index)
    {
        if (index == 0)
        {
            return false;
        }

        if (tokenTypes[index - 1] != TokenType.VowelNucleus)
        {
            return false;
        }

        char first = analysis.Letters[index - 1].Normalized;
        char second = analysis.Letters[index].Normalized;

        string pair = string.Concat(first, second);

        if (!Diphthongs.Contains(pair))
        {
            return false;
        }

        if (pair == "eu" && EuHiatusWords.Contains(analysis.NormalizedWord))
        {
            return false;
        }

        return true;
    }

    private static List<int> FindNuclei(TokenType[] tokenTypes)
    {
        var nuclei = new List<int>();

        for (int i = 0; i < tokenTypes.Length; i++)
        {
            if (tokenTypes[i] == TokenType.VowelNucleus)
            {
                nuclei.Add(i);
            }
        }

        return nuclei;
    }

    private static HashSet<int> FindSplitPositions(
        AnalysisText analysis,
        TokenType[] tokenTypes,
        IReadOnlyList<int> nuclei)
    {
        var splitPositions = new HashSet<int>();

        for (int i = 0; i < nuclei.Count - 1; i++)
        {
            int currentNucleus = nuclei[i];
            int nextNucleus = nuclei[i + 1];

            int splitAfterAnalysisIndex = FindBoundary(
                analysis,
                tokenTypes,
                currentNucleus,
                nextNucleus);

            if (splitAfterAnalysisIndex < 0)
            {
                continue;
            }

            int originalIndex =
                analysis.Letters[splitAfterAnalysisIndex].OriginalIndex;

            splitPositions.Add(originalIndex);
        }

        return splitPositions;
    }

    private static int FindBoundary(
        AnalysisText analysis,
        TokenType[] tokenTypes,
        int currentNucleus,
        int nextNucleus)
    {
        int currentNucleusEnd = currentNucleus;

        while (currentNucleusEnd + 1 < nextNucleus &&
               tokenTypes[currentNucleusEnd + 1] ==
               TokenType.VowelExtension)
        {
            currentNucleusEnd++;
        }

        var intervening = new List<int>();

        for (int i = currentNucleusEnd + 1; i < nextNucleus; i++)
        {
            if (tokenTypes[i] is TokenType.Consonant
                or TokenType.ConsonantalGlide)
            {
                intervening.Add(i);
            }
        }

        if (intervening.Count == 0)
        {
            return currentNucleusEnd;
        }

        /*
         * A single consonant joins the following syllable:
         */
        if (intervening.Count == 1)
        {
            return intervening[0] - 1;
        }

        /*
         * Preserve qu as a complete onset:
         */
        if (StartsWithQu(analysis, tokenTypes, intervening))
        {
            return intervening[0] - 1;
        }

        /*
         * Preserve consonant digraphs:
         */
        if (StartsWithProtectedPair(
                analysis,
                intervening,
                ConsonantDigraphs))
        {
            return intervening[0] - 1;
        }

        /*
         * Preserve muta-cum-liquida combinations:
         */
        if (StartsWithProtectedPair(
                analysis,
                intervening,
                MutaCumLiquida))
        {
            return intervening[0] - 1;
        }

        if (intervening.Count >= 3)
        {
            int penultimate = intervening[^2];
            int last = intervening[^1];

            if (AreAdjacent(penultimate, last))
            {
                string finalPair = GetPair(
                    analysis,
                    penultimate,
                    last);

                if (ConsonantDigraphs.Contains(finalPair) ||
                    MutaCumLiquida.Contains(finalPair))
                {
                    return penultimate - 1;
                }
            }
        }

        return intervening[0];
    }

    private static bool StartsWithQu(
        AnalysisText analysis,
        TokenType[] tokenTypes,
        IReadOnlyList<int> intervening)
    {
        if (intervening.Count < 2)
        {
            return false;
        }

        int first = intervening[0];
        int second = intervening[1];

        if (!AreAdjacent(first, second))
        {
            return false;
        }

        return analysis.Letters[first].Normalized == 'q' &&
               analysis.Letters[second].Normalized == 'u' &&
               tokenTypes[second] == TokenType.ConsonantalGlide;
    }

    private static bool StartsWithProtectedPair(
        AnalysisText analysis,
        IReadOnlyList<int> intervening,
        HashSet<string> protectedPairs)
    {
        if (intervening.Count < 2)
        {
            return false;
        }

        int first = intervening[0];
        int second = intervening[1];

        if (!AreAdjacent(first, second))
        {
            return false;
        }

        string pair = GetPair(analysis, first, second);
        return protectedPairs.Contains(pair);
    }

    private static bool AreAdjacent(int first, int second)
    {
        return second == first + 1;
    }

    private static string GetPair(
        AnalysisText analysis,
        int first,
        int second)
    {
        return string.Concat(
            analysis.Letters[first].Normalized,
            analysis.Letters[second].Normalized);
    }

    private static string InsertHyphens(
        string original,
        IReadOnlySet<int> splitAfterIndices)
    {
        var result = new StringBuilder(
            original.Length + splitAfterIndices.Count);

        for (int i = 0; i < original.Length; i++)
        {
            result.Append(original[i]);

            if (splitAfterIndices.Contains(i) &&
                i < original.Length - 1 &&
                original[i + 1] != '-')
            {
                result.Append('-');
            }
        }

        return result.ToString();
    }

    private static AnalysisText CreateAnalysisText(string word)
    {
        var letters = new List<AnalysisLetter>();
        var normalizedWord = new StringBuilder();

        for (int originalIndex = 0;
             originalIndex < word.Length;
             originalIndex++)
        {
            string decomposed =
                word[originalIndex]
                    .ToString()
                    .Normalize(NormalizationForm.FormD);

            char? normalizedBase = null;

            foreach (char c in decomposed)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(c);

                if (category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark)
                {
                    continue;
                }

                normalizedBase = NormalizeLetter(c);
                break;
            }

            if (normalizedBase is null)
            {
                continue;
            }

            char normalized = normalizedBase.Value;

            letters.Add(
                new AnalysisLetter(
                    normalized,
                    originalIndex));

            if (IsLetter(normalized))
            {
                normalizedWord.Append(normalized);
            }
        }

        return new AnalysisText(
            letters,
            normalizedWord.ToString());
    }

    private static char NormalizeLetter(char value)
    {
        char lower = char.ToLowerInvariant(value);

        return lower switch
        {
            'æ' => 'a',
            'œ' => 'o',
            _ => lower
        };
    }

    private static bool IsVowel(char value)
    {
        return value is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
    }

    private static bool IsLetter(char value)
    {
        return char.IsLetter(value);
    }

    private sealed record AnalysisLetter(
        char Normalized,
        int OriginalIndex);

    private sealed record AnalysisText(
        List<AnalysisLetter> Letters,
        string NormalizedWord);
}