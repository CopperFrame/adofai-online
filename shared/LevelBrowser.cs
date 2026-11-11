// Port: navegador de fases online dentro do CLS, com duas fontes da comunidade:
//
// ADOFAI.gg  — API pública https://api.adofai.gg/api/v1/levels?offset&amount&sort&query (máx. 50 por página).
//              Download: arquivo hospedado no ADOFAI.gg (/forum/v1/levels/{id}/file, redireciona para o R2) ou
//              link do Google Drive (drive.usercontent.google.com, confirm=t). Links antigos do Discord expiraram;
//              fases só da Oficina Steam não têm arquivo baixável fora do cliente Steam.
// TUF        — The Universal Forums, https://api.tuforums.com/v2/database/levels?offset&limit&sort&query
//              (sort = CAMPO_DESC; availableDlFilter=only lista só as que têm download). Download: dlLink, no CDN
//              do próprio TUF (api.tuforums.com/cdn/<id> → cdn.tuforums.com/zips/...).
//
// Depois de baixar, o .zip vai para o importador do jogo (ImportLevelsCLS), que descompacta, valida e move a fase
// para a pasta Worlds. A interface é montada por código (UGUI), numa Canvas por cima do CLS, e abre pelo botão
// "Baixar fases" do menu de pausa.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static PortUi;

public class LevelBrowser : MonoBehaviour
{
    // ------------------------------------------------------------------ modelo comum

    private class Entry
    {
        public string key;          // id único (fonte + id)
        public string title;
        public string artists;
        public string creators;
        public string difficulty;   // texto já formatado (pode ter rich text)
        public double? bpm;
        public int? tiles;
        public bool epilepsy;
        public string videoUrl;
        public string downloadUrl;  // null = sem download fora da Steam
        public string unavailableLabel = "Só na Oficina";
    }

    private class Page
    {
        public int total;
        public bool hasMore;
        public List<Entry> entries = new List<Entry>();
    }

    private abstract class Source
    {
        public abstract string Name { get; }
        // (rótulo, valor do parâmetro sort)
        public abstract (string, string)[] Sorts { get; }
        public abstract string BuildUrl(int offset, int sortIndex, string query);
        public abstract Page Parse(JToken json, int offset);
    }

    private class AdofaiGG : Source
    {
        private const int PageSize = 50;

        public override string Name => "ADOFAI.gg";

        public override (string, string)[] Sorts { get; } =
        {
            ("Recentes", "RECENT_DESC"),
            ("Populares", "LIKE_DESC"),
            ("Mais difíceis", "DIFFICULTY_DESC"),
            ("Mais fáceis", "DIFFICULTY_ASC"),
        };

        public override string BuildUrl(int offset, int sortIndex, string query)
        {
            string url = $"https://api.adofai.gg/api/v1/levels?offset={offset}&amount={PageSize}&sort={Sorts[sortIndex].Item2}";
            if (!string.IsNullOrWhiteSpace(query))
            {
                url += "&query=" + UnityWebRequest.EscapeURL(query.Trim());
            }
            return url;
        }

        public override Page Parse(JToken json, int offset)
        {
            var page = new Page { total = json.Value<int?>("count") ?? 0 };
            foreach (var t in json["results"] as JArray ?? new JArray())
            {
                if (!(t is JObject l))
                {
                    continue;
                }
                long id = l.Value<long>("id");
                string creators = l.Value<string>("creatorAlias");
                if (string.IsNullOrEmpty(creators))
                {
                    creators = Names(l["creators"], "displayName");
                }
                double? diff = l.Value<double?>("difficulty");
                page.entries.Add(new Entry
                {
                    key = "agg" + id,
                    title = l.Value<string>("title") ?? id.ToString(),
                    artists = Names(l["music"]?["artists"], "displayName"),
                    creators = creators,
                    difficulty = diff.HasValue ? $"<color=#ffb347>★ {diff.Value:0.#}</color>" : null,
                    bpm = l.Value<double?>("maxBpm"),
                    tiles = l.Value<int?>("tiles"),
                    epilepsy = l.Value<bool?>("epilepsyWarning") ?? false,
                    videoUrl = l.Value<string>("video") ?? l.Value<string>("videoUrl"),
                    downloadUrl = DownloadUrl(l, id),
                });
            }
            page.hasMore = offset + page.entries.Count < page.total && page.entries.Count > 0;
            return page;
        }

        private static string DownloadUrl(JObject l, long id)
        {
            if (l["file"] is JObject)
            {
                return $"https://api.adofai.gg/forum/v1/levels/{id}/file";
            }
            string url = l.Value<string>("downloadUrl") ?? l.Value<string>("download");
            return NormalizeLink(url);
        }
    }

    private class Tuf : Source
    {
        private const int PageSize = 50;

        public override string Name => "TUF";

        public override (string, string)[] Sorts { get; } =
        {
            ("Recentes", "RECENT_DESC"),
            ("Mais curtidas", "LIKES_DESC"),
            ("Mais concluídas", "CLEARS_DESC"),
            ("Mais difíceis", "DIFF_DESC"),
        };

        public override string BuildUrl(int offset, int sortIndex, string query)
        {
            string url = $"https://api.tuforums.com/v2/database/levels?offset={offset}&limit={PageSize}" +
                         $"&sort={Sorts[sortIndex].Item2}&availableDlFilter=only";
            if (!string.IsNullOrWhiteSpace(query))
            {
                url += "&query=" + UnityWebRequest.EscapeURL(query.Trim());
            }
            return url;
        }

        public override Page Parse(JToken json, int offset)
        {
            var page = new Page
            {
                total = json.Value<int?>("total") ?? 0,
                hasMore = json.Value<bool?>("hasMore") ?? false,
            };
            foreach (var t in json["results"] as JArray ?? new JArray())
            {
                if (!(t is JObject l) || (l.Value<bool?>("isDeleted") ?? false))
                {
                    continue;
                }
                long id = l.Value<long>("id");
                string artists = Names(l["artists"], "name");
                if (string.IsNullOrEmpty(artists))
                {
                    artists = l.Value<string>("artist");
                }
                string diffName = l["difficulty"]?.Value<string>("name");
                string diffColor = l["difficulty"]?.Value<string>("color");
                if (string.IsNullOrEmpty(diffColor) || !diffColor.StartsWith("#"))
                {
                    diffColor = "#ffffff";
                }
                bool epilepsy = false;
                if (l["tags"] is JArray tags)
                {
                    epilepsy = tags.Any(x => (x.Value<string>("name") ?? "").IndexOf("epilep", StringComparison.OrdinalIgnoreCase) >= 0
                                             || (x.Value<string>("name") ?? "").IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                string dl = l.Value<string>("dlLink");
                if (string.IsNullOrEmpty(dl))
                {
                    dl = l.Value<string>("legacyDllink");
                }
                page.entries.Add(new Entry
                {
                    key = "tuf" + id,
                    title = l.Value<string>("song") ?? id.ToString(),
                    artists = artists,
                    creators = l.Value<string>("creator"),
                    difficulty = string.IsNullOrEmpty(diffName) ? null : $"<color={diffColor}>{diffName}</color>",
                    bpm = l.Value<double?>("bpm"),
                    tiles = l.Value<int?>("tilecount"),
                    epilepsy = epilepsy,
                    videoUrl = l.Value<string>("videoLink"),
                    downloadUrl = NormalizeLink(dl),
                });
            }
            return page;
        }
    }

    private static string NormalizeLink(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
        {
            return null;
        }
        if (url.Contains("steamcommunity.com"))
        {
            return null;
        }
        if (url.Contains("drive.google.com"))
        {
            Match m = Regex.Match(url, "(?:/d/|id=)([a-zA-Z0-9_-]+)");
            return m.Success ? $"https://drive.usercontent.google.com/download?id={m.Groups[1].Value}&export=download&confirm=t" : null;
        }
        return url;
    }

    private static string Names(JToken arr, string field)
    {
        var names = new List<string>();
        if (arr is JArray a)
        {
            foreach (var x in a)
            {
                string n = x.Value<string>(field) ?? x.Value<string>("name");
                if (!string.IsNullOrEmpty(n))
                {
                    names.Add(n);
                }
            }
        }
        return string.Join(", ", names);
    }

    // ------------------------------------------------------------------ busca de link (online)

    /// <summary>
    /// Online: procura no ADOFAI.gg e no TUF uma fase com o mesmo título (e artista ou autor) e devolve o link
    /// de download e a chave dela, ou (null, null). Usado quando o anfitrião escolhe uma fase que já tinha.
    /// </summary>
    public static IEnumerator FindDownload(string title, string artist, string author, Action<string, string> done)
    {
        string wantTitle = Simplify(title);
        string wantArtist = Simplify(artist);
        string wantAuthor = Simplify(author);
        if (wantTitle.Length == 0)
        {
            done(null, null);
            yield break;
        }
        foreach (Source src in new Source[] { new AdofaiGG(), new Tuf() })
        {
            using (var req = UnityWebRequest.Get(src.BuildUrl(0, 0, StripTags(title))))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    continue;
                }
                Page page;
                try
                {
                    page = src.Parse(JToken.Parse(req.downloadHandler.text), 0);
                }
                catch
                {
                    continue;
                }
                foreach (Entry e in page.entries)
                {
                    // links antigos do Discord expiraram
                    if (e.downloadUrl == null || e.downloadUrl.Contains("discordapp") || Simplify(e.title) != wantTitle)
                    {
                        continue;
                    }
                    // a mesma música costuma ter várias fases de criadores diferentes: o autor é o que identifica
                    // a fase (o jogo usa autor+artista+título); só sem autor conhecido vale o artista
                    string creators = Simplify(e.creators);
                    bool match = wantAuthor.Length > 0
                        ? creators.Length > 0 && (creators.Contains(wantAuthor) || wantAuthor.Contains(creators))
                        : wantArtist.Length > 0 && Simplify(e.artists).Contains(wantArtist);
                    if (match)
                    {
                        done(e.downloadUrl, e.key);
                        yield break;
                    }
                }
            }
        }
        done(null, null);
    }

    // só letras e números, minúsculas: "Fledgling (feat. X)" e "fledgling(feat.x)" ficam iguais
    private static string Simplify(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in StripTags(s ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ estado

    private static readonly Color Progress = new Color(0.36f, 0.62f, 1f, 0.55f);

    private const string SourcePref = "levelBrowser.source";
    private const string DefaultSource = "ADOFAI.gg";

    public static LevelBrowser instance { get; private set; }

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    /// <summary>Texto do botão do menu de pausa que abre o navegador.</summary>
    public static string MenuLabel => RDString.language == SystemLanguage.Portuguese ? "Baixar fases" : "Download levels";

    private readonly Source[] sources = { new AdofaiGG(), new Tuf() };
    private int sourceIndex;
    private Source source => sources[sourceIndex];

    private Sprite downloadIcon;
    private Sprite backIcon;

    private GameObject panel;
    private Button sortButton;
    private GameObject sortMenu;
    private RectTransform sortMenuList;
    private GameObject statusPill;
    private RectTransform listContent;
    private ScrollRect scroll;
    private InputField searchField;
    private GameObject clearSearch;
    private Text statusText;
    private Text centerText;
    private Button loadMoreButton;
    private readonly List<Button> sourceButtons = new List<Button>();
    private readonly List<Button> sortButtons = new List<Button>();

    private int sortIndex;
    private string query = "";
    private int offset;
    private int requestSerial;
    private bool loading;
    private bool hasMore;
    private bool downloading;

    private readonly Dictionary<string, Texture2D> thumbCache = new Dictionary<string, Texture2D>();

    // ------------------------------------------------------------------ entrada

    /// <summary>Monta o navegador no CLS (chamado por scnCLS.Start). Abre pelo botão do menu de pausa.</summary>
    public static void AttachToCLS()
    {
        if (instance != null)
        {
            return;
        }
        var go = new GameObject("LevelBrowser");
        instance = go.AddComponent<LevelBrowser>();
        instance.Build();
    }

    public static void Open()
    {
        if (instance != null)
        {
            // espera um quadro: o menu de pausa ainda está fechando e devolvendo o controle ao jogador
            instance.StartCoroutine(instance.OpenNextFrame());
        }
    }

    public static void Close()
    {
        if (instance != null)
        {
            instance.SetOpen(false);
        }
    }

    /// <summary>
    /// Online: abre o navegador para o anfitrião escolher uma fase (o botão vira "Escolher"). Chama onPick com
    /// (link, chave única, título, artistas, criadores), ou com null se fechar sem escolher.
    /// </summary>
    public static void PickForOnline(Action<string, string, string, string, string> onPick)
    {
        if (instance == null)
        {
            onPick(null, null, null, null, null);
            return;
        }
        bool modeChanged = instance.pickCallback == null;
        instance.pickCallback = onPick;
        if (modeChanged && instance.listContent.childCount > 1)
        {
            instance.Reload(); // refaz os botões como "Escolher"
        }
        Open();
    }

    private Action<string, string, string, string, string> pickCallback;

    private IEnumerator OpenNextFrame()
    {
        yield return null;
        SetOpen(true);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
        foreach (var t in thumbCache.Values)
        {
            if (t != null)
            {
                Destroy(t);
            }
        }
    }

    private void SetOpen(bool open, bool installing = false)
    {
        if (open == IsOpen)
        {
            return;
        }
        panel.SetActive(open);
        if (ADOBase.playerManager != null)
        {
            ADOBase.playerManager.SetAllPlayerResponsive(!open);
        }
        if (!open && pickCallback != null)
        {
            // fechou sem escolher: avisa o online e volta ao modo normal (botões "Baixar")
            var cb = pickCallback;
            pickCallback = null;
            Reload();
            cb(null, null, null, null, null);
            return;
        }
        if (!open && !installing && scnCLS.instance != null && scnCLS.instance.levelCount == 0)
        {
            // CLS sem fases: volta para o painel "Nível não encontrado" em vez de deixar a tela vazia
            scnCLS.instance.importLevelPanel.OnOpenImportPanel();
        }
        if (open)
        {
            if (scnCLS.instance != null)
            {
                scnCLS.instance.optionsPanels.HideAnyPanel();
            }
            if (listContent.childCount <= 1) // só o botão "Carregar mais"
            {
                Reload();
            }
        }
    }

    private void Update()
    {
        // rolagem infinita: chegou perto do fim da lista, carrega a próxima página
        if (IsOpen && hasMore && !loading && scroll.verticalNormalizedPosition < 0.03f
            && listContent.rect.height > scroll.viewport.rect.height)
        {
            StartCoroutine(FetchPage(requestSerial));
        }
    }

    // ------------------------------------------------------------------ dados

    private void Reload()
    {
        offset = 0;
        hasMore = false;
        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            var child = listContent.GetChild(i).gameObject;
            if (child != loadMoreButton.gameObject)
            {
                Destroy(child);
            }
        }
        scroll.verticalNormalizedPosition = 1f;
        StartCoroutine(FetchPage(++requestSerial));
    }

    private IEnumerator FetchPage(int serial)
    {
        loading = true;
        loadMoreButton.gameObject.SetActive(false);
        if (offset == 0)
        {
            SetCenter("Carregando fases…");
        }
        SetStatus(offset == 0 ? "" : "Carregando mais…");
        Source src = source;
        using (var req = UnityWebRequest.Get(src.BuildUrl(offset, sortIndex, query)))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();
            if (serial != requestSerial)
            {
                yield break; // outra busca começou
            }
            loading = false;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Fail($"Sem conexão com o {src.Name}.\nVerifique a internet e tente de novo.", req.error);
                yield break;
            }
            Page page;
            try
            {
                page = src.Parse(JToken.Parse(req.downloadHandler.text), offset);
            }
            catch (Exception e)
            {
                Fail($"O {src.Name} respondeu algo inesperado.", e.Message);
                yield break;
            }
            foreach (var e in page.entries)
            {
                AddRow(e);
            }
            offset += page.entries.Count;
            hasMore = page.hasMore;
            if (offset == 0)
            {
                SetCenter(string.IsNullOrWhiteSpace(query)
                    ? "Nenhuma fase encontrada."
                    : $"Nenhuma fase encontrada para \"{query.Trim()}\".");
                SetStatus("");
            }
            else
            {
                SetCenter(null);
                SetStatus($"{offset} de {page.total} fases");
            }
            loadMoreButton.gameObject.SetActive(hasMore);
            loadMoreButton.transform.SetAsLastSibling();
        }
    }

    private void Fail(string message, string detail)
    {
        Debug.LogWarning($"[LevelBrowser] {message} ({detail})");
        if (offset == 0)
        {
            SetCenter(message);
            SetStatus("");
        }
        else
        {
            SetStatus("Falha ao carregar mais fases.");
        }
        // permite tentar de novo pelo botão
        hasMore = false;
        loadMoreButton.gameObject.SetActive(offset > 0);
        loadMoreButton.transform.SetAsLastSibling();
    }

    private static string YoutubeId(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }
        Match m = Regex.Match(url, @"(?:youtu\.be/|[?&]v=|/embed/|/shorts/|/live/)([a-zA-Z0-9_-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ------------------------------------------------------------------ download

    private IEnumerator Download(Entry entry, Button button, Text label, RectTransform bar)
    {
        downloading = true;
        button.interactable = false;
        string title = StripTags(entry.title);
        string dir = Path.Combine(scnCLS.tempLevelsFolder, "online");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, SafeFileName(title, entry.key) + ".zip");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        bar.gameObject.SetActive(true);
        using (var req = UnityWebRequest.Get(entry.downloadUrl))
        {
            req.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                float p = Mathf.Clamp01(req.downloadProgress);
                label.text = $"{p * 100f:0}%";
                bar.anchorMax = new Vector2(p, 1f);
                yield return null;
            }
            downloading = false;
            if (req.result != UnityWebRequest.Result.Success)
            {
                DownloadFailed(button, label, bar, $"Não deu para baixar \"{title}\". O link pode ter expirado.", req.error);
                yield break;
            }
        }
        if (!LooksLikeZip(path))
        {
            DownloadFailed(button, label, bar, $"O link de \"{title}\" não tem mais o arquivo da fase.", "não é zip");
            yield break;
        }
        label.text = "Instalando";
        bar.anchorMax = Vector2.one;
        SetOpen(false, installing: true);
        if (scnCLS.instance != null)
        {
            scnCLS.instance.importLevelPanel.ImportAndInstall(new[] { path });
        }
        label.text = "Baixar";
        bar.gameObject.SetActive(false);
        button.interactable = true;
    }

    private void DownloadFailed(Button button, Text label, RectTransform bar, string message, string detail)
    {
        Debug.LogWarning($"[LevelBrowser] {message} ({detail})");
        label.text = "Tentar de novo";
        bar.gameObject.SetActive(false);
        button.interactable = true;
        SetStatus(message);
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using (var f = File.OpenRead(path))
            {
                return f.Length > 4 && f.ReadByte() == 'P' && f.ReadByte() == 'K';
            }
        }
        catch
        {
            return false;
        }
    }

    private static string SafeFileName(string title, string key)
    {
        string s = Regex.Replace(title, "[\\\\/:*?\"<>|\\p{C}]", "_").Trim(' ', '.');
        if (s.Length > 60)
        {
            s = s.Substring(0, 60).Trim();
        }
        return string.IsNullOrEmpty(s) ? key : s;
    }

    private IEnumerator LoadThumb(string videoId, RawImage target)
    {
        if (thumbCache.TryGetValue(videoId, out var cached))
        {
            if (cached != null && target != null)
            {
                target.texture = cached;
                target.color = Color.white;
            }
            yield break;
        }
        thumbCache[videoId] = null;
        using (var req = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg"))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                yield break;
            }
            var tex = DownloadHandlerTexture.GetContent(req);
            thumbCache[videoId] = tex;
            if (target != null)
            {
                target.texture = tex;
                target.color = Color.white;
            }
        }
    }

    // ------------------------------------------------------------------ interface

    private void SetStatus(string s)
    {
        if (statusText != null)
        {
            SetText(statusText, s ?? "");
            statusPill.SetActive(!string.IsNullOrEmpty(s));
        }
    }

    private void SetCenter(string s)
    {
        if (centerText != null)
        {
            centerText.gameObject.SetActive(!string.IsNullOrEmpty(s));
            SetText(centerText, s ?? "");
        }
    }

    private void Build()
    {
        downloadIcon = Resources.Load<Sprite>("new/icon-download");
        backIcon = Resources.Load<Sprite>("new/icon-previous");

        var canvasGo = MakeCanvas(transform, "LevelBrowserCanvas", 400);

        // painel em tela cheia
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGo, false);
        Stretch((RectTransform)panel.transform);
        panel.GetComponent<Image>().color = Bg;

        // ---- barra única no topo: voltar | fontes | busca | ordenação (menu suspenso)
        const float barTop = -18f, barH = 92f;
        var back = MakeButton(panel.transform, "", 0, () => SetOpen(false), light: true);
        Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(30f, barTop), new Vector2(barH, barH));
        AddIcon(back.transform, backIcon, 60f);

        const float tabW = 180f;
        var tabs = NewRect("Sources", panel.transform);
        Place(tabs, new Vector2(0f, 1f), new Vector2(140f, barTop), new Vector2(sources.Length * (tabW + 8f) + 8f, barH));
        var tabsBg = tabs.gameObject.AddComponent<Image>();
        tabsBg.sprite = Rounded;
        tabsBg.type = Image.Type.Sliced;
        tabsBg.color = DarkTile;
        for (int i = 0; i < sources.Length; i++)
        {
            int idx = i;
            var b = MakeButton(tabs, sources[i].Name, 36, () => SelectSource(idx));
            Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(8f + i * (tabW + 8f), 0f), new Vector2(tabW, barH - 16f));
            sourceButtons.Add(b);
        }

        const float sortW = 330f;
        sortButton = MakeButton(panel.transform, "", 34, ToggleSortMenu);
        Place((RectTransform)sortButton.transform, new Vector2(1f, 1f), new Vector2(-30f, barTop), new Vector2(sortW, barH));

        float searchLeft = 140f + sources.Length * (tabW + 8f) + 8f + 20f;
        searchField = MakeInputField(panel.transform, "Buscar música, artista ou criador");
        var srt = (RectTransform)searchField.transform;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0f, 1f);
        srt.offsetMin = new Vector2(searchLeft, barTop - barH);
        srt.offsetMax = new Vector2(-30f - sortW - 20f, barTop);
        searchField.onValueChanged.AddListener(s => clearSearch.SetActive(!string.IsNullOrEmpty(s)));
        searchField.onEndEdit.AddListener(s =>
        {
            if (s != query)
            {
                query = s;
                Reload();
            }
        });
        var clear = MakeButton(searchField.transform, "X", 34, () =>
        {
            searchField.text = "";
            if (query != "")
            {
                query = "";
                Reload();
            }
        });
        Place((RectTransform)clear.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(76f, 76f), new Vector2(1f, 0.5f));
        clearSearch = clear.gameObject;
        clearSearch.SetActive(false);

        // ---- lista rolável
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(panel.transform, false);
        var scrt = (RectTransform)scrollGo.transform;
        scrt.anchorMin = Vector2.zero;
        scrt.anchorMax = Vector2.one;
        scrt.offsetMin = new Vector2(30f, 0f);
        scrt.offsetMax = new Vector2(-30f, -128f);
        scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 60f;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.08f;
        scroll.decelerationRate = 0.12f;

        var viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // área de toque para arrastar
        scroll.viewport = viewport;

        listContent = NewRect("Content", viewport);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.sizeDelta = Vector2.zero;
        var vlg = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 16f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.padding = new RectOffset(0, 0, 0, 90); // espaço para a mensagem de status no rodapé
        listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = listContent;

        loadMoreButton = MakeButton(listContent, "Carregar mais", 36, () =>
        {
            if (!loading)
            {
                hasMore = true;
                StartCoroutine(FetchPage(requestSerial));
            }
        });
        loadMoreButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;
        loadMoreButton.gameObject.SetActive(false);

        centerText = MakeText(scrollGo.transform, "", 46, Muted);
        centerText.alignment = TextAnchor.MiddleCenter;
        centerText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(centerText.rectTransform);
        centerText.gameObject.SetActive(false);

        // ---- mensagem de status: pílula no rodapé (contagem de fases, erros de download)
        var pill = NewRect("Status", panel.transform);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 0f);
        pill.anchoredPosition = new Vector2(0f, 16f);
        var pillImg = pill.gameObject.AddComponent<Image>();
        pillImg.sprite = Rounded;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color(0.1f, 0.09f, 0.14f, 0.95f);
        pillImg.raycastTarget = false;
        var hlg = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(34, 34, 8, 8);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        statusText = MakeText(pill, "", 26, new Color(1f, 1f, 1f, 0.85f));
        statusText.alignment = TextAnchor.MiddleCenter;
        statusPill = pill.gameObject;
        statusPill.SetActive(false);

        // ---- menu de ordenação (abre por cima de tudo; tocar fora fecha)
        sortMenu = new GameObject("SortMenu", typeof(RectTransform), typeof(Image), typeof(Button));
        sortMenu.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)sortMenu.transform);
        sortMenu.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        sortMenu.GetComponent<Button>().onClick.AddListener(() => sortMenu.SetActive(false));
        sortMenuList = NewRect("List", sortMenu.transform);
        Place(sortMenuList, new Vector2(1f, 1f), new Vector2(-30f, -120f), new Vector2(330f, 0f));
        var listBg = sortMenuList.gameObject.AddComponent<Image>();
        listBg.sprite = Rounded;
        listBg.type = Image.Type.Sliced;
        listBg.color = new Color(0.12f, 0.11f, 0.17f, 1f);
        var mvlg = sortMenuList.gameObject.AddComponent<VerticalLayoutGroup>();
        mvlg.padding = new RectOffset(8, 8, 8, 8);
        mvlg.spacing = 6f;
        mvlg.childControlWidth = true;
        mvlg.childControlHeight = true;
        mvlg.childForceExpandWidth = true;
        mvlg.childForceExpandHeight = false;
        sortMenuList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sortMenu.SetActive(false);

        // fonte escolhida por último (ADOFAI.gg na primeira vez)
        string saved = DefaultSource;
        try
        {
            saved = PlayerPrefs.GetString(SourcePref, DefaultSource);
        }
        catch
        {
        }
        sourceIndex = Mathf.Max(0, Array.FindIndex(sources, s => s.Name == saved));

        BuildSortButtons();
        UpdateSourceButtons();
        panel.SetActive(false);
    }

    private void SelectSource(int idx)
    {
        if (idx == sourceIndex)
        {
            return;
        }
        sourceIndex = idx;
        sortIndex = 0;
        try
        {
            PlayerPrefs.SetString(SourcePref, source.Name);
            PlayerPrefs.Save();
        }
        catch
        {
        }
        UpdateSourceButtons();
        BuildSortButtons();
        Reload();
    }

    private void UpdateSourceButtons()
    {
        for (int i = 0; i < sourceButtons.Count; i++)
        {
            SetSelected(sourceButtons[i], i == sourceIndex, Color.clear);
        }
    }

    // a ordenação muda com a fonte, então as opções do menu são recriadas
    private void BuildSortButtons()
    {
        foreach (var b in sortButtons)
        {
            Destroy(b.gameObject);
        }
        sortButtons.Clear();
        var sorts = source.Sorts;
        for (int i = 0; i < sorts.Length; i++)
        {
            int idx = i;
            var b = MakeButton(sortMenuList, sorts[i].Item1, 34, () =>
            {
                sortMenu.SetActive(false);
                if (sortIndex == idx)
                {
                    return;
                }
                sortIndex = idx;
                UpdateSortButtons();
                Reload();
            });
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
            sortButtons.Add(b);
        }
        UpdateSortButtons();
    }

    private void UpdateSortButtons()
    {
        for (int i = 0; i < sortButtons.Count; i++)
        {
            SetSelected(sortButtons[i], i == sortIndex, Color.clear);
        }
        var label = sortButton.GetComponentInChildren<Text>();
        if (label == null)
        {
            label = MakeText(sortButton.transform, "", 34, Color.white);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
        }
        SetText(label, source.Sorts[sortIndex].Item1 + "  v");
    }

    private void ToggleSortMenu()
    {
        sortMenu.SetActive(!sortMenu.activeSelf);
        sortMenu.transform.SetAsLastSibling();
    }


    private void AddRow(Entry e)
    {
        var row = NewRect("Level " + e.key, listContent);
        var rowImg = row.gameObject.AddComponent<Image>();
        rowImg.sprite = Rounded;
        rowImg.type = Image.Type.Sliced;
        rowImg.color = Card;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 172f;

        var thumb = NewRect("Thumb", row).gameObject.AddComponent<RawImage>();
        thumb.color = new Color(1f, 1f, 1f, 0.08f);
        Place(thumb.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(240f, 135f), new Vector2(0f, 0.5f));
        string vid = YoutubeId(e.videoUrl);
        if (vid != null)
        {
            StartCoroutine(LoadThumb(vid, thumb));
        }
        else
        {
            // sem miniatura (vídeo no Bilibili ou sem vídeo): mostra de onde é o vídeo
            string where = string.IsNullOrEmpty(e.videoUrl) ? "sem vídeo"
                : e.videoUrl.Contains("bilibili") ? "Bilibili" : "vídeo";
            var ph = MakeText(thumb.transform, where, 26, Muted);
            ph.alignment = TextAnchor.MiddleCenter;
            Stretch(ph.rectTransform);
        }

        var line1 = MakeText(row, $"{e.title}" + (string.IsNullOrEmpty(e.artists) ? "" : $"  <color=#ffffff88>{e.artists}</color>"), 40, Color.white);
        line1.horizontalOverflow = HorizontalWrapMode.Wrap;
        line1.verticalOverflow = VerticalWrapMode.Truncate;
        line1.alignment = TextAnchor.LowerLeft;
        var l1 = line1.rectTransform;
        l1.anchorMin = new Vector2(0f, 0.5f);
        l1.anchorMax = new Vector2(1f, 1f);
        l1.offsetMin = new Vector2(286f, 0f);
        l1.offsetMax = new Vector2(-320f, -14f);

        var info = new List<string>();
        if (!string.IsNullOrEmpty(e.difficulty)) info.Add(e.difficulty);
        if (!string.IsNullOrEmpty(e.creators)) info.Add("por " + e.creators);
        if (e.bpm.HasValue && e.bpm.Value > 0) info.Add($"{e.bpm.Value:0.#} BPM");
        if (e.tiles.HasValue && e.tiles.Value > 0) info.Add($"{e.tiles.Value} tiles");
        if (e.epilepsy) info.Add("<color=#ff6b52>Fotossensível</color>");
        var line2 = MakeText(row, string.Join("   ·   ", info), 28, Muted);
        line2.horizontalOverflow = HorizontalWrapMode.Wrap;
        line2.verticalOverflow = VerticalWrapMode.Truncate;
        line2.alignment = TextAnchor.UpperLeft;
        var l2 = line2.rectTransform;
        l2.anchorMin = new Vector2(0f, 0f);
        l2.anchorMax = new Vector2(1f, 0.5f);
        l2.offsetMin = new Vector2(286f, 14f);
        l2.offsetMax = new Vector2(-320f, -6f);

        bool available = e.downloadUrl != null;
        Button btn = null;
        Text label = null;
        RectTransform bar = null;
        bool picking = pickCallback != null;
        btn = MakeButton(row, available ? (picking ? "Escolher" : "Baixar") : e.unavailableLabel, 36, () =>
        {
            if (!available)
            {
                return;
            }
            if (pickCallback != null)
            {
                var cb = pickCallback;
                pickCallback = null;
                panel.SetActive(false);
                if (ADOBase.playerManager != null)
                {
                    ADOBase.playerManager.SetAllPlayerResponsive(true);
                }
                Reload();
                cb(e.downloadUrl, e.key, StripTags(e.title), e.artists, e.creators);
            }
            else if (!downloading)
            {
                StartCoroutine(Download(e, btn, label, bar));
            }
        }, light: available);
        Place((RectTransform)btn.transform, new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(270f, 110f), new Vector2(1f, 0.5f));
        label = btn.GetComponentInChildren<Text>();
        if (available)
        {
            // barra de progresso dentro do botão
            bar = NewRect("Progress", btn.transform);
            bar.SetAsFirstSibling();
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            var barImg = bar.gameObject.AddComponent<Image>();
            barImg.sprite = Rounded;
            barImg.type = Image.Type.Sliced;
            barImg.color = Progress;
            barImg.raycastTarget = false;
            bar.gameObject.SetActive(false);
            if (downloadIcon != null && !picking)
            {
                var icon = AddIcon(btn.transform, downloadIcon, 52f);
                Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(52f, 52f), new Vector2(0f, 0.5f));
                label.rectTransform.offsetMin = new Vector2(70f, 0f);
            }
        }
        else
        {
            btn.interactable = false;
            label.color = Muted;
        }

        loadMoreButton.transform.SetAsLastSibling();
    }
}
