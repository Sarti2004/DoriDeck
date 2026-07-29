using System.Text;

namespace DoriDeck.Syllabifiers;

public class LatinSyllabifier : ILyricSyllabifier
{
    // Все гласные латинского языка
    private const string Vowels = "aeiouyAEIOUY";

    // Латинские дифтонги, которые образуют ОДИН слог (их нельзя разрывать дефисом)
    private static readonly string[] LatinDiphthongs = { "ae", "oe", "au", "eu" };

    // Неделимые сочетания согласных (Muta cum Liquida). 
    // Если после p, b, t, d, c, g, f идет l или r — они всегда уходят на следующий слог вместе!
    // Например: "pa-tris", "la-cri-mo-sa", "re-demp-tor"
    private static readonly string[] MutaCumLiquida = {
        "pl", "pr", "bl", "br", "tl", "tr", "dl", "dr", "cl", "cr", "gl", "gr", "fl", "fr"
    };

    // Другие устойчивые буквосочетания, которые ведут себя как одна согласная
    private static readonly string[] LatinDigraphs = { "ch", "ph", "th", "qu" };

    public string Syllabify(string word)
    {
        if (word.Length <= 1) return word;

        string lowerWord = word.ToLower();
        int length = word.Length;

        // Шаг 1. Размечаем гласные звуки, учитывая дифтонги
        bool[] isVowelSound = new bool[length];
        int totalVowels = 0;

        for (int i = 0; i < length; i++)
        {
            if (Vowels.Contains(lowerWord[i]))
            {
                // Проверяем, не является ли эта гласная частью дифтонга (ae, oe, au, eu)
                if (i > 0 && isVowelSound[i - 1] && LatinDiphthongs.Contains(lowerWord.Substring(i - 1, 2)))
                {
                    // Предыдущая гласная уже посчитана, эту как отдельный слог не считаем
                    isVowelSound[i] = false;
                }
                else
                {
                    isVowelSound[i] = true;
                    totalVowels++;
                }
            }
        }

        // Если 0 или 1 слог (например: "te", "me", "et", "pax") — дефисы не нужны
        if (totalVowels <= 1) return word;

        // Шаг 2. Расставляем дефисы
        StringBuilder sb = new StringBuilder();
        int vowelsFound = 0;

        for (int i = 0; i < length; i++)
        {
            sb.Append(word[i]);

            if (isVowelSound[i])
            {
                vowelsFound++;
            }

            // Проверяем условия для вставки дефиса '-'
            if (vowelsFound > 0 && vowelsFound < totalVowels && i < length - 1)
            {
                if (ShouldPutHyphenAfter(word, lowerWord, i, isVowelSound))
                {
                    sb.Append("-");
                }
            }
        }

        return sb.ToString();
    }

    private bool ShouldPutHyphenAfter(string word, string lowerWord, int index, bool[] isVowelSound)
    {
        int length = word.Length;

        // 1. Стык двух самостоятельных гласных (дифтонги мы исключили на Шаге 1)
        // Например: "tu-a", "de-us", "me-us", "di-e-i"
        if (isVowelSound[index] && isVowelSound[index + 1])
        {
            return true;
        }

        // 2. Текущий символ — гласный (или часть дифтонга), а следующий — согласный
        if (isVowelSound[index] && !isVowelSound[index + 1])
        {
            // Смотрим, сколько согласных идет дальше до следующей гласной
            int nextVowelIndex = -1;
            for (int j = index + 1; j < length; j++)
            {
                if (isVowelSound[j]) { nextVowelIndex = j; break; }
            }

            if (nextVowelIndex != -1)
            {
                int consonantsCount = nextVowelIndex - (index + 1);

                // Если согласная всего одна (например: "a-me-nus", "do-mi-nus"), дефис ставится СРАЗУ
                if (consonantsCount == 1) return true;

                // Если согласных две или больше (стык), проверяем неделимые группы
                if (consonantsCount >= 2)
                {
                    string pair = lowerWord.Substring(index + 1, 2);

                    // Если стык начинается с неделимой группы (ch, ph, th, qu, pl, br...),
                    // то ВСЯ эта группа уходит на следующий слог. Дефис ставится СРАЗУ.
                    if (LatinDigraphs.Contains(pair) || MutaCumLiquida.Contains(pair))
                    {
                        return true;
                    }
                }
            }
        }

        // 3. Разрыв обычного стыка согласных (например: "san-ctus", "sem-per", "prop-ter")
        if (!isVowelSound[index] && !isVowelSound[index + 1])
        {
            // Проверяем, не стоим ли мы внутри неделимого диграфа (ch, th...)
            if (index > 0)
            {
                string prevPair = lowerWord.Substring(index - 1, 2);
                if (LatinDigraphs.Contains(prevPair)) return false; 
            }

            // Смотрим вперед: если впереди идет группа Muta cum Liquida (например, "tr" в "pa-tris"),
            // то дефис перед ней уже поставился. Но если это обычный стык типа "nt" или "mp",
            // дефис ставится ровно между ними, при условии, что дальше есть гласные.
            if (index + 2 < length)
            {
                string nextPair = lowerWord.Substring(index + 1, 2);
                // Если впереди неделимая группа согласных, мы её не рубим (например, "re-demp-tor" -> "mp" разрываем, "tor" уходит)
                if (MutaCumLiquida.Contains(nextPair) || LatinDigraphs.Contains(nextPair))
                {
                    return false; 
                }
            }

            // Убеждаемся, что в конце слова еще остались гласные, чтобы не поставить дефис перед финальной согласной
            if (word.Skip(index + 1).Any(c => Vowels.Contains(char.ToLower(c))))
            {
                return true;
            }
        }

        return false;
    }
}