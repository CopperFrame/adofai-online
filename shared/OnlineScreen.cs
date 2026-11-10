// Port: tela "Online" do CLS (abre pelo botão Online do menu de pausa). Duas vistas:
//   - Salas: nome do jogador, região, lista de salas públicas (da região escolhida ou de todas), criar sala
//     pública/privada e entrar com código.
//   - Sala: código para compartilhar, jogadores (anfitrião, pronto), fase escolhida, pronto/começar, sair.
// A rede fica no OnlineClient; aqui é só interface (montada por código com o PortUi).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static PortUi;

public class OnlineScreen : MonoBehaviour
{
    // continentes do Cloudflare usados como regiões das salas ("" = automático)
    private static readonly (string code, string label)[] Regions =
    {
        ("", "Automática"),
        ("SA", "América do Sul"),
        ("NA", "América do Norte"),
        ("EU", "Europa"),
        ("AS", "Ásia"),
        ("OC", "Oceania"),
        ("AF", "África"),
    };

    public static OnlineScreen instance { get; private set; }

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    public static string MenuLabel => "Online";

    /// <summary>Abrir a tela assim que o CLS carregar (veio pelo portal Online do menu principal).</summary>
    public static bool openOnArrival;

    private OnlineClient client => OnlineClient.Instance;

    private GameObject panel;
    private Text titleText;
    private Text pingText;
    private Text toastText;
    private GameObject toast;
    private float toastUntil;

    // vista "Salas"
    private GameObject lobbyView;
    private InputField nameField;
    private Button regionButton;
    private Button allRegionsButton;
    private RectTransform roomList;
    private Text roomListStatus;
    private InputField codeField;
    private bool showAllRegions;
    private bool busy;
    private GameObject regionMenu;
    private RectTransform regionMenuList;

    // vista "Sala"
    private GameObject roomView;
    private Text roomCodeText;
    private Text roomInfoText;
    private RectTransform playerList;
    private Text levelText;
    private Button chooseLevelButton;
    private Button optionsButton;
    private GameObject optionsView;
    private Button languageButton;
    private Button updatesButton;
    private Button resetServersButton;
    private InputField primaryField;
    private InputField fallbackField;
    private Selectable focus; // item marcado pela navegação por teclado
    private Button immortalButton;
    private Button speedButton;
    private Text modsText;
    private static readonly float[] SpeedSteps = { 1f, 1.25f, 1.5f, 1.75f, 2f, 0.5f, 0.75f };
    private Button readyButton;
    private Button startButton;

    // ------------------------------------------------------------------ entrada

    public static void AttachToCLS()
    {
        if (instance != null)
        {
            return;
        }
        var go = new GameObject("OnlineScreen");
        instance = go.AddComponent<OnlineScreen>();
        OnlineOptions.ApplyLanguage(); // idioma escolhido nas opções (no port: PlayerPrefs; padrão português)
        instance.Build();
        if (openOnArrival)
        {
            openOnArrival = false;
            Open();
        }
        else if (OnlineClient.Exists && OnlineClient.Instance.state == OnlineClient.State.InRoom)
        {
            // voltou de uma partida: reabre a sala
            OnlineMatch.Instance.Recheck();
            Open();
        }
    }

    public static void Open()
    {
        if (instance != null)
        {
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

    private RawImage blurBackground;
    private TakeScreenshot screenshot;

    private static bool TransitionVisible()
    {
        var ui = scrUIController.instance;
        return ui != null && (ui.isWipingToBlack || (ui.transitionPanel != null && ui.transitionPanel.gameObject.activeInHierarchy));
    }

    private IEnumerator OpenNextFrame()
    {
        yield return null; // o menu de pausa ainda está fechando
        // logo depois de carregar o CLS (portal do menu principal) a transição de tela ainda está preta:
        // espera ela sumir, senão a foto do fundo sai preta (no máximo 3 s)
        float waitUntil = Time.unscaledTime + 3f;
        while (Time.unscaledTime < waitUntil && TransitionVisible())
        {
            yield return null;
        }
        // o mesmo fundo do menu de pausa: foto da tela desfocada (tirada antes da tela aparecer) e escurecida
        try
        {
            screenshot ??= new TakeScreenshot();
        }
        catch
        {
            screenshot = null;
        }
        if (screenshot != null && blurBackground != null)
        {
            yield return screenshot.GetBlurredScreenshot(blurBackground);
            if (blurBackground.texture != null)
            {
                blurBackground.color = new Color(0.4528302f, 0.4528302f, 0.4528302f, 1f);
            }
        }
        SetOpen(true);
    }

    private void OnEnable()
    {
        var c = client;
        c.OnRoomChanged += RefreshRoom;
        c.OnError += ShowToast;
        c.OnLeft += OnLeftRoom;
        c.OnChatLine += AppendChat;
        OnlineMatch.Instance.OnLevelStateChanged += RefreshRoom;
    }

    private void OnDisable()
    {
        if (!OnlineClient.Exists)
        {
            return;
        }
        var c = client;
        c.OnRoomChanged -= RefreshRoom;
        c.OnError -= ShowToast;
        c.OnLeft -= OnLeftRoom;
        c.OnChatLine -= AppendChat;
        OnlineMatch.Instance.OnLevelStateChanged -= RefreshRoom;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void SetOpen(bool open)
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
        if (open)
        {
            if (scnCLS.instance != null)
            {
                scnCLS.instance.optionsPanels.HideAnyPanel();
            }
            ShowView(client.state == OnlineClient.State.InRoom);
            if (client.state != OnlineClient.State.InRoom)
            {
                StartCoroutine(Connect());
            }
        }
    }

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }
        if (toast.activeSelf && Time.unscaledTime > toastUntil)
        {
            toast.SetActive(false);
        }
        AutoRefreshRooms();
        HandleKeyboard();
        float ping = client.pingMs;
        string p = client.state == OnlineClient.State.InRoom && ping >= 0f ? $"{ping:0} ms" : "";
        if (pingText.text != p)
        {
            SetText(pingText, p);
            pingText.color = ping < 80f ? new Color(0.55f, 0.9f, 0.6f) : ping < 160f ? new Color(1f, 0.85f, 0.4f) : Danger;
        }
    }

    // ------------------------------------------------------------------ fluxo

    private IEnumerator Connect()
    {
        if (!OnlineClient.ServerConfigured)
        {
            SetListStatus("O servidor online ainda não foi configurado neste jogo.");
            yield break;
        }
        SetListStatus("Conectando…");
        string error = null;
        yield return client.FetchInfo(e => error = e);
        if (error != null)
        {
            SetListStatus("Sem conexão com o servidor online.\n" + error);
            yield break;
        }
        UpdateRegionLabels();
        yield return RefreshRooms();
    }

    private const float AutoRefreshSeconds = 5f;
    private float nextAutoRefresh;

    // a lista de salas se atualiza sozinha enquanto a tela de salas está aberta
    private void AutoRefreshRooms()
    {
        if (busy || !lobbyView.activeSelf || regionMenu.activeSelf || !OnlineClient.ServerConfigured ||
            client.state != OnlineClient.State.Offline || Time.unscaledTime < nextAutoRefresh)
        {
            return;
        }
        StartCoroutine(RefreshRooms(silent: true));
    }

    private IEnumerator RefreshRooms(bool silent = false)
    {
        if (busy)
        {
            yield break;
        }
        busy = true;
        nextAutoRefresh = Time.unscaledTime + AutoRefreshSeconds;
        if (!silent)
        {
            SetListStatus("Procurando salas…");
        }
        List<OnlineClient.RoomInfo> rooms = null;
        string error = null;
        yield return client.ListRooms((r, e) =>
        {
            rooms = r;
            error = e;
        });
        busy = false;
        nextAutoRefresh = Time.unscaledTime + AutoRefreshSeconds;
        if (error != null)
        {
            // atualização automática com erro: mantém a lista que já está na tela
            if (!silent || roomList.childCount == 0)
            {
                ClearChildren(roomList);
                SetListStatus("Não deu para listar as salas.\n" + error);
            }
            yield break;
        }
        ClearChildren(roomList);
        string region = EffectiveRegion();
        var shown = rooms.Where(r => showAllRegions || string.IsNullOrEmpty(region) || r.region == region)
            .OrderBy(r => r.inGame)
            .ThenByDescending(r => r.players)
            .ToList();
        var waiting = shown.Where(r => !r.inGame).ToList();
        var playing = shown.Where(r => r.inGame).ToList();
        if (waiting.Count > 0)
        {
            AddSection($"Aguardando jogadores  <color=#ffffff88>({waiting.Count})</color>");
            foreach (var r in waiting)
            {
                AddRoomRow(r);
            }
        }
        if (playing.Count > 0)
        {
            AddSection($"Em partida  <color=#ffffff88>({playing.Count})</color>  <size=24><color=#ffffff66>entre para assistir</color></size>");
            foreach (var r in playing)
            {
                AddRoomRow(r);
            }
        }
        if (shown.Count == 0)
        {
            SetListStatus(rooms.Count == 0
                ? "Nenhuma sala aberta. Crie uma!"
                : $"Nenhuma sala em {RegionLabel(region)}. Veja todas as regiões ou crie uma!");
        }
        else
        {
            SetListStatus("");
        }
    }

    private IEnumerator CreateRoom(bool isPublic)
    {
        if (busy || !SaveName())
        {
            yield break;
        }
        busy = true;
        ShowToast("Criando sala…");
        string code = null, error = null;
        yield return client.CreateRoom(Loc.T("Sala de ") + OnlineClient.PlayerName, isPublic, (c, e) =>
        {
            code = c;
            error = e;
        });
        busy = false;
        if (error != null || string.IsNullOrEmpty(code))
        {
            ShowToast("Não deu para criar a sala: " + error);
            yield break;
        }
        JoinRoom(code);
    }

    private void JoinRoom(string code)
    {
        if (!SaveName())
        {
            return;
        }
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length != 5)
        {
            ShowToast("O código da sala tem 5 letras/números.");
            return;
        }
        ShowToast("Entrando na sala " + code + "…");
        client.Join(code);
    }

    private bool SaveName()
    {
        string name = (nameField.text ?? "").Trim();
        if (name.Length == 0)
        {
            ShowToast("Escolha um nome antes de entrar.");
            return false;
        }
        OnlineClient.PlayerName = name;
        return true;
    }

    private void OnLeftRoom()
    {
        if (IsOpen)
        {
            ShowView(false);
            StartCoroutine(RefreshRooms());
        }
    }

    private string EffectiveRegion()
    {
        string r = OnlineClient.Region;
        return string.IsNullOrEmpty(r) ? (client.detectedRegion ?? "") : r;
    }

    private static string RegionLabel(string code)
    {
        foreach (var r in Regions)
        {
            if (r.code == code && code != "")
            {
                return r.label;
            }
        }
        return string.IsNullOrEmpty(code) ? "sua região" : code;
    }

    // ------------------------------------------------------------------ interface: estrutura

    private void Build()
    {
        Transform canvas = MakeCanvas(transform, "OnlineCanvas", 410);
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas, false);
        Stretch((RectTransform)panel.transform);
        // fundo como o do menu de pausa: foto desfocada da tela atrás, escurecida
        panel.GetComponent<Image>().color = Color.clear;
        blurBackground = NewRect("Blur", panel.transform).gameObject.AddComponent<RawImage>();
        Stretch(blurBackground.rectTransform);
        blurBackground.color = new Color(0f, 0f, 0f, 0.55f);

        // barra do topo: voltar | título | ping
        var back = MakeBackButton(panel.transform, OnBack);
        Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(100f, 100f));

        titleText = MakeText(panel.transform, "Online", 52, Color.white);
        titleText.alignment = TextAnchor.MiddleCenter;
        Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1000f, 92f));

        pingText = MakeText(panel.transform, "", 30, Muted);
        pingText.alignment = TextAnchor.MiddleRight;
        Place(pingText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -18f), new Vector2(300f, 92f));

        // opções (idioma, servidores...): só na vista de salas, no canto de cima à direita
        optionsButton = MakeButton(panel.transform, "Opções", 30, ShowOptions);
        Place((RectTransform)optionsButton.transform, new Vector2(1f, 1f), new Vector2(-30f, -22f), new Vector2(210f, 72f), new Vector2(1f, 1f));

        BuildLobbyView();
        BuildRoomView();
        BuildRegionMenu();
        BuildOptions();

        // aviso temporário no rodapé
        var pill = NewRect("Toast", panel.transform);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 0f);
        pill.anchoredPosition = new Vector2(0f, 24f);
        var pillImg = pill.gameObject.AddComponent<Image>();
        pillImg.sprite = Rounded;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color(0.16f, 0.15f, 0.22f, 0.97f);
        pillImg.raycastTarget = false;
        var hlg = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(40, 40, 12, 12);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        toastText = MakeText(pill, "", 28, Color.white);
        toastText.alignment = TextAnchor.MiddleCenter;
        toast = pill.gameObject;
        toast.SetActive(false);

        panel.SetActive(false);
    }

    private void BuildLobbyView()
    {
        lobbyView = NewRect("Lobby", panel.transform).gameObject;
        var root = (RectTransform)lobbyView.transform;
        Stretch(root);
        root.offsetMax = new Vector2(0f, -128f);

        // coluna da esquerda: jogador, região, criar, código
        var left = MakePanel(root, "Left", Card).rectTransform;
        left.anchorMin = new Vector2(0f, 0f);
        left.anchorMax = new Vector2(0f, 1f);
        left.pivot = new Vector2(0f, 1f);
        left.offsetMin = new Vector2(30f, 30f);
        left.offsetMax = new Vector2(30f + 560f, 0f);

        float y = -46f;
        Label(left, "Seu nome", ref y);
        nameField = MakeInputField(left, "Como os outros vão te ver", 30f, 36);
        nameField.characterLimit = 24;
        nameField.text = OnlineClient.PlayerName;
        Row((RectTransform)nameField.transform, ref y, 84f);

        Label(left, "Região das salas", ref y);
        regionButton = MakeButton(left, "", 34, () => ToggleMenu(regionMenu));
        Row((RectTransform)regionButton.transform, ref y, 84f);

        y -= 14f;
        var createPublic = MakeButton(left, "Criar sala pública", 36, () => StartCoroutine(CreateRoom(true)), light: true);
        Row((RectTransform)createPublic.transform, ref y, 90f);
        var createPrivate = MakeButton(left, "Criar sala privada", 34, () => StartCoroutine(CreateRoom(false)));
        Row((RectTransform)createPrivate.transform, ref y, 84f);

        y -= 14f;
        Label(left, "Entrar com código", ref y);
        codeField = MakeInputField(left, "ABC12", 200f, 40);
        codeField.characterLimit = 5;
        codeField.characterValidation = InputField.CharacterValidation.Alphanumeric;
        codeField.onValueChanged.AddListener(s =>
        {
            string up = s.ToUpperInvariant();
            if (up != s)
            {
                codeField.text = up;
            }
        });
        Row((RectTransform)codeField.transform, ref y, 90f);
        var join = MakeButton(codeField.transform, "Entrar", 34, () => JoinRoom(codeField.text), light: true);
        Place((RectTransform)join.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(180f, 74f), new Vector2(1f, 0.5f));

        // coluna da direita: salas públicas
        var right = NewRect("Rooms", root);
        right.anchorMin = Vector2.zero;
        right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(30f + 560f + 30f, 30f);
        right.offsetMax = new Vector2(-30f, 0f);

        var header = MakeText(right, "Salas abertas", 40, Color.white);
        Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(500f, 80f));

        var refresh = MakeButton(right, "Atualizar", 32, () => StartCoroutine(RefreshRooms()));
        Place((RectTransform)refresh.transform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(230f, 76f));
        allRegionsButton = MakeButton(right, "Todas as regiões", 32, () =>
        {
            showAllRegions = !showAllRegions;
            UpdateRegionLabels();
            StartCoroutine(RefreshRooms());
        });
        Place((RectTransform)allRegionsButton.transform, new Vector2(1f, 1f), new Vector2(-250f, 0f), new Vector2(330f, 76f));

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(right, false);
        var scrt = (RectTransform)scrollGo.transform;
        Stretch(scrt);
        scrt.offsetMax = new Vector2(0f, -96f);
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 60f;
        var viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        scroll.viewport = viewport;
        roomList = NewRect("Content", viewport);
        roomList.anchorMin = new Vector2(0f, 1f);
        roomList.anchorMax = new Vector2(1f, 1f);
        roomList.pivot = new Vector2(0.5f, 1f);
        roomList.sizeDelta = Vector2.zero;
        var vlg = roomList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        roomList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = roomList;

        roomListStatus = MakeText(scrollGo.transform, "", 38, Muted);
        roomListStatus.alignment = TextAnchor.MiddleCenter;
        roomListStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(roomListStatus.rectTransform);
    }

    private void BuildRoomView()
    {
        roomView = NewRect("Room", panel.transform).gameObject;
        var root = (RectTransform)roomView.transform;
        Stretch(root);
        root.offsetMax = new Vector2(0f, -128f);

        // esquerda: código, informações, fase e ações
        var left = MakePanel(root, "Left", Card).rectTransform;
        left.anchorMin = new Vector2(0f, 0f);
        left.anchorMax = new Vector2(0f, 1f);
        left.pivot = new Vector2(0f, 1f);
        left.offsetMin = new Vector2(30f, 30f);
        left.offsetMax = new Vector2(30f + 640f, 0f);

        float y = -46f;
        Label(left, "Código da sala", ref y);
        roomCodeText = MakeText(left, "", 84, Color.white);
        roomCodeText.alignment = TextAnchor.MiddleCenter;
        Row(roomCodeText.rectTransform, ref y, 110f);
        roomInfoText = MakeText(left, "", 28, Muted);
        roomInfoText.alignment = TextAnchor.MiddleCenter;
        Row(roomInfoText.rectTransform, ref y, 50f);

        y -= 10f;
        Label(left, "Fase", ref y);
        levelText = MakeText(left, "", 34, Color.white);
        levelText.horizontalOverflow = HorizontalWrapMode.Wrap;
        levelText.verticalOverflow = VerticalWrapMode.Truncate;
        Row(levelText.rectTransform, ref y, 112f);
        chooseLevelButton = MakeButton(left, "Escolher fase", 32, ShowLevelPicker);
        Row((RectTransform)chooseLevelButton.transform, ref y, 70f);

        // modificadores da partida: o anfitrião escolhe, os outros só veem
        immortalButton = MakeButton(left, "Imortal", 26, ToggleImmortal);
        Place((RectTransform)immortalButton.transform, new Vector2(0f, 1f), new Vector2(44f, y), new Vector2(270f, 64f), new Vector2(0f, 1f));
        speedButton = MakeButton(left, "Velocidade", 26, CycleSpeed);
        Place((RectTransform)speedButton.transform, new Vector2(0f, 1f), new Vector2(44f + 282f, y), new Vector2(270f, 64f), new Vector2(0f, 1f));
        modsText = MakeText(left, "", 26, Muted);
        modsText.alignment = TextAnchor.MiddleCenter;
        Place(modsText.rectTransform, new Vector2(0f, 1f), new Vector2(44f, y), new Vector2(552f, 64f), new Vector2(0f, 1f));
        y -= 76f;

        // botões de baixo
        var leave = MakeButton(left, "Sair da sala", 32, () => client.Leave());
        Place((RectTransform)leave.transform, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(260f, 84f));
        readyButton = MakeButton(left, "Pronto", 36, () =>
        {
            var me = client.GetPlayer(client.myId);
            client.SetReady(me == null || !me.ready);
        }, light: true);
        Place((RectTransform)readyButton.transform, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(290f, 84f));

        // direita: jogadores
        var right = NewRect("Players", root);
        right.anchorMin = Vector2.zero;
        right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(30f + 640f + 30f, 30f);
        right.offsetMax = new Vector2(-30f, 0f);
        var header = MakeText(right, $"Jogadores (até {OnlineClient.MaxPlayers})", 40, Color.white);
        Place(header.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(700f, 80f));
        playerList = NewRect("List", right);
        playerList.anchorMin = new Vector2(0f, 1f);
        playerList.anchorMax = new Vector2(1f, 1f);
        playerList.pivot = new Vector2(0.5f, 1f);
        playerList.sizeDelta = Vector2.zero; // sem isto o RectTransform novo traz 100 de largura a mais e a lista invade o painel da esquerda
        playerList.anchoredPosition = new Vector2(0f, -96f);
        var vlg = playerList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = PlayerRowSpacing;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        playerList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildChat(right);

        // começar fica embaixo, à direita, longe dos botões da coluna da sala (só o anfitrião vê)
        // fora de partida: começa; com a partida em andamento (anfitrião voltou à sala): encerra para todos
        startButton = MakeButton(right, "Começar partida", 40, () =>
        {
            if (client.inGame)
            {
                OnlineMatch.Instance.EndMatch();
            }
            else
            {
                client.StartGame();
            }
        }, light: true);
        Place((RectTransform)startButton.transform, new Vector2(1f, 1f), new Vector2(0f, 2f), new Vector2(400f, 80f));
    }

    private void BuildRegionMenu()
    {
        regionMenu = new GameObject("RegionMenu", typeof(RectTransform), typeof(Image), typeof(Button));
        regionMenu.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)regionMenu.transform);
        regionMenu.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        regionMenu.GetComponent<Button>().onClick.AddListener(() => regionMenu.SetActive(false));
        regionMenuList = MakePanel(regionMenu.transform, "List", new Color(0.12f, 0.11f, 0.17f, 1f)).rectTransform;
        Place(regionMenuList, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 0f));
        var vlg = regionMenuList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        regionMenuList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        foreach (var r in Regions)
        {
            string code = r.code;
            var b = MakeButton(regionMenuList, r.label, 34, () =>
            {
                OnlineClient.Region = code;
                regionMenu.SetActive(false);
                UpdateRegionLabels();
                StartCoroutine(RefreshRooms());
            });
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        }
        regionMenu.SetActive(false);
    }

    // ------------------------------------------------------------------ interface: conteúdo

    private void ShowView(bool inRoom)
    {
        lobbyView.SetActive(!inRoom);
        roomView.SetActive(inRoom);
        regionMenu.SetActive(false);
        optionsButton.gameObject.SetActive(!inRoom);
        if (optionsView != null)
        {
            optionsView.SetActive(false);
        }
        if (levelPicker != null)
        {
            levelPicker.SetActive(false);
        }
        SetText(titleText, inRoom ? (client.roomName ?? "Sala") : "Online");
        if (inRoom)
        {
            RebuildChat();
            RefreshRoom();
        }
        else
        {
            UpdateRegionLabels();
        }
    }

    private void UpdateRegionLabels()
    {
        string chosen = OnlineClient.Region;
        string label = string.IsNullOrEmpty(chosen)
            ? $"Automática ({RegionLabel(client.detectedRegion ?? "")})"
            : RegionLabel(chosen);
        var t = regionButton.GetComponentInChildren<Text>();
        if (t == null)
        {
            t = MakeText(regionButton.transform, "", 34, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }
        SetText(t, label + "  v");
        for (int i = 0; i < regionMenuList.childCount; i++)
        {
            var b = regionMenuList.GetChild(i).GetComponent<Button>();
            if (b != null)
            {
                SetSelected(b, Regions[i].code == chosen, Color.clear);
            }
        }
        SetSelected(allRegionsButton, showAllRegions, DarkTile);
    }

    private void RefreshRoom()
    {
        if (!IsOpen)
        {
            return;
        }
        bool inRoom = client.state == OnlineClient.State.InRoom;
        if (inRoom != roomView.activeSelf)
        {
            ShowView(inRoom);
            return;
        }
        if (!inRoom)
        {
            return;
        }
        SetText(titleText, client.roomName ?? "Sala");
        SetText(roomCodeText, client.roomCode ?? "");
        string where = RegionLabel(client.roomRegion ?? "");
        SetText(roomInfoText, string.IsNullOrEmpty(client.roomColo) ? where : $"{where} · servidor {client.roomColo}");

        var level = client.level;
        string title = level?.Value<string>("title");
        string artist = level?.Value<string>("artist");
        var match = OnlineMatch.Instance;
        string status;
        switch (match.levelState)
        {
            case OnlineMatch.LevelState.Checking:
                status = "<color=#ffffff88>Procurando a fase…</color>";
                break;
            case OnlineMatch.LevelState.Downloading:
                status = $"<color=#7fb2ff>Baixando {match.downloadProgress * 100f:0}%</color>";
                break;
            case OnlineMatch.LevelState.Ready:
                status = "<color=#8be28b>Fase pronta neste aparelho</color>";
                break;
            case OnlineMatch.LevelState.Missing:
            case OnlineMatch.LevelState.Failed:
                status = $"<color=#ff6b52>{match.levelError}</color>";
                break;
            default:
                status = "";
                break;
        }
        SetText(levelText, string.IsNullOrEmpty(title)
            ? "<color=#ffffff88>Nenhuma fase escolhida</color>"
            : title + (string.IsNullOrEmpty(artist) ? "" : $"  <color=#ffffff88>{artist}</color>") + "\n<size=38>" + status + "</size>");
        chooseLevelButton.gameObject.SetActive(client.isHost);
        RefreshMods();

        ClearChildren(playerList);
        foreach (var p in client.players)
        {
            AddPlayerRow(p);
        }

        var me = client.GetPlayer(client.myId);
        bool ready = me != null && me.ready;
        SetText(readyButton.GetComponentInChildren<Text>(), ready ? "Não estou pronto" : "Pronto");
        SetSelected(readyButton, !ready, DarkTile);
        // só dá para ficar pronto com a fase instalada neste aparelho
        readyButton.interactable = ready || match.levelState == OnlineMatch.LevelState.Ready;
        bool allReady = client.players.Count > 0 && client.players.All(p => p.ready);
        startButton.gameObject.SetActive(client.isHost);
        // o chat começa logo abaixo do último jogador e vai até o fim da coluna
        float listBottom = 96f + client.players.Count * (PlayerRowHeight + PlayerRowSpacing) + 12f;
        chatPanel.offsetMin = new Vector2(chatPanel.offsetMin.x, 0f);
        chatPanel.offsetMax = new Vector2(chatPanel.offsetMax.x, -listBottom);
        startButton.interactable = client.inGame || (allReady && level != null);
        string startLabel = client.inGame ? "Encerrar partida" : "Começar partida";
        var startText = startButton.GetComponentInChildren<Text>();
        if (startText.text != Loc.T(startLabel))
        {
            SetText(startText, startLabel);
        }
        readyButton.gameObject.SetActive(!client.inGame);

        if (match.resultsUnseen && match.lastResults != null)
        {
            ShowResults(match.lastResults);
        }
    }

    // ------------------------------------------------------------------ chat da sala

    private RectTransform chatPanel;
    private const float PlayerRowHeight = 60f;
    private const float PlayerRowSpacing = 6f;
    private RectTransform chatContent;
    private ScrollRect chatScroll;
    private InputField chatField;

    private void BuildChat(RectTransform right)
    {
        // embaixo da lista de jogadores (4 linhas), em cima do botão de começar
        chatPanel = MakePanel(right, "Chat", new Color(0f, 0f, 0f, 0.35f)).rectTransform;
        chatPanel.anchorMin = new Vector2(0f, 0f);
        chatPanel.anchorMax = new Vector2(1f, 1f);
        chatPanel.offsetMin = new Vector2(0f, 120f);
        chatPanel.offsetMax = new Vector2(0f, -470f);

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(chatPanel, false);
        var scrt = (RectTransform)scrollGo.transform;
        Stretch(scrt);
        scrt.offsetMin = new Vector2(20f, 96f);
        scrt.offsetMax = new Vector2(-20f, -12f);
        chatScroll = scrollGo.GetComponent<ScrollRect>();
        chatScroll.horizontal = false;
        chatScroll.scrollSensitivity = 40f;
        var viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        chatScroll.viewport = viewport;
        chatContent = NewRect("Content", viewport);
        chatContent.anchorMin = new Vector2(0f, 1f);
        chatContent.anchorMax = new Vector2(1f, 1f);
        chatContent.pivot = new Vector2(0.5f, 1f);
        chatContent.sizeDelta = Vector2.zero;
        var vlg = chatContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        chatContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        chatScroll.content = chatContent;

        chatField = MakeInputField(chatPanel, "Escreva uma mensagem…", 30f, 32);
        chatField.characterLimit = 140;
        var frt = (RectTransform)chatField.transform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.offsetMin = new Vector2(16f, 12f);
        frt.offsetMax = new Vector2(-220f, 84f);
        chatField.onEndEdit.AddListener(_ =>
        {
            // Enter no teclado (PC) ou "concluído" no teclado do celular envia
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                (TouchScreenKeyboard.isSupported && chatField.touchScreenKeyboard != null &&
                 chatField.touchScreenKeyboard.status == TouchScreenKeyboard.Status.Done))
            {
                SendChat();
            }
        });
        var send = MakeButton(chatPanel, "Enviar", 32, SendChat, light: true);
        Place((RectTransform)send.transform, new Vector2(1f, 0f), new Vector2(-12f, 10f), new Vector2(200f, 76f), new Vector2(1f, 0f));
    }

    private void SendChat()
    {
        string text = (chatField.text ?? "").Trim();
        if (text.Length == 0 || client.state != OnlineClient.State.InRoom)
        {
            return;
        }
        client.SendChat(text); // o servidor devolve para todos (inclusive quem mandou), e aí entra no histórico
        chatField.text = "";
    }

    private void RebuildChat()
    {
        if (chatContent == null)
        {
            return;
        }
        ClearChildren(chatContent);
        foreach (var line in client.chatLog)
        {
            AddChatLine(line);
        }
        ScrollChatToEnd();
    }

    private void AppendChat(OnlineClient.ChatLine line)
    {
        if (chatContent == null)
        {
            return;
        }
        // mantém só o tamanho do histórico na tela
        while (chatContent.childCount >= OnlineClient.ChatHistory)
        {
            DestroyImmediate(chatContent.GetChild(0).gameObject);
        }
        AddChatLine(line);
        ScrollChatToEnd();
    }

    private void AddChatLine(OnlineClient.ChatLine line)
    {
        // o texto dos jogadores não pode virar formatação (cores, tamanhos...): "<" com um espaço invisível
        string safe = (line.text ?? "").Replace("<", "<\u200B");
        string text = line.system
            ? $"<color=#ffffff77><i>{safe}</i></color>"
            : $"<color={(line.mine ? "#8be28b" : "#ffd166")}>{(line.name ?? "").Replace("<", "<\u200B")}:</color>  {safe}";
        var t = MakeText(chatContent, text, 28, Color.white);
        t.gameObject.name = "ChatLine"; // o texto dos jogadores não é traduzido (Loc)
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.alignment = TextAnchor.UpperLeft;
        // a altura de cada mensagem vem da própria lista (VerticalLayoutGroup controla a altura dos filhos);
        // um ContentSizeFitter aqui brigava com ela e deixava as linhas com altura zero, uma em cima da outra
    }

    private void ScrollChatToEnd()
    {
        Canvas.ForceUpdateCanvases();
        if (chatContent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
        }
        if (chatScroll != null)
        {
            chatScroll.verticalNormalizedPosition = 0f;
        }
    }

    // ------------------------------------------------------------------ placar da última partida

    private GameObject resultsPanel;

    private void ShowResults(Newtonsoft.Json.Linq.JArray results)
    {
        OnlineMatch.Instance.resultsUnseen = false;
        if (resultsPanel != null)
        {
            Destroy(resultsPanel);
        }
        resultsPanel = new GameObject("Results", typeof(RectTransform), typeof(Image));
        resultsPanel.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)resultsPanel.transform);
        resultsPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        var box = MakePanel(resultsPanel.transform, "Box", new Color(0.09f, 0.08f, 0.13f, 1f)).rectTransform;
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1250f, 160f + results.Count * 120f + 120f);

        var title = MakeText(box, "Resultado da partida", 52, Color.white);
        title.alignment = TextAnchor.MiddleCenter;
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1100f, 100f));

        float y = -140f;
        int pos = 0;
        foreach (var row in results)
        {
            pos++;
            bool finished = row.Value<bool>("finished");
            bool left = row.Value<bool?>("left") ?? false;
            bool quit = row.Value<bool?>("quit") ?? false;
            int deaths = row.Value<int?>("deaths") ?? 0;
            float acc = row.Value<float?>("acc") ?? -1f;
            float progress = row.Value<float?>("progress") ?? 0f;
            int id = row.Value<int>("id");

            var line = MakePanel(box, "Row", pos == 1 && finished ? new Color(1f, 0.82f, 0.4f, 0.16f) : Card).rectTransform;
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.offsetMin = new Vector2(30f, y - 105f);
            line.offsetMax = new Vector2(-30f, y);
            y -= 120f;

            string medal = pos == 1 ? "#ffd166" : pos == 2 ? "#d9dee6" : pos == 3 ? "#e3a06b" : "#ffffff88";
            var place = MakeText(line, $"<color={medal}>{pos}º</color>", 48, Color.white);
            place.alignment = TextAnchor.MiddleCenter;
            Place(place.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(110f, 100f), new Vector2(0f, 0.5f));

            string name = row.Value<string>("name") + (id == client.myId ? "  <color=#ffffff88>(você)</color>" : "") +
                          (left ? "  <color=#ffffff66>(saiu)</color>" : "");
            var nameText = MakeText(line, name, 40, Color.white);
            nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            nameText.rectTransform.offsetMin = new Vector2(140f, 0f);
            nameText.rectTransform.offsetMax = Vector2.zero;

            string quedas = deaths == 1 ? "1 queda" : $"{deaths} quedas";
            string detail = finished
                ? $"<color=#8be28b>Terminou</color>   {(acc >= 0f ? $"{acc * 100f:0.00}%" : "")}   ·   {quedas}"
                : $"<color=#ffffff88>{(quit ? "Desistiu" : "Não terminou")}</color>   {progress * 100f:0}% da fase   ·   {quedas}";
            var detailText = MakeText(line, detail, 32, Color.white);
            detailText.alignment = TextAnchor.MiddleRight;
            detailText.rectTransform.anchorMin = new Vector2(0.4f, 0f);
            detailText.rectTransform.anchorMax = new Vector2(1f, 1f);
            detailText.rectTransform.offsetMin = Vector2.zero;
            detailText.rectTransform.offsetMax = new Vector2(-30f, 0f);
        }

        var close = MakeButton(box, "Fechar", 36, () => Destroy(resultsPanel), light: true);
        Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(300f, 86f));
    }

    private void AddPlayerRow(OnlineClient.Player p)
    {
        var row = MakePanel(playerList, "Player " + p.id, Card).rectTransform;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = PlayerRowHeight;

        string name = p.name + (p.id == client.myId ? "  <color=#ffffff88>(você)</color>" : "");
        var nameText = MakeText(row, name, 32, Color.white);
        nameText.rectTransform.anchorMin = Vector2.zero;
        nameText.rectTransform.anchorMax = Vector2.one;
        nameText.rectTransform.offsetMin = new Vector2(30f, 0f);
        nameText.rectTransform.offsetMax = new Vector2(-420f, 0f);

        var tags = new List<string>();
        if (p.id == client.hostId)
        {
            tags.Add("<color=#ffd166>Anfitrião</color>");
        }
        tags.Add(p.ready ? "<color=#8be28b>Pronto</color>" : "<color=#ffffff88>Esperando</color>");
        var tagText = MakeText(row, string.Join("   ", tags), 26, Color.white);
        tagText.alignment = TextAnchor.MiddleRight;
        tagText.rectTransform.anchorMin = Vector2.zero;
        tagText.rectTransform.anchorMax = Vector2.one;
        tagText.rectTransform.offsetMin = new Vector2(0f, 0f);
        tagText.rectTransform.offsetMax = new Vector2(client.isHost && p.id != client.myId ? -180f : -30f, 0f);

        if (client.isHost && p.id != client.myId)
        {
            int id = p.id;
            var kick = MakeButton(row, "Remover", 28, () => client.Kick(id));
            Place((RectTransform)kick.transform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(160f, 50f), new Vector2(1f, 0.5f));
        }
    }

    private void AddSection(string title)
    {
        var t = MakeText(roomList, title, 32, new Color(1f, 0.85f, 0.5f));
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
    }

    private void AddRoomRow(OnlineClient.RoomInfo r)
    {
        var row = MakePanel(roomList, "Room " + r.code, r.inGame ? new Color(1f, 0.82f, 0.4f, 0.06f) : Card).rectTransform;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 128f;

        // nome da sala e fase
        var title = MakeText(row, r.name, 38, Color.white);
        title.alignment = TextAnchor.LowerLeft;
        title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(30f, 0f);
        title.rectTransform.offsetMax = new Vector2(-560f, -8f);
        var level = MakeText(row, string.IsNullOrEmpty(r.level)
            ? "<color=#ffffff66>Fase ainda não escolhida</color>"
            : "<color=#ffffff88>Fase:</color> " + r.level, 28, Muted);
        level.alignment = TextAnchor.UpperLeft;
        level.horizontalOverflow = HorizontalWrapMode.Wrap;
        level.verticalOverflow = VerticalWrapMode.Truncate;
        level.rectTransform.anchorMin = new Vector2(0f, 0f);
        level.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        level.rectTransform.offsetMin = new Vector2(30f, 8f);
        level.rectTransform.offsetMax = new Vector2(-560f, -4f);

        // ocupação (bolinhas) e região
        var seats = new System.Text.StringBuilder();
        for (int i = 0; i < r.maxPlayers; i++)
        {
            seats.Append(i < r.players ? "<color=#8be28b>●</color>" : "<color=#ffffff40>●</color>");
        }
        var occupancy = MakeText(row, seats + $"  <size=26>{r.players}/{r.maxPlayers}</size>", 34, Color.white);
        occupancy.alignment = TextAnchor.LowerCenter;
        Place(occupancy.rectTransform, new Vector2(1f, 0.5f), new Vector2(-280f, 4f), new Vector2(260f, 56f), new Vector2(1f, 0f));
        var region = MakeText(row, RegionLabel(r.region), 24, Muted);
        region.alignment = TextAnchor.UpperCenter;
        Place(region.rectTransform, new Vector2(1f, 0.5f), new Vector2(-280f, -2f), new Vector2(260f, 40f), new Vector2(1f, 1f));

        // partida em andamento: entra assistindo (o servidor marca como espectador até a partida acabar)
        bool canJoin = r.inGame || r.players < r.maxPlayers;
        string code = r.code;
        var join = MakeButton(row, r.inGame ? "Assistir" : canJoin ? "Entrar" : "Cheia", 34, () => JoinRoom(code), light: canJoin);
        Place((RectTransform)join.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(240f, 92f), new Vector2(1f, 0.5f));
        join.interactable = canJoin;
    }

    private void OnBack()
    {
        if (optionsView != null && optionsView.activeSelf)
        {
            optionsView.SetActive(false);
            return;
        }
        if (regionMenu.activeSelf)
        {
            regionMenu.SetActive(false);
            return;
        }
        if (levelPicker != null && levelPicker.activeSelf)
        {
            levelPicker.SetActive(false);
            return;
        }
        if (resultsPanel != null)
        {
            Destroy(resultsPanel);
            return;
        }
        // sair da tela não tira da sala: dá para voltar ao CLS e reabrir pelo menu de pausa
        SetOpen(false);
    }

    /// <summary>Botão "voltar" do celular / Esc.</summary>
    public static void Back()
    {
        if (instance != null)
        {
            instance.OnBack();
        }
    }

    private void ToggleMenu(GameObject menu)
    {
        menu.SetActive(!menu.activeSelf);
        menu.transform.SetAsLastSibling();
    }

    private void ShowToast(string message)
    {
        if (toast == null || string.IsNullOrEmpty(message))
        {
            return;
        }
        SetText(toastText, message);
        toast.SetActive(true);
        toast.transform.SetAsLastSibling();
        toastUntil = Time.unscaledTime + 4f;
    }

    private void SetListStatus(string s)
    {
        SetText(roomListStatus, s ?? "");
        roomListStatus.gameObject.SetActive(!string.IsNullOrEmpty(s));
    }

    // ------------------------------------------------------------------ escolha de fase (anfitrião)

    private GameObject levelPicker;
    private RectTransform levelPickerList;
    private InputField levelPickerSearch;

    private void ShowLevelPicker()
    {
        if (!client.isHost)
        {
            return;
        }
        if (levelPicker == null)
        {
            BuildLevelPicker();
        }
        levelPicker.SetActive(true);
        levelPicker.transform.SetAsLastSibling();
        levelPickerSearch.text = "";
        FillLevelPicker("");
    }

    private void BuildLevelPicker()
    {
        levelPicker = new GameObject("LevelPicker", typeof(RectTransform), typeof(Image), typeof(Button));
        levelPicker.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)levelPicker.transform);
        levelPicker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        levelPicker.GetComponent<Button>().onClick.AddListener(() => levelPicker.SetActive(false));

        var box = MakePanel(levelPicker.transform, "Box", new Color(0.09f, 0.08f, 0.13f, 1f)).rectTransform;
        box.anchorMin = new Vector2(0.5f, 0.5f);
        box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1300f, 800f);
        box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // toques na caixa não fecham

        var title = MakeText(box, "Escolher fase", 44, Color.white);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -20f), new Vector2(600f, 80f));
        var community = MakeButton(box, "Da comunidade (ADOFAI.gg / TUF)", 32, PickCommunityLevel, light: true);
        Place((RectTransform)community.transform, new Vector2(1f, 1f), new Vector2(-30f, -20f), new Vector2(560f, 80f));

        levelPickerSearch = MakeInputField(box, "Buscar nas minhas fases", 30f, 34);
        var srt = (RectTransform)levelPickerSearch.transform;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.offsetMin = new Vector2(30f, -200f);
        srt.offsetMax = new Vector2(-30f, -116f);
        levelPickerSearch.onValueChanged.AddListener(FillLevelPicker);

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(box, false);
        var scrt = (RectTransform)scrollGo.transform;
        Stretch(scrt);
        scrt.offsetMin = new Vector2(30f, 30f);
        scrt.offsetMax = new Vector2(-30f, -220f);
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 60f;
        var viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        scroll.viewport = viewport;
        levelPickerList = NewRect("Content", viewport);
        levelPickerList.anchorMin = new Vector2(0f, 1f);
        levelPickerList.anchorMax = new Vector2(1f, 1f);
        levelPickerList.pivot = new Vector2(0.5f, 1f);
        levelPickerList.sizeDelta = Vector2.zero;
        var vlg = levelPickerList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        levelPickerList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = levelPickerList;
    }

    private void FillLevelPicker(string search)
    {
        ClearChildren(levelPickerList);
        var levels = scnCLS.instance != null
            ? scnCLS.instance.GetLocalLevels()
            : new List<KeyValuePair<string, ADOFAI.LevelDataCLS>>();
        search = (search ?? "").Trim();
        int shown = 0;
        foreach (var item in levels.OrderBy(l => StripTags(l.Value.title)))
        {
            var lv = item.Value;
            string title = StripTags(lv.title);
            string artist = StripTags(lv.artist);
            string author = StripTags(lv.author);
            if (search.Length > 0 && (title + " " + artist + " " + author).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (++shown > 150)
            {
                break; // lista grande: a busca filtra o resto
            }
            string hash = lv.Hash;
            var row = MakeButton(levelPickerList, "", 0, () =>
            {
                levelPicker.SetActive(false);
                StartCoroutine(PickLocalLevel(hash, title, artist, author));
            });
            row.GetComponent<Image>().color = Card;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;
            var t = MakeText(row.transform, title + (artist.Length > 0 ? $"  <color=#ffffff88>{artist}</color>" : "") +
                                            (author.Length > 0 ? $"   <size=24><color=#ffffff66>por {author}</color></size>" : ""), 34, Color.white);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(26f, 0f);
            t.rectTransform.offsetMax = new Vector2(-26f, 0f);
        }
        if (shown == 0)
        {
            var empty = MakeText(levelPickerList, levels.Count == 0
                ? "Você ainda não tem fases. Escolha uma da comunidade."
                : "Nenhuma fase encontrada.", 32, Muted);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        }
    }

    // fase que o anfitrião já tem: procura o link no ADOFAI.gg/TUF para quem não tiver poder baixar
    private IEnumerator PickLocalLevel(string hash, string title, string artist, string author)
    {
        ShowToast("Procurando a fase no ADOFAI.gg e no TUF…");
        string url = null, key = null;
        yield return LevelBrowser.FindDownload(title, artist, author, (u, k) =>
        {
            url = u;
            key = k;
        });
        var level = new Newtonsoft.Json.Linq.JObject
        {
            ["source"] = "local",
            ["hash"] = hash,
            ["title"] = title,
            ["artist"] = artist,
            ["author"] = author,
        };
        bool shared = false;
        if (url == null)
        {
            // fora das fontes: envia a fase para o servidor da sala, automaticamente
            ShowToast("Fase fora do ADOFAI.gg/TUF: enviando para a sala…");
            yield return OnlineMatch.Instance.ShareLevel(hash, ShowToast, (u, k) =>
            {
                url = u;
                key = k;
                shared = u != null;
            });
        }
        if (url != null)
        {
            level["url"] = url;
            level["key"] = key;
            ShowToast(shared ? "Fase enviada para a sala: quem não tiver vai baixar automaticamente." : "Fase encontrada: quem não tiver vai baixar automaticamente.");
        }
        else
        {
            ShowToast("Não deu para enviar a fase: só quem já tem poderá jogar.");
        }
        if (client.isHost)
        {
            client.SetLevel(level);
        }
    }

    // ------------------------------------------------------------------ opções (idioma, servidores, atualizações)

    private void BuildOptions()
    {
        optionsView = new GameObject("Options", typeof(RectTransform), typeof(Image), typeof(Button));
        optionsView.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)optionsView.transform);
        optionsView.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        optionsView.GetComponent<Button>().onClick.AddListener(() => optionsView.SetActive(false));

        var box = MakePanel(optionsView.transform, "Box", new Color(0.09f, 0.08f, 0.13f, 1f)).rectTransform;
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // toques na caixa não fecham

        var title = MakeText(box, "Opções", 46, Color.white);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -22f), new Vector2(600f, 80f));

        float y = -120f;
        Label(box, "Idioma", ref y);
        languageButton = MakeButton(box, "…", 32, CycleLanguage); // o texto vem de RefreshOptionsLabels
        Row((RectTransform)languageButton.transform, ref y, 76f);

        if (OnlineOptions.CanEditServers)
        {
            Label(box, "Servidor principal", ref y);
            primaryField = MakeInputField(box, "https://…", 30f, 30);
            Row((RectTransform)primaryField.transform, ref y, 76f);
            if (OnlineOptions.HasFallbackServer)
            {
                Label(box, "Servidor de reserva", ref y);
                fallbackField = MakeInputField(box, "https://…", 30f, 30);
                Row((RectTransform)fallbackField.transform, ref y, 76f);
            }
            var hint = MakeText(box, "Todos da sala precisam usar o mesmo servidor.", 24, Muted);
            Row(hint.rectTransform, ref y, 40f);
            resetServersButton = MakeButton(box, "Restaurar servidores padrão", 28, ResetServersClicked);
            Row((RectTransform)resetServersButton.transform, ref y, 70f);
        }
        if (OnlineOptions.HasUpdateCheck)
        {
            updatesButton = MakeButton(box, "…", 28, ToggleUpdates);
            Row((RectTransform)updatesButton.transform, ref y, 70f);
        }

        box.sizeDelta = new Vector2(1100f, -y + 140f);
        var save = MakeButton(box, "Salvar e fechar", 34, SaveOptions, light: true);
        Place((RectTransform)save.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(420f, 88f), new Vector2(0.5f, 0f));
        optionsView.SetActive(false);
    }

    private void ShowOptions()
    {
        if (client.state == OnlineClient.State.InRoom || optionsView == null)
        {
            return;
        }
        if (primaryField != null)
        {
            primaryField.text = OnlineOptions.PrimaryServer;
        }
        if (fallbackField != null)
        {
            fallbackField.text = OnlineOptions.FallbackServer;
        }
        RefreshOptionsLabels();
        optionsView.SetActive(true);
        optionsView.transform.SetAsLastSibling();
        ClearFocus();
    }

    private static string LanguageName(string code)
    {
        // cada idioma aparece no próprio nome (não passam pela tradução)
        return code == "en" ? "English" : code == "pt" ? "Português" : code == "ko" ? "한국어" : code == "auto" ? "Automático (idioma do jogo)" : Loc.PackName(code);
    }

    private void RefreshOptionsLabels()
    {
        SetText(languageButton.GetComponentInChildren<Text>(), LanguageName(OnlineOptions.Language));
        if (updatesButton != null)
        {
            SetText(updatesButton.GetComponentInChildren<Text>(),
                "Verificar atualizações do mod: " + (OnlineOptions.CheckForUpdates ? "Sim" : "Não"));
        }
    }

    private void CycleLanguage()
    {
        int i = Array.IndexOf(OnlineOptions.Languages, OnlineOptions.Language);
        OnlineOptions.Language = OnlineOptions.Languages[(i + 1) % OnlineOptions.Languages.Length];
        RefreshOptionsLabels();
    }

    private void ToggleUpdates()
    {
        OnlineOptions.CheckForUpdates = !OnlineOptions.CheckForUpdates;
        RefreshOptionsLabels();
    }

    private void ResetServersClicked()
    {
        if (primaryField != null)
        {
            primaryField.text = OnlineOptions.DefaultPrimary;
        }
        if (fallbackField != null)
        {
            fallbackField.text = OnlineOptions.DefaultFallback;
        }
    }

    private void SaveOptions()
    {
        string p = primaryField != null ? primaryField.text.Trim().TrimEnd('/') : null;
        string fb = fallbackField != null ? fallbackField.text.Trim().TrimEnd('/') : null;
        if ((p != null && !OnlineOptions.IsValidServer(p)) || (fb != null && !OnlineOptions.IsValidServer(fb)))
        {
            ShowToast("Endereço inválido: use https://…");
            return;
        }
        bool changed = false;
        if (p != null && p != OnlineOptions.PrimaryServer)
        {
            OnlineOptions.PrimaryServer = p;
            changed = true;
        }
        if (fb != null && fb != OnlineOptions.FallbackServer)
        {
            OnlineOptions.FallbackServer = fb;
            changed = true;
        }
        optionsView.SetActive(false);
        if (changed)
        {
            OnlineClient.ResetActiveServer();
            ShowToast("Servidor salvo. Atualizando as salas…");
            StartCoroutine(Connect());
        }
    }

    // ------------------------------------------------------------------ teclado (e botões de controle)
    // Tab / Shift+Tab percorrem os itens; as setas vão ao item mais próximo na direção; Enter, Espaço ou o botão A
    // acionam; Esc ou o botão B voltam. O item marcado ganha um contorno.

    private Transform NavRoot()
    {
        if (optionsView != null && optionsView.activeSelf)
        {
            return optionsView.transform;
        }
        if (levelPicker != null && levelPicker.activeSelf)
        {
            return levelPicker.transform;
        }
        if (resultsPanel != null)
        {
            return resultsPanel.transform;
        }
        return panel.transform;
    }

    private List<Selectable> NavTargets()
    {
        var list = new List<Selectable>();
        foreach (var s in NavRoot().GetComponentsInChildren<Selectable>(false))
        {
            if (s == null || !s.interactable || !s.gameObject.activeInHierarchy || s is Scrollbar)
            {
                continue;
            }
            if (s.gameObject == optionsView || s.gameObject == levelPicker)
            {
                continue; // o fundo escuro que fecha a janela ao tocar não é um item
            }
            if (s.transition == Selectable.Transition.None && !(s is InputField))
            {
                continue; // fundos que só fecham a janela ao tocar
            }
            list.Add(s);
        }
        return list;
    }

    private static Vector2 CenterOf(Selectable s)
    {
        var rt = (RectTransform)s.transform;
        return (Vector2)rt.TransformPoint(rt.rect.center);
    }

    private static float ReadingOrder(Selectable s)
    {
        Vector2 c = CenterOf(s);
        return -Mathf.Round(c.y / 60f) * 100000f + c.x; // de cima para baixo, da esquerda para a direita
    }

    private void ClearFocus()
    {
        if (focus != null && focus.targetGraphic != null)
        {
            var o = focus.targetGraphic.GetComponent<Outline>();
            if (o != null)
            {
                Destroy(o);
            }
        }
        focus = null;
    }

    private void SetFocus(Selectable next)
    {
        ClearFocus();
        focus = next;
        if (next.targetGraphic != null)
        {
            var o = next.targetGraphic.GetComponent<Outline>() ?? next.targetGraphic.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(1f, 0.85f, 0.4f, 1f);
            o.effectDistance = new Vector2(4f, -4f);
        }
        // dentro de uma lista com rolagem, traz o item para a vista
        var sr = next.GetComponentInParent<ScrollRect>();
        var canvas = next.GetComponentInParent<Canvas>();
        if (sr != null && sr.content != null && canvas != null && next.transform.IsChildOf(sr.content))
        {
            Canvas.ForceUpdateCanvases();
            var viewport = sr.viewport != null ? sr.viewport : (RectTransform)sr.transform;
            var item = new Vector3[4];
            var view = new Vector3[4];
            ((RectTransform)next.transform).GetWorldCorners(item);
            viewport.GetWorldCorners(view);
            float over = 0f;
            if (item[1].y > view[1].y)
            {
                over = item[1].y - view[1].y;
            }
            else if (item[0].y < view[0].y)
            {
                over = item[0].y - view[0].y;
            }
            if (over != 0f)
            {
                sr.content.anchoredPosition -= new Vector2(0f, over / canvas.scaleFactor);
            }
        }
    }

    private void Navigate(int tab, Vector2 dir)
    {
        var targets = NavTargets();
        if (targets.Count == 0)
        {
            return;
        }
        Selectable current = focus != null && targets.Contains(focus) ? focus : null;
        Selectable next = null;
        if (current == null)
        {
            next = targets.OrderBy(ReadingOrder).First();
        }
        else if (tab != 0)
        {
            var ordered = targets.OrderBy(ReadingOrder).ToList();
            int i = ordered.IndexOf(current);
            next = ordered[(i + tab + ordered.Count) % ordered.Count];
        }
        else
        {
            Vector2 c = CenterOf(current);
            float best = float.MaxValue;
            foreach (var t in targets)
            {
                if (t == current)
                {
                    continue;
                }
                Vector2 d = CenterOf(t) - c;
                float along = Vector2.Dot(d, dir);
                if (along <= 1f)
                {
                    continue;
                }
                float across = Mathf.Abs(Vector2.Dot(d, new Vector2(-dir.y, dir.x)));
                float score = along + across * 2.5f;
                if (score < best)
                {
                    best = score;
                    next = t;
                }
            }
            if (next == null)
            {
                return;
            }
        }
        SetFocus(next);
    }

    private void HandleKeyboard()
    {
        if (focus != null && (!focus.gameObject.activeInHierarchy || !NavTargets().Contains(focus)))
        {
            ClearFocus();
        }
        var es = EventSystem.current;
        bool typing = false;
        if (es != null && es.currentSelectedGameObject != null)
        {
            var field = es.currentSelectedGameObject.GetComponent<InputField>();
            typing = field != null && field.isFocused;
        }
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (typing)
            {
                es.SetSelectedGameObject(null); // sai da digitação
            }
            Navigate(shift ? -1 : 1, Vector2.zero);
            return;
        }
        if (typing)
        {
            return;
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            Navigate(0, Vector2.down);
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            Navigate(0, Vector2.up);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            Navigate(0, Vector2.right);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            Navigate(0, Vector2.left);
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            if (focus is InputField input)
            {
                input.ActivateInputField();
            }
            else if (focus is Button button)
            {
                button.onClick.Invoke();
            }
        }
        else if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            OnBack();
        }
    }

    private void ToggleImmortal()
    {
        if (client.isHost && !client.inGame)
        {
            client.SetMods(!client.modImmortal, client.modSpeed);
        }
    }

    private void CycleSpeed()
    {
        if (!client.isHost || client.inGame)
        {
            return;
        }
        int i = Array.FindIndex(SpeedSteps, v => Mathf.Abs(v - client.modSpeed) < 0.001f);
        client.SetMods(client.modImmortal, SpeedSteps[(i + 1) % SpeedSteps.Length]);
    }

    private static string SpeedLabel(float speed) => speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";

    private void RefreshMods()
    {
        bool host = client.isHost && !client.inGame;
        immortalButton.gameObject.SetActive(host);
        speedButton.gameObject.SetActive(host);
        modsText.gameObject.SetActive(!host);
        if (host)
        {
            SetText(immortalButton.GetComponentInChildren<Text>(), client.modImmortal ? "Imortal: sim" : "Imortal: não");
            SetSelected(immortalButton, client.modImmortal, DarkTile);
            SetText(speedButton.GetComponentInChildren<Text>(), "Velocidade " + SpeedLabel(client.modSpeed));
            SetSelected(speedButton, Mathf.Abs(client.modSpeed - 1f) > 0.001f, DarkTile);
        }
        else
        {
            var parts = new List<string>();
            if (client.modImmortal)
            {
                parts.Add("Imortal");
            }
            if (Mathf.Abs(client.modSpeed - 1f) > 0.001f)
            {
                parts.Add("Velocidade " + SpeedLabel(client.modSpeed));
            }
            SetText(modsText, parts.Count == 0 ? "Sem modificadores" : "Modificadores: " + string.Join(" · ", parts));
        }
    }

    private void PickCommunityLevel()
    {
        levelPicker.SetActive(false);
        SetOpen(false);
        LevelBrowser.PickForOnline((url, key, title, artist, author) =>
        {
            if (url != null && client.isHost)
            {
                client.SetLevel(new Newtonsoft.Json.Linq.JObject
                {
                    ["source"] = "url",
                    ["url"] = url,
                    ["key"] = key,
                    ["title"] = title,
                    ["artist"] = artist ?? "",
                    ["author"] = author ?? "", // para quem já tem a fase achar a própria cópia e não baixar de novo
                });
            }
            Open();
        });
    }

    // ------------------------------------------------------------------ estilo do jogo
    // Esta tela usa a arte de pedra do jogo: os nomes abaixo escondem os do PortUi (botões e colunas de pedra).

    private static Button MakeButton(Transform parent, string label, int size, Action onClick, bool light = false)
        => MakeStoneButton(parent, label, size, onClick, bright: light);

    private static void SetSelected(Button b, bool selected, Color idle) => SetStoneSelected(b, selected);

    // as colunas e as janelas ganham a moldura de pedra com tijolos; linhas e listas continuam simples
    private static Image MakePanel(Transform parent, string name, Color color)
        => name == "Left" || name == "Box" ? MakeStonePanel(parent, name) : PortUi.MakePanel(parent, name, color);

    // ------------------------------------------------------------------ utilidades

    private static void Label(RectTransform parent, string text, ref float y)
    {
        var t = MakeText(parent, text, 28, Muted);
        Row(t.rectTransform, ref y, 46f);
    }

    // empilha um elemento em largura total na coluna, de cima para baixo
    private static void Row(RectTransform rt, ref float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(44f, y - height);
        rt.offsetMax = new Vector2(-44f, y);
        y -= height + 12f;
    }

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Destroy(t.GetChild(i).gameObject);
        }
    }
}
