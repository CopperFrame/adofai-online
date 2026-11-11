// Filtro de palavrões do chat e dos nomes. Roda só na tela de quem lê (cada jogador escolhe em Opções):
// as palavras da lista viram "****". Compara só palavras inteiras (sem acento, sem maiúsculas, com letras repetidas e
// trocas comuns como 0=o, 1=i, 3=e, 4=a, 5=s, @=a, $=s), para não mascarar palavras normais que contêm outra.
// No mod de PC dá para acrescentar palavras em BepInEx/config/AdofaiOnline-lang/filter.txt (uma por linha).
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class ChatFilter
{
    private const string Builtin =
        // português
        "porra caralho merda puta foder foda fodase fdp vsf buceta viado arrombado cuzao vagabunda retardado mongoloide bosta cacete " +
        // inglês
        "fuck fucking fucker motherfucker shit bitch asshole cunt nigger nigga faggot fag retard retarded whore slut tranny pussy dickhead " +
        // coreano
        "씨발 시발 병신 개새끼 좆 지랄 븅신";

    private static readonly HashSet<string> Words = new HashSet<string>();
    private static readonly Regex Token = new Regex(@"[\p{L}\p{N}@$]+", RegexOptions.Compiled);

    static ChatFilter()
    {
        foreach (string w in Builtin.Split(' '))
        {
            AddWord(w);
        }
    }

    public static void AddWord(string word)
    {
        string n = Normalize(word ?? "");
        if (n.Length > 1)
        {
            Words.Add(n);
        }
    }

    /// <summary>O texto com as palavras da lista trocadas por asteriscos (ou o próprio texto, se o filtro estiver desligado).</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text) || !OnlineOptions.ChatFilter)
        {
            return text;
        }
        return Token.Replace(text, m => IsBad(m.Value) ? new string('*', m.Value.Length) : m.Value);
    }

    private static bool IsBad(string token)
    {
        string n = Normalize(token);
        if (n.Length < 2)
        {
            return false;
        }
        if (Words.Contains(n))
        {
            return true;
        }
        // plural simples: "bitchs", "fucks"
        return n.Length > 3 && n[n.Length - 1] == 's' && Words.Contains(n.Substring(0, n.Length - 1));
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        char last = '\0';
        foreach (char raw in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                continue; // acentos
            }
            char c;
            switch (raw)
            {
                case '0': c = 'o'; break;
                case '1': c = 'i'; break;
                case '3': c = 'e'; break;
                case '4': c = 'a'; break;
                case '5': c = 's'; break;
                case '@': c = 'a'; break;
                case '$': c = 's'; break;
                default: c = raw; break;
            }
            if (c != last)
            {
                sb.Append(c); // letras repetidas contam como uma ("fuuuck")
                last = c;
            }
        }
        return sb.ToString();
    }
}
