// Port (online): opções que o jogador muda na tela "Opções" do Online (idioma, servidores, atualizações).
//
// Onde o valor fica guardado depende de quem usa: no port Android, em PlayerPrefs; no mod de PC, no arquivo de
// configuração do BepInEx (o Plugin troca o Store). A tela só fala com esta classe.
using System;
using UnityEngine;

public static class OnlineOptions
{
    public interface IStore
    {
        string Get(string key, string fallback);
        void Set(string key, string value);
    }

    // Chaves: "language" (auto/en/pt/ko), "primary", "fallback", "updates" (true/false)
    private sealed class PrefsStore : IStore
    {
        public string Get(string key, string fallback)
        {
            if (key == "primary")
            {
                return OnlineClient.ServerOverride; // no port, "servidor principal" é o endereço manual
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

    // o que a tela de opções mostra: o mod liga tudo; o port só idioma e um servidor
    public static bool CanEditServers = true;
    public static bool HasFallbackServer;
    public static bool HasUpdateCheck;
    public static string DefaultLanguage = "pt";
    public static string DefaultPrimary = "";
    public static string DefaultFallback = "";

    public static readonly string[] Languages = { "auto", "en", "pt", "ko" };

    public static event Action Changed;

    public static string Language
    {
        get
        {
            string v = (Store.Get("language", DefaultLanguage) ?? DefaultLanguage).Trim().ToLowerInvariant();
            return Array.IndexOf(Languages, v) >= 0 ? v : DefaultLanguage;
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
            default:
                Loc.Preferred = Loc.Mode.Auto;
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
