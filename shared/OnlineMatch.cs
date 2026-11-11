// Port: partida online (fase 2). Cuida de:
//   - Fase da sala: cada jogador acha a fase escolhida pelo anfitrião nas próprias fases (pelo hash do jogo,
//     MD5 de autor+artista+título) ou baixa do link (ADOFAI.gg/TUF) e instala em Worlds. Só dá para marcar
//     "pronto" com a fase instalada.
//   - Início sincronizado: no "start" do servidor todos carregam a fase; quando cada um chega na tela de
//     "pressione para começar" avisa "carregado". O anfitrião espera todos (ou um tempo-limite) e marca a hora
//     de início no relógio do servidor; cada aparelho começa a fase nessa hora em vez de esperar um toque.
//   - Fim: ao voltar para o CLS a partida acaba; o anfitrião avisa o servidor para liberar a sala.
// Os jogadores ainda jogam cada um na sua tela; ver os outros na pista é a fase 3.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class OnlineMatch : MonoBehaviour
{
    public enum LevelState
    {
        None,
        Checking,
        Downloading,
        Ready,
        Missing,
        Failed,
    }

    private const float LoadTimeoutSeconds = 30f; // quem demorar mais que isso para carregar fica para trás
    private const double GoDelayMs = 1500.0;
    private const string InstalledPref = "online.level.";

    private static OnlineMatch instance;

    public static OnlineMatch Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("OnlineMatch");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<OnlineMatch>();
            }
            return instance;
        }
    }

    /// <summary>Há uma partida online em andamento (da carga da fase até voltar ao CLS).</summary>
    public static bool Active => instance != null && instance.active;

    /// <summary>O jogador local está só assistindo (entrou no meio da partida).</summary>
    public static bool SpectatingFromStart => instance != null && instance.active && instance.spectateOnly;

    private bool spectateOnly;
    private bool spectateDeclined; // espectador saiu de propósito: não volta a assistir esta partida

    public LevelState levelState { get; private set; }
    public float downloadProgress { get; private set; }
    public string levelError { get; private set; }
    public string levelPath { get; private set; }

    public event Action OnLevelStateChanged;

    /// <summary>Tentativa atual de um jogador remoto: onde a música dele estava numa hora do servidor.</summary>
    public class RemoteRun
    {
        public int run = -1;          // número da tentativa (muda a cada vez que ele começa/recomeça)
        public double serverMs;       // hora do servidor da última sincronização
        public double songPos;        // posição na música dele nessa hora
        public float rate = 1f;       // velocidade da música (pitch)
        public int deadFloor = -1;    // caiu neste bloco (até recomeçar)
        public int startFloor;        // bloco de partida da tentativa (0 ou checkpoint)
        public int syncFloor = -1;    // bloco atual segundo o último "run" (para quem chegou no meio)
        // placar (valem para a partida toda, não só para a tentativa atual)
        public int deaths;
        public int diedRun = -1;      // tentativa em que a última queda foi contada (evita contar duas vezes)
        public float progress;        // maior fração da fase alcançada (0-1)
        public bool finishedEver;
        public bool quit;             // saiu da fase (voltou para a sala) sem terminar
        public float lastSeen = -1f;  // último aviso dele (a cada ~3 s enquanto joga)
        public double syncTime;       // posição da música em que ele chegou nesse bloco
        // toques da tentativa: bloco alcançado e a posição da música em que isso aconteceu (em ordem)
        public readonly List<int> hitFloors = new List<int>();
        public readonly List<double> hitTimes = new List<double>();
        public bool finished;
        public float accuracy = -1f;

        /// <summary>Posição atual na música do jogador remoto, pelo relógio do servidor.</summary>
        public double SongPosAt(double serverNowMs) => songPos + (serverNowMs - serverMs) / 1000.0 * rate;
    }

    public readonly Dictionary<int, RemoteRun> remoteRuns = new Dictionary<int, RemoteRun>();

    // ---- placar e fim da partida
    // A partida acaba quando todos os jogadores terminam, 90 s depois do primeiro terminar, ou quando o anfitrião
    // encerra. Quem decide é o aparelho do anfitrião (recebe tudo); ele manda o placar no "end".
    private const double FinishWindowMs = 90000.0;
    private const float QuitAfterEndSeconds = 2.5f;

    // números do jogador local nesta partida (o OnlineGhosts atualiza)
    public int localDeaths;
    public float localProgress;
    public bool localFinished;
    public float localAccuracy = -1f;

    /// <summary>Hora do servidor em que a partida acaba (0 = sem prazo ainda).</summary>
    public double deadlineServerMs { get; private set; }

    /// <summary>Placar da última partida (lista ordenada) e se o jogador ainda não viu.</summary>
    public JArray lastResults { get; private set; }
    public bool resultsUnseen;

    private bool ending;
    private float quitAt = -1f;
    private Text timerText;
    public int[] StartOrder => startOrder;
    private int localRun;

    /// <summary>Número da próxima tentativa do jogador local.</summary>
    public int NextLocalRun() => ++localRun;

    public RemoteRun GetRun(int id)
    {
        if (!remoteRuns.TryGetValue(id, out var r))
        {
            r = new RemoteRun();
            remoteRuns[id] = r;
        }
        return r;
    }

    private OnlineClient client => OnlineClient.Instance;
    private string levelKey; // JSON da fase resolvida/em resolução
    private Coroutine resolving;

    private bool active;
    private bool reportedLoaded;
    private float failedSince = -1f;
    private const float AutoRestartSeconds = 1.5f;
    private int[] startOrder = new int[0];
    private readonly HashSet<int> loadedPlayers = new HashSet<int>();
    private double goAtServerMs;
    private float startedAt;

    private GameObject overlay;      // canvas (fica ativo)
    private GameObject overlayPill;  // aviso no rodapé (aparece e some)
    private Text overlayText;

    private void Awake()
    {
        var c = OnlineClient.Instance;
        c.OnRoomChanged += OnRoomChanged;
        c.OnStart += OnStart;
        c.OnGameEvent += OnGameEvent;
        c.OnLeft += OnLeft;
        c.OnEnd += OnMatchEnded;
    }

    private void OnDestroy()
    {
        if (OnlineClient.Exists)
        {
            var c = OnlineClient.Instance;
            c.OnRoomChanged -= OnRoomChanged;
            c.OnStart -= OnStart;
            c.OnGameEvent -= OnGameEvent;
            c.OnLeft -= OnLeft;
            c.OnEnd -= OnMatchEnded;
        }
        if (instance == this)
        {
            instance = null;
        }
    }

    // ------------------------------------------------------------------ fase da sala

    private void OnRoomChanged()
    {
        if (!client.inGame)
        {
            spectateDeclined = false;
        }
        if (active && goAtServerMs <= 0 && reportedLoaded)
        {
            // alguém saiu durante o carregamento ou o anfitrião mudou: a largada pode não precisar mais esperar
            TryGo();
        }
        string key = client.level?.ToString(Newtonsoft.Json.Formatting.None);
        if (key == levelKey)
        {
            if (levelState == LevelState.Ready)
            {
                TrySpectateJoin();
            }
            return;
        }
        levelKey = key;
        if (resolving != null)
        {
            StopCoroutine(resolving);
            resolving = null;
        }
        levelPath = null;
        levelError = null;
        downloadProgress = 0f;
        if (client.level is JObject level)
        {
            resolving = StartCoroutine(Resolve(level));
        }
        else
        {
            SetState(LevelState.None);
        }
    }

    /// <summary>Reavalia a fase (por exemplo, depois de instalar uma fase no CLS).</summary>
    public void Recheck()
    {
        levelKey = null;
        OnRoomChanged();
    }

    private void SetState(LevelState state)
    {
        levelState = state;
        OnLevelStateChanged?.Invoke();
    }

    private IEnumerator Resolve(JObject level)
    {
        SetState(LevelState.Checking);
        string source = level.Value<string>("source");
        string hash = level.Value<string>("hash");

        // as fases instaladas (inclusive as da oficina) só entram na lista depois que o jogo lê as pastas
        if (scnCLS.instance != null)
        {
            yield return scnCLS.instance.EnsureLevelsIndexed();
        }

        // 1) já tenho a fase (mesmo hash nas minhas fases do CLS)?
        string path = !string.IsNullOrEmpty(hash) && scnCLS.instance != null ? scnCLS.instance.FindLevelFileByHash(hash) : null;

        // 2) baixada antes pelo online?
        string key = level.Value<string>("key");
        if (path == null && !string.IsNullOrEmpty(key))
        {
            path = InstalledPath(key);
        }

        // 3) já tem a fase por outro caminho (navegador de fases, importador, outro site)? mesmo título e autor
        if (path == null && scnCLS.instance != null)
        {
            path = scnCLS.instance.FindLevelFileByMeta(level.Value<string>("title"), level.Value<string>("artist"), level.Value<string>("author"));
            if (path != null && !string.IsNullOrEmpty(key))
            {
                // lembra para as próximas vezes
                try
                {
                    PlayerPrefs.SetString(InstalledPref + key, System.IO.Path.GetDirectoryName(path));
                    PlayerPrefs.Save();
                }
                catch
                {
                }
            }
        }

        if (path == null && (source == "url" || !string.IsNullOrEmpty(level.Value<string>("url"))))
        {
            string url = level.Value<string>("url");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                levelError = "A fase não tem link de download.";
                SetState(LevelState.Failed);
                yield break;
            }
            SetState(LevelState.Downloading);
            yield return Download(url, key, level.Value<string>("title"), p => path = p);
            if (path == null)
            {
                SetState(LevelState.Failed);
                yield break;
            }
        }

        if (path == null)
        {
            levelError = "Você não tem esta fase, e ela não foi encontrada no ADOFAI.gg/TUF para baixar.";
            SetState(LevelState.Missing);
            yield break;
        }
        levelPath = path;
        SetState(LevelState.Ready);
        resolving = null;
        TrySpectateJoin();
    }

    private const long MaxShareBytes = 150L * 1024 * 1024;

    /// <summary>
    /// Anfitrião: a fase não está no ADOFAI.gg/TUF, então envia a pasta dela (zip) para o servidor da sala, sem
    /// botão: quem não tiver a fase baixa dali. Devolve o link e a chave, ou null se não deu (então só quem já tem joga).
    /// </summary>
    public IEnumerator ShareLevel(string hash, Action<string> status, Action<string, string> done)
    {
        if (!client.serverHasFiles || string.IsNullOrEmpty(client.roomCode) || scnCLS.instance == null)
        {
            done(null, null);
            yield break;
        }
        string file = scnCLS.instance.FindLevelFileByHash(hash);
        string dir = string.IsNullOrEmpty(file) ? null : Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || Path.GetFullPath(dir).TrimEnd('/', '\\') == Path.GetFullPath(scnCLS.localWorldsPath).TrimEnd('/', '\\'))
        {
            done(null, null); // fase solta na pasta de mundos: zipar tudo seria o conjunto de todas as fases
            yield break;
        }
        long total = 0;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
            }
        }
        catch
        {
            done(null, null);
            yield break;
        }
        if (total > MaxShareBytes)
        {
            status("Fase grande demais para enviar à sala (" + total / (1024 * 1024) + " MB).");
            done(null, null);
            yield break;
        }
        string key = "s" + System.Text.RegularExpressions.Regex.Replace(hash ?? "", "[^A-Za-z0-9]", "");
        if (key.Length < 5)
        {
            key = "s" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }
        string url = OnlineClient.ServerUrl + "/files/" + key;

        // já enviada antes (e ainda no servidor)?
        using (var head = UnityWebRequest.Head(url))
        {
            head.timeout = 8;
            yield return head.SendWebRequest();
            if (head.result == UnityWebRequest.Result.Success)
            {
                done(url, key);
                yield break;
            }
        }

        status("Preparando a fase para enviar à sala…");
        string zip = Path.Combine(Application.temporaryCachePath, "share-" + key + ".zip");
        string zipError = null;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (File.Exists(zip))
                {
                    File.Delete(zip);
                }
                using (var stream = new FileStream(zip, FileMode.Create))
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
                {
                    string baseDir = Path.GetFullPath(dir).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
                    foreach (string path in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        string entry = Path.GetFullPath(path).Substring(baseDir.Length).Replace('\\', '/');
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, path, entry, System.IO.Compression.CompressionLevel.Fastest);
                    }
                }
            }
            catch (Exception e)
            {
                zipError = e.Message;
            }
        });
        while (!task.IsCompleted)
        {
            yield return null;
        }
        if (zipError != null)
        {
            Debug.LogWarning("[OnlineMatch] não deu para compactar a fase: " + zipError);
            done(null, null);
            yield break;
        }

        bool ok = false;
        using (var put = new UnityWebRequest(url, "PUT"))
        {
            put.uploadHandler = new UploadHandlerFile(zip);
            put.downloadHandler = new DownloadHandlerBuffer();
            put.SetRequestHeader("x-room", client.roomCode);
            var op = put.SendWebRequest();
            int lastPct = -10;
            while (!op.isDone)
            {
                int pct = Mathf.RoundToInt(put.uploadProgress * 100f);
                if (pct >= lastPct + 10)
                {
                    lastPct = pct;
                    status("Enviando a fase para a sala… " + pct + "%");
                }
                yield return null;
            }
            ok = put.result == UnityWebRequest.Result.Success;
            if (!ok)
            {
                Debug.LogWarning("[OnlineMatch] envio da fase falhou: " + put.error + " " + put.downloadHandler.text);
            }
        }
        try
        {
            File.Delete(zip);
        }
        catch
        {
        }
        done(ok ? url : null, ok ? key : null);
    }

    private static string InstalledPath(string key)
    {
        string dir;
        try
        {
            dir = PlayerPrefs.GetString(InstalledPref + key, "");
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return null;
        }
        var found = AdoPackageInstaller.FindLevelFile(dir);
        return found.Value;
    }

    private IEnumerator Download(string url, string key, string title, Action<string> done)
    {
        string tempDir = Path.Combine(scnCLS.tempLevelsFolder, "online");
        Directory.CreateDirectory(tempDir);
        string zip = Path.Combine(tempDir, key + ".zip");
        if (File.Exists(zip))
        {
            File.Delete(zip);
        }
        using (var req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                downloadProgress = Mathf.Clamp01(req.downloadProgress);
                OnLevelStateChanged?.Invoke();
                yield return null;
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                levelError = "Não deu para baixar a fase: " + req.error;
                done(null);
                yield break;
            }
        }
        string target = null;
        try
        {
            if (!AdoPackageInstaller.CheckFileIsZip(zip).Value)
            {
                levelError = "O link da fase não tem mais o arquivo.";
                done(null);
                yield break;
            }
            string worlds = scnCLS.localWorldsPath;
            Directory.CreateDirectory(worlds);
            string safe = Regex.Replace(PortUi.StripTags(title ?? ""), "[\\\\/:*?\"<>|\\p{C}]", "_").Trim(' ', '.');
            if (safe.Length > 50)
            {
                safe = safe.Substring(0, 50).Trim();
            }
            target = Path.Combine(worlds, (safe.Length > 0 ? safe + " " : "") + "[online " + key + "]");
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            ZipUtils.Unzip(zip, target);
            File.Delete(zip);
        }
        catch (Exception e)
        {
            levelError = "Não deu para instalar a fase: " + e.Message;
            done(null);
            yield break;
        }
        var found = AdoPackageInstaller.FindLevelFile(target);
        if (string.IsNullOrEmpty(found.Value))
        {
            levelError = "O arquivo baixado não tem uma fase (.adofai).";
            done(null);
            yield break;
        }
        try
        {
            PlayerPrefs.SetString(InstalledPref + key, target);
            PlayerPrefs.Save();
        }
        catch
        {
        }
        done(found.Value);
    }

    // ------------------------------------------------------------------ partida

    /// <summary>Quem terminou a fase passa a assistir: a próxima largada é de espectador.</summary>
    public void EnterSpectateMode()
    {
        spectateOnly = true;
        reportedLoaded = true;
    }

    // entrou numa sala com a partida em andamento: carrega a fase só para assistir
    private void TrySpectateJoin()
    {
        if (active || spectateDeclined || !client.inGame || !client.isSpectator || levelState != LevelState.Ready)
        {
            return;
        }
        active = true;
        spectateOnly = true;
        reportedLoaded = true; // espectador não participa da largada
        startOrder = client.roomOrder;
        remoteRuns.Clear();
        localRun = 0;
        OnlineScreen.Close();
        ShowOverlay("Carregando a partida para assistir…");
        ApplyMods();
        GCS.practiceMode = false;
        GCS.checkpointNum = 0;
        ADOBase.controller.LoadCustomLevel(levelPath);
    }

    // modificadores do anfitrião valem para a partida toda: imortal (continua a pista após errar) e velocidade da fase
    private void ApplyMods()
    {
        GCS.useNoFail = client.modImmortal;
        float speed = client.modSpeed;
        bool fast = Mathf.Abs(speed - 1f) > 0.001f;
        GCS.speedTrialMode = fast;
        GCS.nextSpeedRun = speed;
        GCS.currentSpeedTrial = speed;
    }

    private static void ClearMods()
    {
        GCS.useNoFail = false;
        GCS.speedTrialMode = false;
        GCS.nextSpeedRun = 1f;
        GCS.currentSpeedTrial = 1f;
    }

    private void OnStart(double atLocalSeconds, int[] order)
    {
        if (levelState != LevelState.Ready || string.IsNullOrEmpty(levelPath))
        {
            // não deveria acontecer (o servidor só começa com todos prontos), mas não dá para jogar sem a fase
            Debug.LogWarning("[OnlineMatch] start sem a fase pronta");
            return;
        }
        active = true;
        spectateOnly = false;
        reportedLoaded = false;
        startOrder = order ?? new int[0];
        remoteRuns.Clear();
        localRun = 0;
        ResetScore();
        loadedPlayers.Clear();
        goAtServerMs = 0;
        startedAt = Time.unscaledTime;
        OnlineScreen.Close();
        ShowOverlay("Carregando a fase…");
        ApplyMods();
        GCS.practiceMode = false;
        GCS.checkpointNum = 0;
        ADOBase.controller.LoadCustomLevel(levelPath);
    }

    /// <summary>Chamado pelo scrController enquanto espera o toque para começar a fase.</summary>
    public static bool ShouldStartNow()
    {
        var m = instance;
        if (m == null || !m.active)
        {
            return false;
        }
        if (m.spectateOnly)
        {
            // assistindo: a fase começa (em piloto automático, invisível) no ponto do jogador assistido
            m.HideOverlay();
            if (!OnlineGhosts.Exists)
            {
                OnlineGhosts.Begin();
            }
            OnlineSpectate.Begin();
            return OnlineSpectate.ReadyToStart();
        }
        if (!m.reportedLoaded)
        {
            m.reportedLoaded = true;
            m.MarkLoaded(m.client.myId);
            m.client.SendGameEvent(new JObject { ["k"] = "loaded" });
            m.ShowOverlay("Esperando os outros jogadores…");
        }
        if (m.goAtServerMs <= 0)
        {
            return false;
        }
        double remaining = m.goAtServerMs - m.client.ServerNowMs;
        if (remaining > 0)
        {
            m.ShowOverlay("Começando…");
            return false;
        }
        m.HideOverlay();
        m.failedSince = -1f;
        // a cada tentativa (a cena recarrega quando o jogador recomeça) os fantasmas são recriados
        OnlineGhosts.Begin();
        return true;
    }

    /// <summary>
    /// Chamado pelo scrController na tela de queda: no online o jogador recomeça sozinho (do início ou do
    /// checkpoint) depois de um instante, para continuar a corrida com os outros.
    /// </summary>
    public static bool ShouldAutoRestart()
    {
        var m = instance;
        if (m == null || !m.active)
        {
            return false;
        }
        if (m.failedSince < 0f)
        {
            m.failedSince = Time.unscaledTime;
        }
        if (Time.unscaledTime - m.failedSince < AutoRestartSeconds)
        {
            return false;
        }
        m.failedSince = -1f; // dispara uma vez só
        return true;
    }

    private void OnGameEvent(int from, JToken d)
    {
        if (d == null || (!active && !client.inGame))
        {
            return;
        }
        string kind = d.Value<string>("k");
        if (kind == "run" || kind == "h" || kind == "die" || kind == "win" || kind == "loaded")
        {
            var seen = GetRun(from);
            seen.lastSeen = Time.unscaledTime;
            seen.quit = false;
        }
        switch (kind)
        {
            case "quit":
                GetRun(from).quit = true;
                break;
            case "loaded":
                if (active)
                {
                    MarkLoaded(from);
                }
                break;
            case "go":
                goAtServerMs = d.Value<double>("at");
                break;
            case "deadline":
                deadlineServerMs = d.Value<double>("at");
                break;
            case "run":
            {
                var r = GetRun(from);
                int n = d.Value<int?>("n") ?? 0;
                if (n != r.run)
                {
                    // nova tentativa: volta a correr
                    r.run = n;
                    r.deadFloor = -1;
                    r.finished = false;
                    r.hitFloors.Clear();
                    r.hitTimes.Clear();
                }
                r.startFloor = d.Value<int?>("f0") ?? 0;
                r.progress = Mathf.Max(r.progress, d.Value<float?>("pr") ?? 0f);
                r.syncFloor = d.Value<int?>("cf") ?? -1;
                r.syncTime = d.Value<double?>("ct") ?? 0.0;
                r.serverMs = d.Value<double>("s");
                r.songPos = d.Value<double>("p");
                r.rate = d.Value<float?>("r") ?? 1f;
                break;
            }
            case "h":
            {
                // toques do jogador remoto: [[bloco, posição da música], ...]
                var r = GetRun(from);
                if ((d.Value<int?>("n") ?? r.run) != r.run || !(d["h"] is JArray hits))
                {
                    break;
                }
                foreach (var h in hits)
                {
                    if (h is JArray pair && pair.Count >= 2)
                    {
                        r.hitFloors.Add(pair[0].Value<int>());
                        r.hitTimes.Add(pair[1].Value<double>());
                    }
                }
                break;
            }
            case "die":
            {
                var r = GetRun(from);
                int n = d.Value<int?>("n") ?? r.run;
                if (n == r.run)
                {
                    r.deadFloor = d.Value<int>("f");
                }
                if (n != r.diedRun)
                {
                    r.diedRun = n;
                    r.deaths++;
                }
                r.progress = Mathf.Max(r.progress, d.Value<float?>("pr") ?? 0f);
                break;
            }
            case "win":
            {
                var r = GetRun(from);
                r.finished = true;
                r.finishedEver = true;
                r.progress = 1f;
                r.accuracy = d.Value<float?>("acc") ?? -1f;
                break;
            }
        }
    }

    private void MarkLoaded(int id)
    {
        loadedPlayers.Add(id);
        TryGo();
    }

    private void Update()
    {
        if (active && client.isHost && goAtServerMs <= 0 && reportedLoaded &&
            Time.unscaledTime - startedAt > LoadTimeoutSeconds)
        {
            SendGo();
        }
        if (client.isHost && client.inGame && !ending && startOrder.Length > 0)
        {
            CheckMatchEnd();
        }
        UpdateTimer();
        if (quitAt > 0f && Time.unscaledTime >= quitAt)
        {
            quitAt = -1f;
            HideOverlay();
            if (ADOBase.isScnGame && ADOBase.controller != null)
            {
                ADOBase.controller.QuitToMainMenu(); // fase personalizada: volta ao CLS, onde a sala reabre
            }
        }
    }

    private void ResetScore()
    {
        localDeaths = 0;
        localProgress = 0f;
        localFinished = false;
        localAccuracy = -1f;
        deadlineServerMs = 0;
        ending = false;
    }

    private const float SilentSeconds = 15f; // sem avisos por esse tempo: saiu do jogo ou perdeu a conexão
    private const float NeverSeenSeconds = 45f;

    private bool HasFinished(int id) => id == client.myId ? localFinished : GetRun(id).finishedEver;

    // já não está mais jogando: terminou, saiu da fase, ou parou de dar sinal
    private bool IsDone(int id)
    {
        if (id == client.myId)
        {
            return localFinished || !active || spectateOnly;
        }
        var r = GetRun(id);
        if (r.finishedEver || r.quit)
        {
            return true;
        }
        // nunca deu sinal (ainda carregando a fase, que pode demorar): só depois de bem mais tempo
        return r.lastSeen >= 0f
            ? Time.unscaledTime - r.lastSeen > SilentSeconds
            : Time.unscaledTime - startedAt > NeverSeenSeconds;
    }

    private bool HasQuit(int id) => id == client.myId ? !localFinished && !active : !GetRun(id).finishedEver && GetRun(id).quit;

    // só o anfitrião: todos terminaram? acabou o prazo? (o prazo começa quando o primeiro termina)
    private void CheckMatchEnd()
    {
        var racers = new List<int>();
        foreach (int id in startOrder)
        {
            if (client.GetPlayer(id) != null)
            {
                racers.Add(id);
            }
        }
        if (racers.Count == 0)
        {
            EndMatch();
            return;
        }
        // acaba quando ninguém mais está jogando (terminou, saiu da fase ou sumiu); o prazo de 90 s começa
        // quando alguém termina de verdade
        bool all = true, any = false;
        foreach (int id in racers)
        {
            all &= IsDone(id);
            any |= HasFinished(id);
        }
        if (all)
        {
            EndMatch();
            return;
        }
        if (any && deadlineServerMs <= 0)
        {
            deadlineServerMs = client.ServerNowMs + FinishWindowMs;
            client.SendGameEvent(new JObject { ["k"] = "deadline", ["at"] = deadlineServerMs });
        }
        if (deadlineServerMs > 0 && client.ServerNowMs >= deadlineServerMs)
        {
            EndMatch();
        }
    }

    /// <summary>Anfitrião: encerra a partida agora e manda o placar.</summary>
    public void EndMatch()
    {
        if (!client.isHost || !client.inGame || ending)
        {
            return;
        }
        ending = true;
        client.EndGame(BuildResults());
    }

    private JArray BuildResults()
    {
        var rows = new List<JObject>();
        foreach (int id in startOrder)
        {
            var p = client.GetPlayer(id);
            bool me = id == client.myId;
            var r = me ? null : GetRun(id);
            rows.Add(new JObject
            {
                ["id"] = id,
                ["name"] = p?.name ?? Loc.T("Jogador"),
                ["left"] = p == null,
                ["finished"] = me ? localFinished : r.finishedEver,
                ["quit"] = HasQuit(id),
                ["acc"] = me ? localAccuracy : r.accuracy,
                ["deaths"] = me ? localDeaths : r.deaths,
                ["progress"] = me ? (localFinished ? 1f : localProgress) : (r.finishedEver ? 1f : r.progress),
            });
        }
        // quem terminou na frente; entre eles, menos quedas e depois maior precisão; os outros, por progresso
        rows.Sort((a, b) =>
        {
            bool fa = a.Value<bool>("finished"), fb = b.Value<bool>("finished");
            if (fa != fb)
            {
                return fa ? -1 : 1;
            }
            if (fa)
            {
                int c = a.Value<int>("deaths").CompareTo(b.Value<int>("deaths"));
                return c != 0 ? c : b.Value<float>("acc").CompareTo(a.Value<float>("acc"));
            }
            int cp = b.Value<float>("progress").CompareTo(a.Value<float>("progress"));
            return cp != 0 ? cp : a.Value<int>("deaths").CompareTo(b.Value<int>("deaths"));
        });
        return new JArray(rows);
    }

    private void OnMatchEnded(JToken results)
    {
        deadlineServerMs = 0;
        ending = false;
        if (results is JArray list && list.Count > 0)
        {
            lastResults = list;
            resultsUnseen = true;
        }
        if (active)
        {
            // quem ainda está na fase volta para a sala, onde vê o placar
            ShowOverlay("Partida encerrada!");
            quitAt = Time.unscaledTime + QuitAfterEndSeconds;
        }
    }

    // contagem regressiva no topo da tela depois que o primeiro termina
    private void UpdateTimer()
    {
        bool show = active && deadlineServerMs > 0 && client.inGame;
        if (!show)
        {
            if (timerText != null && timerText.gameObject.activeSelf)
            {
                timerText.gameObject.SetActive(false);
            }
            return;
        }
        if (timerText == null)
        {
            ShowOverlay("");
            HideOverlay();
            timerText = PortUi.MakeText(overlay.transform, "", 34, Color.white);
            timerText.alignment = TextAnchor.MiddleCenter;
            PortUi.Place(timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1000f, 70f));
            timerText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        }
        overlay.SetActive(true);
        double left = System.Math.Max(0.0, (deadlineServerMs - client.ServerNowMs) / 1000.0);
        string text = $"Alguém terminou! A partida acaba em {(int)left / 60}:{(int)left % 60:00}";
        if (timerText.text != Loc.T(text))
        {
            PortUi.SetText(timerText, text);
        }
        timerText.gameObject.SetActive(true);
    }

    private void TryGo()
    {
        if (!client.isHost || goAtServerMs > 0)
        {
            return;
        }
        // só quem está na corrida (ordem do início) e ainda na sala: espectadores (quem entrou no momento em que a
        // partida começou) não participam da largada e nunca avisam "carregado"
        foreach (int id in startOrder)
        {
            if (client.GetPlayer(id) != null && !loadedPlayers.Contains(id))
            {
                return;
            }
        }
        SendGo();
    }

    private void SendGo()
    {
        goAtServerMs = client.ServerNowMs + GoDelayMs;
        client.SendGameEvent(new JObject { ["k"] = "go", ["at"] = goAtServerMs });
    }

    /// <summary>Chamado quando o CLS abre: a partida (se havia) acabou.</summary>
    public static void OnReturnedToCLS()
    {
        var m = instance;
        if (m == null || !m.active)
        {
            return;
        }
        if (m.spectateOnly && m.client.isSpectator && m.client.inGame)
        {
            m.spectateDeclined = true;
        }
        m.active = false;
        m.spectateOnly = false;
        m.quitAt = -1f;
        OnlineSpectate.Stop();
        ClearMods();
        RDC.auto = false;
        GCS.checkpointNum = 0;
        m.HideOverlay();
        // voltar ao CLS não encerra a partida dos outros, mas avisa que este jogador saiu da corrida
        if (m.client.inGame && !m.localFinished)
        {
            m.client.SendGameEvent(new JObject { ["k"] = "quit" });
        }
    }

    private void OnLeft()
    {
        active = false;
        HideOverlay();
        levelKey = null;
        SetState(LevelState.None);
    }

    // ------------------------------------------------------------------ aviso na tela durante a carga

    private void ShowOverlay(string text)
    {
        if (overlay == null)
        {
            Transform canvas = PortUi.MakeCanvas(transform, "OnlineOverlay", 420);
            var pill = PortUi.MakePanel(canvas, "Pill", new Color(0.08f, 0.07f, 0.12f, 0.92f)).rectTransform;
            pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 0.2f);
            var hlg = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(44, 44, 16, 16);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            overlayText = PortUi.MakeText(pill, "", 34, Color.white);
            overlayText.alignment = TextAnchor.MiddleCenter;
            overlay = canvas.gameObject;
            overlayPill = pill.gameObject;
        }
        if (overlayText.text != Loc.T(text))
        {
            PortUi.SetText(overlayText, text);
        }
        overlay.SetActive(true);
        overlayPill.SetActive(true);
    }

    private void HideOverlay()
    {
        if (overlayPill != null)
        {
            overlayPill.SetActive(false);
        }
    }
}
