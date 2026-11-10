// Idiomas em arquivo de texto (mod de PC): qualquer pessoa pode acrescentar um idioma sem mexer no código.
//
// Pasta: BepInEx/config/AdofaiOnline-lang/. Cada arquivo <codigo>.txt é um idioma, por exemplo es.txt:
//
//     # name: Español
//     Pronto = Listo
//     Criar sala = Crear sala
//
// Do lado esquerdo vai o texto original (português, igual ao arquivo template.txt, que o mod gera a cada
// abertura com todos os textos e a versão em inglês). "\n" vale como quebra de linha. O que o arquivo não
// traduzir aparece em inglês. Para usar: opção Language = <codigo> (ou a tela Online > Opções).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static partial class Loc
{
    private sealed class Pack
    {
        public string code;
        public string name;
        public Dictionary<string, string> map = new Dictionary<string, string>();
        public KeyValuePair<string, string>[] byLength;
    }

    private static readonly Dictionary<string, Pack> Packs = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> CacheCustom = new Dictionary<string, string>();
    private static Pack customPack;

    /// <summary>Código do idioma de arquivo em uso (só vale com Mode.Custom).</summary>
    public static string CustomCode { get; private set; }

    /// <summary>Códigos dos idiomas encontrados na pasta de idiomas.</summary>
    public static IEnumerable<string> PackCodes => Packs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    public static bool HasPack(string code) => !string.IsNullOrEmpty(code) && Packs.ContainsKey(code);

    public static string PackName(string code) => Packs.TryGetValue(code ?? "", out Pack p) ? p.name : code;

    /// <summary>Escolhe o idioma de arquivo (ou nenhum, com null).</summary>
    public static void UseCustom(string code)
    {
        CustomCode = code;
        customPack = code != null && Packs.TryGetValue(code, out Pack p) ? p : null;
        CacheCustom.Clear();
    }

    /// <summary>Lê os idiomas da pasta e grava o template.txt. Nunca lança: um arquivo ruim só é ignorado.</summary>
    public static void LoadPacks(string dir, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(dir);
            WriteTemplate(Path.Combine(dir, "template.txt"));
            foreach (string file in Directory.GetFiles(dir, "*.txt"))
            {
                string code = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();
                if (code.Length == 0 || code == "template" || code == "auto" || code == "en" || code == "pt" || code == "ko")
                {
                    continue; // en, pt e ko já são do mod
                }
                try
                {
                    Pack pack = Parse(code, File.ReadAllLines(file, Encoding.UTF8));
                    Packs[code] = pack;
                    log?.Invoke("Idioma carregado: " + code + " (" + pack.name + "), " + pack.map.Count + " textos");
                }
                catch (Exception e)
                {
                    log?.Invoke("Idioma " + code + " ignorado: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            log?.Invoke("Pasta de idiomas indisponível: " + e.Message);
        }
    }

    private static Pack Parse(string code, string[] lines)
    {
        var pack = new Pack { code = code, name = code };
        foreach (string raw in lines)
        {
            string line = raw.Trim('﻿');
            if (line.Trim().Length == 0)
            {
                continue;
            }
            if (line.TrimStart().StartsWith("#"))
            {
                string c = line.TrimStart().TrimStart('#').Trim();
                if (c.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    string n = c.Substring(5).Trim();
                    if (n.Length > 0)
                    {
                        pack.name = n;
                    }
                }
                continue;
            }
            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }
            string source = line.Substring(0, eq).Replace("\\n", "\n");
            string target = line.Substring(eq + 3).Replace("\\n", "\n");
            if (target.Length > 0)
            {
                pack.map[source] = target;
            }
        }
        pack.byLength = pack.map.OrderByDescending(kv => kv.Key.Length).ToArray();
        return pack;
    }

    private static void WriteTemplate(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Template: copy this file to <language code>.txt (for example es.txt), translate the text after \" = \"");
        sb.AppendLine("# and add a line \"# name: Your language name\". Left side = original text (Portuguese); right side = English.");
        sb.AppendLine("# Lines you do not translate (or delete) are shown in English. Use \\n for a line break.");
        sb.AppendLine("# Generated again every time the game starts: do not edit this file.");
        sb.AppendLine();
        var seen = new HashSet<string>();
        foreach (var kv in Exact)
        {
            if (seen.Add(kv.Key))
            {
                sb.AppendLine(kv.Key.Replace("\n", "\\n") + " = " + kv.Value.Replace("\n", "\\n"));
            }
        }
        foreach (var (pt, en) in PhraseList)
        {
            if (seen.Add(pt))
            {
                sb.AppendLine(pt.Replace("\n", "\\n") + " = " + en.Replace("\n", "\\n"));
            }
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string TranslateCustom(string pt)
    {
        if (customPack == null)
        {
            return Translate(pt, Lang.English);
        }
        if (customPack.map.TryGetValue(pt, out string whole))
        {
            return whole;
        }
        string s = pt;
        foreach (var kv in customPack.byLength)
        {
            if (s.Contains(kv.Key))
            {
                s = s.Replace(kv.Key, kv.Value);
            }
        }
        return Translate(s, Lang.English); // o que o arquivo não cobre cai para o inglês
    }
}
