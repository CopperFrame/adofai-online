// Opções que o jogador muda na tela "Opções" do Online (idioma, servidores, atualizações).
//
// O valor fica guardado no arquivo de
// configuração do BepInEx (o Plugin troca o Store). A tela só fala com esta classe.
using System;
using System.Linq;
using UnityEngine;

public static class OnlineOptions
{
    public interface IStore
    {
        string Get(string key, string fallback);
        void Set(string key, string value);
    }

    // Chaves: "language" (auto/en/pt/ko), "primary", "fallback", "updates" (true/false), "filter" (true/false)
    private sealed class PrefsStore : IStore
    {
        public string Get(string key, string fallback)
        {
            if (key == "primary")
            {
                return OnlineClient.ServerOverride;
            }
            try
            {
                return PlayerPrefs.GetString("online.opt." + key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public void Set(string key, string value)
        {
            if (key == "primary")
            {
                OnlineClient.ServerOverride = value;
                return;
            }
            try
            {
                PlayerPrefs.SetString("online.opt." + key, value ?? "");
                PlayerPrefs.Save();
            }
            catch
            {
            }
        }
    }

    public static IStore Store = new PrefsStore();

    // o que a tela de opções mostra
    public static bool CanEditServers = true;
    public static bool HasFallbackServer;
    public static bool HasUpdateCheck;
    public static string DefaultLanguage = "pt";
    public static string DefaultPrimary = "";
    public static string DefaultFallback = "";

    private static readonly string[] BuiltIn = { "auto", "en", "pt", "ko" };

    /// <summary>Idiomas da tela Opções: os do mod e os arquivos da pasta de idiomas (só no mod de PC).</summary>
    public static string[] Languages => BuiltIn.Concat(Loc.PackCodes).ToArray();

    public static event Action Changed;

    public static string Language
    {
        get
        {
            string v = (Store.Get("language", DefaultLanguage) ?? DefaultLanguage).Trim().ToLowerInvariant();
            return Array.IndexOf(BuiltIn, v) >= 0 || Loc.HasPack(v) ? v : DefaultLanguage;
        }
        set
        {
            Store.Set("language", value);
            ApplyLanguage();
            Changed?.Invoke();
        }
    }

    public static void ApplyLanguage()
    {
        switch (Language)
        {
            case "en":
                Loc.Preferred = Loc.Mode.English;
                break;
            case "pt":
                Loc.Preferred = Loc.Mode.Portuguese;
                break;
            case "ko":
                Loc.Preferred = Loc.Mode.Korean;
                break;
            case "auto":
                Loc.Preferred = Loc.Mode.Auto;
                break;
            default:
                Loc.UseCustom(Language);
                Loc.Preferred = Loc.Mode.Custom;
                break;
        }
    }

    public static string PrimaryServer
    {
        get => (Store.Get("primary", DefaultPrimary) ?? "").Trim();
        set
        {
            Store.Set("primary", (value ?? "").Trim().TrimEnd('/'));
            Changed?.Invoke();
        }
    }

    public static string FallbackServer
    {
        get => (Store.Get("fallback", DefaultFallback) ?? "").Trim();
        set
        {
            Store.Set("fallback", (value ?? "").Trim().TrimEnd('/'));
            Changed?.Invoke();
        }
    }

    public static bool CheckForUpdates
    {
        get => (Store.Get("updates", "true") ?? "true") != "false";
        set
        {
            Store.Set("updates", value ? "true" : "false");
            Changed?.Invoke();
        }
    }

    /// <summary>Filtro de palavrões no chat e nos nomes (cada jogador escolhe; ligado por padrão).</summary>
    public static bool ChatFilter
    {
        get => (Store.Get("filter", "true") ?? "true") != "false";
        set
        {
            Store.Set("filter", value ? "true" : "false");
            Changed?.Invoke();
        }
    }

    public static void ResetServers()
    {
        PrimaryServer = DefaultPrimary;
        if (HasFallbackServer)
        {
            FallbackServer = DefaultFallback;
        }
    }

    /// <summary>Endereço aceito? Vazio vale (sem servidor); senão precisa ser http(s).</summary>
    public static bool IsValidServer(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0)
        {
            return true;
        }
        Uri uri;
        return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
