// Port: cliente das partidas online. Fala com o servidor de salas (Server/online, Cloudflare Workers) por HTTP
// (listar/criar salas) e WebSocket (dentro da sala). Mensagens são JSON; o protocolo está no servidor.
//
// O WebSocket roda em tarefas de fundo; as mensagens recebidas entram numa fila e são tratadas no Update, na
// thread principal, onde os eventos (OnRoomChanged, OnStart, OnGameEvent...) são disparados.
//
// Relógio: o servidor manda a hora dele no "pong"; com o tempo de ida e volta estimamos a diferença entre os
// relógios (ServerNow), usada para todos começarem a fase no mesmo instante.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class OnlineClient : MonoBehaviour
{
    public const int Protocol = 1;
    public const int MaxPlayers = 4;

    // Endereço do servidor publicado (wrangler deploy). Pode ser trocado na tela Online (fica salvo).
    // Servidores, em ordem de preferência: o próprio (Oracle Cloud, São Paulo; ping baixo no Brasil) e o da
    // Cloudflare (reserva). O jogo usa o primeiro que responder; todos os jogadores com a mesma versão caem no
    // mesmo servidor. Vazio = ainda não configurado.
    // no mod de PC os servidores vêm do arquivo de configuração do BepInEx: nada fixo no código público
    public static string PrimaryServerUrl => AdofaiOnlineMod.ServerConfig.Primary;
    public static string FallbackServerUrl => AdofaiOnlineMod.ServerConfig.Fallback;
    public static string DefaultServerUrl => FallbackServerUrl;
    private const string ServerPref = "online.server";
    private const string NamePref = "online.name";
    private const string RegionPref = "online.region";

    public class Player
    {
        public int id;
        public string name;
        public bool ready;
        public bool spectator; // entrou com a partida em andamento: assiste até ela acabar
    }

    public class RoomInfo
    {
        public string code;
        public string name;
        public string colo;
        public string region;
        public int players;
        public int maxPlayers;
        public bool inGame;
        public string level;
        public bool locked; // sala com senha
    }

    public enum State
    {
        Offline,
        Connecting,
        InRoom,
    }

    private static OnlineClient instance;

    public static OnlineClient Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("OnlineClient");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<OnlineClient>();
            }
            return instance;
        }
    }

    public static bool Exists => instance != null;

    // ------------------------------------------------------------------ preferências

    // servidor em uso nesta sessão (o primeiro da lista que respondeu)
    private static string activeServer;

    /// <summary>Endereço manual do servidor (preferências); vazio = automático (lista acima).</summary>
    public static string ServerOverride
    {
        get => Pref(ServerPref, "").Trim().TrimEnd('/');
        set => SetPref(ServerPref, value?.Trim().TrimEnd('/'));
    }

    public static string ServerUrl
    {
        get
        {
            string over = ServerOverride;
            if (over.Length > 0)
            {
                return over;
            }
            if (!string.IsNullOrEmpty(activeServer))
            {
                return activeServer;
            }
            return (string.IsNullOrEmpty(PrimaryServerUrl) ? FallbackServerUrl : PrimaryServerUrl).TrimEnd('/');
        }
        set => ServerOverride = value;
    }

    /// <summary>Esquece o servidor que respondeu antes (depois de mudar os endereços nas opções).</summary>
    public static void ResetActiveServer()
    {
        activeServer = null;
    }

    private static List<string> CandidateServers()
    {
        var list = new List<string>();
        string over = ServerOverride;
        if (over.Length > 0)
        {
            list.Add(over);
            return list;
        }
        if (!string.IsNullOrEmpty(PrimaryServerUrl))
        {
            list.Add(PrimaryServerUrl.TrimEnd('/'));
        }
        if (!string.IsNullOrEmpty(FallbackServerUrl))
        {
            list.Add(FallbackServerUrl.TrimEnd('/'));
        }
        return list;
    }

    public static string PlayerName
    {
        get => Pref(NamePref, "");
        set => SetPref(NamePref, value?.Trim());
    }

    /// <summary>Continente escolhido para salas ("" = automático, pelo servidor).</summary>
    public static string Region
    {
        get => Pref(RegionPref, "");
        set => SetPref(RegionPref, value ?? "");
    }

    public static bool ServerConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

    private static string Pref(string key, string fallback)
    {
        try
        {
            return PlayerPrefs.GetString(key, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    private static void SetPref(string key, string value)
    {
        try
        {
            PlayerPrefs.SetString(key, value ?? "");
            PlayerPrefs.Save();
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ estado da sala

    public State state { get; private set; }
    public int myId { get; private set; }
    public int hostId { get; private set; }
    public bool isHost => state == State.InRoom && myId == hostId;
    public string roomCode { get; private set; }
    public string roomName { get; private set; }
    public string roomColo { get; private set; }
    public string roomRegion { get; private set; }
    public JToken level { get; private set; }

    /// <summary>Modificadores da partida escolhidos pelo anfitrião (imortal, speed).</summary>
    public JObject mods { get; private set; } = new JObject();
    public bool modImmortal => mods.Value<bool?>("immortal") ?? false;
    public float modSpeed => Mathf.Clamp(mods.Value<float?>("speed") ?? 1f, 0.5f, 2f);
    public bool HasMods => modImmortal || Mathf.Abs(modSpeed - 1f) > 0.001f;
    /// <summary>Fila de fases (o anfitrião passa para a próxima quando a partida acaba).</summary>
    public JArray queue { get; private set; } = new JArray();
    public const int MaxQueue = 8;
    public bool inGame { get; private set; }
    /// <summary>Jogadores da partida em andamento (para quem entrou assistindo).</summary>
    public int[] roomOrder { get; private set; } = new int[0];
    public bool isSpectator => GetPlayer(myId)?.spectator ?? false;
    public readonly List<Player> players = new List<Player>();

    /// <summary>Continente de quem está jogando, segundo o servidor (SA, NA, EU...).</summary>
    public string detectedRegion { get; private set; }

    /// <summary>O servidor em uso guarda fases enviadas pelo anfitrião (servidor próprio; a Cloudflare não).</summary>
    public bool serverHasFiles { get; private set; }

    /// <summary>Ida e volta até o servidor, em ms (-1 = ainda não medido).</summary>
    public float pingMs { get; private set; } = -1f;

    public event Action OnRoomChanged;
    public event Action<string> OnError;
    public event Action OnLeft;
    public event Action<int, string> OnChat;

    /// <summary>Linha do chat da sala (mensagem de alguém ou aviso do jogo, como "entrou na sala").</summary>
    public class ChatLine
    {
        public string name;
        public string text;
        public bool system;
        public bool mine;
    }

    public const int ChatHistory = 60;

    /// <summary>Histórico do chat da sala (fica guardado durante a partida; some ao sair da sala).</summary>
    public readonly List<ChatLine> chatLog = new List<ChatLine>();

    /// <summary>Uma linha nova entrou no chat.</summary>
    public event Action<ChatLine> OnChatLine;

    private void AddChat(ChatLine line)
    {
        chatLog.Add(line);
        if (chatLog.Count > ChatHistory)
        {
            chatLog.RemoveAt(0);
        }
        OnChatLine?.Invoke(line);
    }
    /// <summary>Partida vai começar: (hora local em segundos de Time.realtimeSinceStartup, ordem dos jogadores).</summary>
    public event Action<double, int[]> OnStart;
    public event Action<int, JToken> OnGameEvent;
    public event Action<JToken> OnEnd;

    private ClientWebSocket socket;
    private CancellationTokenSource cancel;
    private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> closeReasons = new ConcurrentQueue<string>();
    private Task sendChain = Task.CompletedTask;
    private readonly object sendLock = new object();
    private float nextPing;

    // diferença entre o relógio do servidor (ms) e o local (ms desde o início do app), média dos melhores pings
    private double clockOffsetMs;
    private bool clockSynced;
    private float bestRtt = float.MaxValue;

    private static double LocalMs => Time.realtimeSinceStartupAsDouble * 1000.0;

    public double ServerNowMs => LocalMs + clockOffsetMs;

    public Player GetPlayer(int id) => players.Find(p => p.id == id);

    // ------------------------------------------------------------------ HTTP

    /// <summary>Acha um servidor que responda (o próprio primeiro, a Cloudflare de reserva) e lê as informações dele.</summary>
    public IEnumerator FetchInfo(Action<string> done)
    {
        string lastError = null;
        foreach (string url in CandidateServers())
        {
            string error = null;
            yield return FetchInfoFrom(url, e => error = e);
            if (error == null)
            {
                activeServer = url;
                if (url != CandidateServers()[0])
                {
                    Debug.LogWarning("[OnlineClient] servidor principal fora do ar; usando " + url);
                }
                done(null);
                yield break;
            }
            lastError = error;
        }
        done(lastError ?? "Nenhum servidor configurado.");
    }

    private IEnumerator FetchInfoFrom(string url, Action<string> done)
    {
        using (var req = UnityWebRequest.Get(url + "/"))
        {
            req.timeout = 6;
            yield return req.SendWebRequest();
            string error = null;
            if (req.result != UnityWebRequest.Result.Success)
            {
                error = req.error;
            }
            else
            {
                try
                {
                    var info = JObject.Parse(req.downloadHandler.text);
                    detectedRegion = info.Value<string>("region") ?? "";
                    serverHasFiles = info.Value<bool?>("files") ?? false;
                    if (info.Value<int?>("protocol") != Protocol)
                    {
                        error = "O servidor usa outra versão do online. Atualize o jogo.";
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }
            done(error);
        }
    }

    public IEnumerator ListRooms(Action<List<RoomInfo>, string> done)
    {
        using (var req = UnityWebRequest.Get(ServerUrl + "/rooms"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                done(null, req.error);
                yield break;
            }
            List<RoomInfo> rooms;
            try
            {
                rooms = JArray.Parse(req.downloadHandler.text).ToObject<List<RoomInfo>>();
            }
            catch (Exception e)
            {
                done(null, e.Message);
                yield break;
            }
            done(rooms, null);
        }
    }

    public IEnumerator CreateRoom(string name, bool isPublic, string password, Action<string, string> done)
    {
        var body = new JObject { ["name"] = name, ["public"] = isPublic, ["region"] = Region };
        if (!string.IsNullOrEmpty(password))
        {
            body["password"] = password; // o servidor guarda só o hash
        }
        using (var req = new UnityWebRequest(ServerUrl + "/rooms", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                done(null, req.error);
                yield break;
            }
            string code = null;
            try
            {
                code = JObject.Parse(req.downloadHandler.text).Value<string>("code");
            }
            catch (Exception e)
            {
                done(null, e.Message);
                yield break;
            }
            done(code, null);
        }
    }

    // ------------------------------------------------------------------ WebSocket

    public static string Sha256Hex(string text)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }

    public void Join(string code, string password = null)
    {
        Leave(notify: false);
        code = (code ?? "").Trim().ToUpperInvariant();
        string name = string.IsNullOrWhiteSpace(PlayerName) ? Loc.T("Jogador") : PlayerName;
        string wsUrl = ServerUrl.Replace("https://", "wss://").Replace("http://", "ws://") +
                       $"/rooms/{code}/ws?v={Protocol}&name={Uri.EscapeDataString(name)}" +
                       (string.IsNullOrEmpty(password) ? "" : "&pwh=" + Sha256Hex(code + ":" + password)); // a senha não vai em texto puro
        state = State.Connecting;
        roomCode = code;
        clockSynced = false;
        bestRtt = float.MaxValue;
        pingMs = -1f;
        socket = new ClientWebSocket();
        cancel = new CancellationTokenSource();
        sendChain = Task.CompletedTask;
        var ws = socket;
        var token = cancel.Token;
        Task.Run(async () =>
        {
            try
            {
                await ws.ConnectAsync(new Uri(wsUrl), token);
                await ReceiveLoop(ws, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                closeReasons.Enqueue("Não foi possível conectar à sala: " + e.Message);
            }
        });
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                closeReasons.Enqueue(ws.CloseStatusDescription ?? "");
                return;
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
            {
                inbox.Enqueue(sb.ToString());
                sb.Clear();
            }
        }
        closeReasons.Enqueue("");
    }

    private void Send(JObject msg)
    {
        var ws = socket;
        if (ws == null || cancel == null)
        {
            return;
        }
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None)));
        var token = cancel.Token;
        lock (sendLock)
        {
            // o ClientWebSocket não aceita dois envios ao mesmo tempo: encadeia
            sendChain = sendChain.ContinueWith(async _ =>
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token);
                    }
                    catch
                    {
                    }
                }
            }, token).Unwrap();
        }
    }

    public void Leave(bool notify = true)
    {
        bool wasConnected = state != State.Offline;
        var ws = socket;
        socket = null;
        if (cancel != null)
        {
            var c = cancel;
            cancel = null;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch
                    {
                    }
                    c.Cancel();
                    ws.Dispose();
                });
            }
            else
            {
                c.Cancel();
                ws?.Dispose();
            }
        }
        while (inbox.TryDequeue(out _))
        {
        }
        while (closeReasons.TryDequeue(out _))
        {
        }
        state = State.Offline;
        chatLog.Clear();
        players.Clear();
        level = null;
        mods = new JObject();
        inGame = false;
        myId = hostId = 0;
        if (wasConnected && notify)
        {
            OnLeft?.Invoke();
        }
    }

    public void SetReady(bool ready) => Send(new JObject { ["t"] = "ready", ["ready"] = ready });

    public void SetMods(bool immortal, float speed)
    {
        var m = new JObject();
        if (immortal)
        {
            m["immortal"] = true;
        }
        if (Mathf.Abs(speed - 1f) > 0.001f)
        {
            m["speed"] = Mathf.Round(speed * 100f) / 100f;
        }
        mods = m; // vale já para quem escolheu; o servidor repassa aos outros
        Send(new JObject { ["t"] = "mods", ["mods"] = m });
        OnRoomChanged?.Invoke();
    }

    public void SetQueue(JArray items)
    {
        var q = new JArray();
        foreach (var it in items)
        {
            if (q.Count < MaxQueue)
            {
                q.Add(it);
            }
        }
        queue = q; // vale já para o anfitrião; o servidor repassa aos outros
        Send(new JObject { ["t"] = "queue", ["queue"] = q });
        OnRoomChanged?.Invoke();
    }

    /// <summary>Acrescenta no fim da fila; false se ela já estiver cheia.</summary>
    public bool AddToQueue(JObject levelInfo)
    {
        if (queue.Count >= MaxQueue)
        {
            return false;
        }
        var q = new JArray(queue) { levelInfo };
        SetQueue(q);
        return true;
    }

    public void SetLevel(JObject levelInfo) => Send(new JObject { ["t"] = "level", ["level"] = levelInfo });

    public void StartGame() => Send(new JObject { ["t"] = "start" });

    public void EndGame(JToken results) => Send(new JObject { ["t"] = "end", ["results"] = results });

    public void SendGameEvent(JObject data) => Send(new JObject { ["t"] = "ev", ["d"] = data });

    public void SendChat(string text) => Send(new JObject { ["t"] = "chat", ["text"] = text });

    public void Kick(int id) => Send(new JObject { ["t"] = "kick", ["id"] = id });

    /// <summary>Anfitrião: remove e impede de voltar à sala (pelo IP, enquanto a sala existir).</summary>
    public void Ban(int id) => Send(new JObject { ["t"] = "ban", ["id"] = id });

    /// <summary>Denuncia um jogador: o servidor registra o nome e as últimas mensagens dele e avisa o anfitrião.</summary>
    public void Report(int id) => Send(new JObject { ["t"] = "report", ["id"] = id });

    // ------------------------------------------------------------------ mensagens

    private void Update()
    {
        while (inbox.TryDequeue(out string raw))
        {
            JObject msg;
            try
            {
                msg = JObject.Parse(raw);
            }
            catch
            {
                continue;
            }
            Handle(msg);
            if (state == State.Offline)
            {
                return; // Handle saiu da sala (erro)
            }
        }
        if (closeReasons.TryDequeue(out string reason) && state != State.Offline)
        {
            if (!string.IsNullOrEmpty(reason) && reason != "bye")
            {
                OnError?.Invoke(reason);
            }
            else if (state == State.InRoom)
            {
                OnError?.Invoke("Conexão com a sala perdida.");
            }
            Leave();
            return;
        }
        if (state == State.InRoom && Time.unscaledTime >= nextPing)
        {
            // pings rápidos no começo para acertar o relógio, depois a cada 5 s (mantém a sala viva)
            nextPing = Time.unscaledTime + (clockSynced ? 5f : 0.4f);
            Send(new JObject { ["t"] = "ping", ["c"] = LocalMs });
        }
    }

    private void Handle(JObject msg)
    {
        switch (msg.Value<string>("t"))
        {
            case "welcome":
            {
                state = State.InRoom;
                myId = msg.Value<int>("you");
                var room = msg["room"] as JObject;
                ReadRoom(room);
                double serverTime = msg.Value<double>("serverTime");
                clockOffsetMs = serverTime - LocalMs; // estimativa inicial, refinada pelos pings
                nextPing = 0f;
                OnRoomChanged?.Invoke();
                break;
            }
            case "join":
            {
                var p = msg["player"]?.ToObject<Player>();
                if (p != null && GetPlayer(p.id) == null)
                {
                    players.Add(p);
                    AddChat(new ChatLine { system = true, text = p.name + (p.spectator ? Loc.T(" entrou para assistir") : Loc.T(" entrou na sala")) });
                }
                OnRoomChanged?.Invoke();
                break;
            }
            case "leave":
            {
                var gone = GetPlayer(msg.Value<int>("id"));
                players.RemoveAll(p => p.id == msg.Value<int>("id"));
                if (gone != null)
                {
                    AddChat(new ChatLine { system = true, text = gone.name + Loc.T(" saiu da sala") });
                }
                OnRoomChanged?.Invoke();
                break;
            }
            case "host":
                hostId = msg.Value<int>("id");
                OnRoomChanged?.Invoke();
                break;
            case "ready":
            {
                var p = GetPlayer(msg.Value<int>("id"));
                if (p != null)
                {
                    p.ready = msg.Value<bool>("ready");
                }
                OnRoomChanged?.Invoke();
                break;
            }
            case "mods":
                mods = msg["mods"] as JObject ?? new JObject();
                OnRoomChanged?.Invoke();
                break;
            case "queue":
                queue = msg["queue"] as JArray ?? new JArray();
                OnRoomChanged?.Invoke();
                break;
            case "level":
                level = msg["level"];
                foreach (var p in players)
                {
                    p.ready = false;
                }
                OnRoomChanged?.Invoke();
                break;
            case "pong":
            {
                double sent = msg.Value<double>("c");
                double now = LocalMs;
                float rtt = (float)(now - sent);
                pingMs = pingMs < 0f ? rtt : Mathf.Lerp(pingMs, rtt, 0.3f);
                // a medição com o menor ida-e-volta é a mais confiável para o relógio
                if (rtt <= bestRtt * 1.2f)
                {
                    bestRtt = Mathf.Min(bestRtt, rtt);
                    clockOffsetMs = msg.Value<double>("s") + rtt / 2.0 - now;
                }
                if (bestRtt < float.MaxValue)
                {
                    clockSynced = true;
                }
                break;
            }
            case "start":
            {
                inGame = true;
                double atServer = msg.Value<double>("at");
                double atLocalSeconds = (atServer - clockOffsetMs) / 1000.0;
                int[] order = msg["order"]?.ToObject<int[]>() ?? Array.Empty<int>();
                OnRoomChanged?.Invoke();
                OnStart?.Invoke(atLocalSeconds, order);
                break;
            }
            case "end":
                inGame = false;
                roomOrder = new int[0];
                foreach (var p in players)
                {
                    p.ready = false;
                    p.spectator = false; // espectadores viram jogadores para a próxima partida
                }
                OnEnd?.Invoke(msg["results"]);
                AdvanceQueue();
                OnRoomChanged?.Invoke();
                break;
            case "ev":
                OnGameEvent?.Invoke(msg.Value<int>("from"), msg["d"]);
                break;
            case "chat":
            {
                int from = msg.Value<int>("from");
                string text = msg.Value<string>("text") ?? "";
                if (text.Length > 0)
                {
                    AddChat(new ChatLine { name = GetPlayer(from)?.name ?? Loc.T("Jogador"), text = text, mine = from == myId });
                }
                OnChat?.Invoke(from, text);
                break;
            }
            case "notice":
            {
                // avisos do servidor (denúncias): o texto é montado aqui, no idioma do jogador
                string noticeCode = msg.Value<string>("code");
                if (noticeCode == "report_sent")
                {
                    AddChat(new ChatLine { system = true, text = Loc.T("Denúncia enviada. Obrigado.") });
                }
                else if (noticeCode == "report_host")
                {
                    AddChat(new ChatLine { system = true, text = Loc.T("Um jogador denunciou ") + (msg.Value<string>("name") ?? "?") });
                }
                break;
            }
            case "error":
            {
                string code = msg.Value<string>("code");
                OnError?.Invoke(msg.Value<string>("message") ?? code);
                if (code == "version" || code == "full" || code == "ingame" || code == "kicked" || code == "banned" || code == "password")
                {
                    Leave();
                }
                break;
            }
        }
    }

    // fim da partida: o anfitrião coloca a próxima fase da fila como fase da sala
    private void AdvanceQueue()
    {
        if (!isHost || queue.Count == 0 || !(queue[0] is JObject next))
        {
            return;
        }
        var rest = new JArray();
        for (int i = 1; i < queue.Count; i++)
        {
            rest.Add(queue[i]);
        }
        SetLevel(next);
        SetQueue(rest);
    }

    private void ReadRoom(JObject room)
    {
        players.Clear();
        if (room == null)
        {
            return;
        }
        roomCode = room.Value<string>("code");
        roomName = room.Value<string>("name");
        roomColo = room.Value<string>("colo");
        roomRegion = room.Value<string>("region");
        hostId = room.Value<int>("host");
        level = room["level"];
        mods = room["mods"] as JObject ?? new JObject();
        queue = room["queue"] as JArray ?? new JArray();
        if (level != null && level.Type == JTokenType.Null)
        {
            level = null;
        }
        inGame = room.Value<bool>("inGame");
        roomOrder = room["order"]?.ToObject<int[]>() ?? new int[0];
        foreach (var p in room["players"] as JArray ?? new JArray())
        {
            players.Add(p.ToObject<Player>());
        }
    }

    private void OnApplicationQuit()
    {
        Leave(notify: false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            Leave(notify: false);
            instance = null;
        }
    }
}
