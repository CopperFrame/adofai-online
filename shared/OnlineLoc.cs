// Port (online): idioma das telas do online.
//
// O texto do código está em português. Em inglês, cada texto de tela é traduzido na hora de desenhar
// (Loc.T para os poucos textos montados no código, e LocalizeTexts para todos os componentes Text das telas do
// online). Textos de jogadores (nomes, chat, títulos de fases) não passam por aqui.
//
// No mod de PC o idioma segue o do jogo (ou a opção "Language" do arquivo de configuração do BepInEx).
// No port Android o padrão continua sendo o português.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public static partial class Loc
{
    public enum Mode
    {
        Auto,
        English,
        Portuguese,
        Korean,
    }

    public enum Lang
    {
        Portuguese,
        English,
        Korean,
    }

    public static Mode Preferred =
#if ADOFAI_MOD
        Mode.Auto;
#else
        Mode.Portuguese;
#endif

    /// <summary>Idioma das telas: o escolhido, ou (Auto) o do jogo: português, coreano ou, nos demais, inglês.</summary>
    public static Lang Language
    {
        get
        {
            switch (Preferred)
            {
                case Mode.English:
                    return Lang.English;
                case Mode.Portuguese:
                    return Lang.Portuguese;
                case Mode.Korean:
                    return Lang.Korean;
                default:
                    if (RDString.language == SystemLanguage.Portuguese)
                    {
                        return Lang.Portuguese;
                    }
                    return RDString.language == SystemLanguage.Korean ? Lang.Korean : Lang.English;
            }
        }
    }

    public static bool English => Language == Lang.English;

    // textos inteiros (palavras soltas): só trocam quando o texto é exatamente este
    private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>
    {
        ["Fase"] = "Level",
        ["Sala"] = "Room",
        ["Online"] = "Online",
        ["Pronto"] = "Ready",
        ["Não estou pronto"] = "Not ready",
        ["Enviar"] = "Send",
        ["Entrar"] = "Join",
        ["Assistir"] = "Watch",
        ["Cheia"] = "Full",
        ["Atualizar"] = "Refresh",
        ["Fechar"] = "Close",
        ["Remover"] = "Remove",
        ["Imortal"] = "Immortal",
        ["Velocidade"] = "Speed",
        ["Baixar"] = "Download",
        ["Instalando"] = "Installing",
        ["Escolher"] = "Pick",
        ["Recentes"] = "Recent",
        ["Populares"] = "Popular",
        ["Automática"] = "Automatic",
        ["Europa"] = "Europe",
        ["Ásia"] = "Asia",
        ["África"] = "Africa",
        ["Conectando…"] = "Connecting…",
        ["vídeo"] = "video",
        ["Jogador"] = "Player",
        ["Sala de "] = "Room by ",
    };

    // trechos: trocados em qualquer lugar do texto (do mais longo para o mais curto)
    private static readonly (string pt, string en)[] PhraseList =
    {
        // cliente e servidor
        ("Nenhum servidor configurado.", "No server configured."),
        ("O servidor usa outra versão do online. Atualize o jogo.", "The server uses a different online version. Update the game/mod."),
        ("Não foi possível conectar à sala: ", "Could not connect to the room: "),
        ("Conexão com a sala perdida.", "Connection to the room lost."),
        ("Versão do jogo diferente da sala.", "Game version differs from the room's."),
        ("Sala cheia.", "Room is full."),
        ("Nem todos estão prontos.", "Not everyone is ready."),
        ("Você foi removido da sala.", "You were removed from the room."),
        (" entrou para assistir", " joined to watch"),
        (" entrou na sala", " joined the room"),
        (" saiu da sala", " left the room"),

        // fases e partida
        ("A fase não tem link de download.", "The level has no download link."),
        ("Você não tem esta fase, e ela não foi encontrada no ADOFAI.gg/TUF para baixar.", "You don't have this level, and it wasn't found on ADOFAI.gg/TUF to download."),
        ("Preparando a fase para enviar à sala…", "Preparing the level to send to the room…"),
        ("Não deu para baixar a fase: ", "Could not download the level: "),
        ("O link da fase não tem mais o arquivo.", "The level link no longer has the file."),
        ("Não deu para instalar a fase: ", "Could not install the level: "),
        ("O arquivo baixado não tem uma fase (.adofai).", "The downloaded file has no level (.adofai)."),
        ("Carregando a partida para assistir…", "Loading the match to watch…"),
        ("Carregando a fase…", "Loading the level…"),
        ("Esperando os outros jogadores…", "Waiting for the other players…"),
        ("Começando…", "Starting…"),
        ("Partida encerrada. Voltando para a sala…", "Match ended. Going back to the room…"),
        ("Partida encerrada!", "Match ended!"),
        ("Alguém terminou! A partida acaba em ", "Someone finished! The match ends in "),
        ("Encerrar partida", "End match"),
        ("Ninguém mais está jogando.", "Nobody else is playing."),
        ("Assistindo  <b>", "Watching  <b>"),
        ("Começar partida", "Start match"),
        ("Resultado da partida", "Match results"),
        ("Não terminou", "Did not finish"),
        ("Desistiu", "Quit"),
        ("Terminou", "Finished"),
        ("  (caiu)", "  (failed)"),
        ("  (terminou)", "  (finished)"),
        (" da fase", " of the level"),

        // exigência do jogo original (OfficialAppCheck)
        ("É necessário ter o jogo original", "The original game is required"),
        ("Instale A Dance of Fire and Ice pela Google Play para usar este port.", "Install A Dance of Fire and Ice from Google Play to use this port."),
        ("O app instalado não é o original da 7th Beat Games.", "The installed app is not the original from 7th Beat Games."),
        ("O jogo original precisa ter sido instalado pela Google Play.", "The original game must have been installed from Google Play."),
        ("Não foi possível verificar o jogo original.", "Could not verify the original game."),
        ("Abrir na Google Play", "Open in Google Play"),
        ("Verificar de novo", "Check again"),

        // tela online: lista de salas
        ("O servidor online ainda não foi configurado neste jogo.", "The online server is not configured in this game."),
        ("Sem conexão com o servidor online.\n", "Could not reach the online server.\n"),
        ("Não deu para listar as salas.\n", "Could not list the rooms.\n"),
        ("Procurando salas…", "Looking for rooms…"),
        ("Aguardando jogadores", "Waiting for players"),
        ("Em partida", "In match"),
        ("entre para assistir", "join to watch"),
        ("Nenhuma sala aberta. Crie uma!", "No open rooms. Create one!"),
        ("Criando sala…", "Creating room…"),
        ("Não deu para criar a sala: ", "Could not create the room: "),
        ("O código da sala tem 5 letras/números.", "The room code has 5 letters/numbers."),
        ("Entrando na sala ", "Joining room "),
        ("Escolha um nome antes de entrar.", "Pick a name before joining."),
        ("sua região", "your region"),
        ("Seu nome", "Your name"),
        ("Como os outros vão te ver", "How others will see you"),
        ("Região das salas", "Room region"),
        ("Criar sala pública", "Create public room"),
        ("Criar sala privada", "Create private room"),
        ("Entrar com código", "Join with code"),
        ("Salas abertas", "Open rooms"),
        ("Todas as regiões", "All regions"),
        ("Automática", "Automatic"),
        ("América do Sul", "South America"),
        ("América do Norte", "North America"),
        ("Europa", "Europe"),
        ("Ásia", "Asia"),
        ("África", "Africa"),
        ("Fase ainda não escolhida", "Level not chosen yet"),
        ("<color=#ffffff88>Fase:</color> ", "<color=#ffffff88>Level:</color> "),

        // tela online: sala
        ("Código da sala", "Room code"),
        ("Escolher fase", "Pick level"),
        ("Sair da sala", "Leave room"),
        ("Jogadores (até ", "Players (up to "),
        (" · servidor ", " · server "),
        ("Procurando a fase…", "Looking for the level…"),
        ("Baixando ", "Downloading "),
        ("Fase pronta neste aparelho", "Level ready on this device"),
        ("Nenhuma fase escolhida", "No level chosen"),
        ("Escreva uma mensagem…", "Type a message…"),
        ("<color=#ffd166>Anfitrião</color>", "<color=#ffd166>Host</color>"),
        ("<color=#8be28b>Pronto</color>", "<color=#8be28b>Ready</color>"),
        ("<color=#ffffff88>Esperando</color>", "<color=#ffffff88>Waiting</color>"),
        ("(você)", "(you)"),
        ("(saiu)", "(left)"),
        ("Imortal: sim", "Immortal: yes"),
        ("Imortal: não", "Immortal: no"),
        ("Sem modificadores", "No modifiers"),
        ("Modificadores: ", "Modifiers: "),
        ("Imortal", "Immortal"),
        ("Velocidade ", "Speed "),

        // escolher / enviar fase
        ("Da comunidade (ADOFAI.gg / TUF)", "Community (ADOFAI.gg / TUF)"),
        ("Buscar nas minhas fases", "Search my levels"),
        ("Você ainda não tem fases. Escolha uma da comunidade.", "You don't have any levels yet. Pick one from the community."),
        ("Nenhuma fase encontrada para ", "No levels found for "),
        ("Nenhuma fase encontrada.", "No levels found."),
        ("Procurando a fase no ADOFAI.gg e no TUF…", "Looking for the level on ADOFAI.gg and TUF…"),
        ("Fase fora do ADOFAI.gg/TUF: enviando para a sala…", "Level not on ADOFAI.gg/TUF: sending it to the room…"),
        ("Fase enviada para a sala: quem não tiver vai baixar automaticamente.", "Level sent to the room: players who don't have it will download it automatically."),
        ("Fase encontrada: quem não tiver vai baixar automaticamente.", "Level found: players who don't have it will download it automatically."),
        ("Não deu para enviar a fase: só quem já tem poderá jogar.", "Could not send the level: only players who already have it can play."),

        // navegador de fases (ADOFAI.gg / TUF)
        ("Só na Oficina", "Workshop only"),
        ("Mais difíceis", "Hardest"),
        ("Mais fáceis", "Easiest"),
        ("Mais curtidas", "Most liked"),
        ("Mais concluídas", "Most cleared"),
        ("Baixar fases", "Download levels"),
        ("Carregando fases…", "Loading levels…"),
        ("Carregando mais…", "Loading more…"),
        ("Falha ao carregar mais fases.", "Failed to load more levels."),
        ("Tentar de novo", "Try again"),
        ("Buscar música, artista ou criador", "Search song, artist or creator"),
        ("Carregar mais", "Load more"),
        ("Fases da comunidade", "Community levels"),
        ("sem vídeo", "no video"),
        ("Fotossensível", "Photosensitive"),
    };

    private static readonly (Regex re, string en)[] RegexList =
    {
        (new Regex(@"Enviando a fase para a sala… (\d+)%"), "Uploading the level to the room… $1%"),
        (new Regex(@"Fase grande demais para enviar à sala \((\d+) MB\)\."), "Level too large to send to the room ($1 MB)."),
        (new Regex(@"Nenhuma sala em (.+?)\. Veja todas as regiões ou crie uma!"), "No rooms in $1. See all regions or create one!"),
        (new Regex(@"Sem conexão com o (.+?)\.\nVerifique a internet e tente de novo\."), "Could not reach $1.\nCheck your internet connection and try again."),
        (new Regex(@"O (\S+) respondeu algo inesperado\."), "$1 returned something unexpected."),
        (new Regex(@"Não deu para baixar (""[^""]*"")\. O link pode ter expirado\."), "Could not download $1. The link may have expired."),
        (new Regex(@"O link de (""[^""]*"") não tem mais o arquivo da fase\."), "The link for $1 no longer has the level file."),
        (new Regex(@"(\d+) de (\d+) fases"), "$1 of $2 levels"),
        (new Regex(@"(\d+)º"), "#$1"),
        (new Regex(@"\b1 queda\b"), "1 death"),
        (new Regex(@"(\d+) quedas"), "$1 deaths"),
        (new Regex(@"(^|>|·\s+)por "), "$1by "),
        (new Regex(@"^Recentes(?=\s|$)"), "Recent"),
        (new Regex(@"^Populares(?=\s|$)"), "Popular"),
    };

    private static (string pt, string en)[] phrases;
    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> CacheKo = new Dictionary<string, string>();

    /// <summary>Traduz um texto de tela para o idioma escolhido (português = devolve o próprio texto).</summary>
    public static string T(string pt)
    {
        Lang lang = Language;
        if (string.IsNullOrEmpty(pt) || lang == Lang.Portuguese)
        {
            return pt;
        }
        var cache = lang == Lang.Korean ? CacheKo : Cache;
        if (cache.TryGetValue(pt, out string cached))
        {
            return cached;
        }
        string result = Translate(pt, lang);
        if (cache.Count > 4000)
        {
            cache.Clear();
        }
        cache[pt] = result;
        return result;
    }

    private static string Translate(string pt, Lang lang)
    {
        bool ko = lang == Lang.Korean;
        if (Exact.TryGetValue(pt, out string whole))
        {
            return ko && KoPhrases.TryGetValue(pt, out string koWhole) ? koWhole : whole;
        }
        string s = pt;
        for (int i = 0; i < RegexList.Length; i++)
        {
            s = RegexList[i].re.Replace(s, ko && i < RegexKo.Length ? RegexKo[i] : RegexList[i].en);
        }
        if (phrases == null)
        {
            phrases = PhraseList.OrderByDescending(p => p.pt.Length).ToArray();
        }
        foreach (var (p, en) in phrases)
        {
            if (s.Contains(p))
            {
                s = s.Replace(p, ko && KoPhrases.TryGetValue(p, out string k) ? k : en);
            }
        }
        return s;
    }

    // ---- tradução automática dos componentes Text das telas do online

    private static readonly List<Transform> Roots = new List<Transform>();
    private static readonly List<Text> Scratch = new List<Text>();
    private static bool hooked;

    /// <summary>Registra a raiz de uma tela (canvas): todos os Text dela passam a ser traduzidos.</summary>
    public static void Register(Transform root)
    {
        Roots.Add(root);
        if (!hooked)
        {
            hooked = true;
            Canvas.willRenderCanvases += TranslateAll;
        }
    }

    private static void TranslateAll()
    {
        if (Language == Lang.Portuguese)
        {
            return;
        }
        for (int i = Roots.Count - 1; i >= 0; i--)
        {
            Transform root = Roots[i];
            if (root == null)
            {
                Roots.RemoveAt(i);
                continue;
            }
            root.GetComponentsInChildren(true, Scratch);
            foreach (Text t in Scratch)
            {
                if (t == null || string.IsNullOrEmpty(t.text) || t.gameObject.name == "ChatLine")
                {
                    continue;
                }
                var input = t.GetComponentInParent<InputField>();
                if (input != null && input.textComponent == t)
                {
                    continue; // o que o jogador digita não se traduz
                }
                string en = T(t.text);
                if (en != t.text)
                {
                    PortUi.SetText(t, en); // também escolhe a fonte (coreano precisa de uma fonte com hangul)
                }
            }
            Scratch.Clear();
        }
    }
}
