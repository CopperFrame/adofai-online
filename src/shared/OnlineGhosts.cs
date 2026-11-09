// Port (online, fase 3): os outros jogadores aparecem na pista como "fantasmas", numa corrida.
//
// Cada fantasma segue o tempo da música do próprio dono. Quando alguém começa ou recomeça (do início ou de um
// checkpoint), avisa os outros com "run": a hora do servidor e a posição na música naquele instante (e reenvia a
// cada poucos segundos para corrigir pequenas diferenças). Com o relógio do servidor, cada aparelho sabe onde a
// música do outro está. Quem cai e recomeça fica para trás (e pode ser ultrapassado).
//
// Os movimentos são os reais: cada troca de bloco do jogador vai pela rede ("h") com a posição da música em que
// aconteceu, e o fantasma repete esses toques com um pequeno atraso (metade do ping + folga, ~0,2 s), para que
// os toques já tenham chegado quando forem desenhados. Como no jogo, o ângulo do planeta que gira depende só do
// tempo da música; o toque decide quando ele troca de bloco: se o jogador atrasa, o planeta passa do ponto.
//
// Pela rede chegam também "die" (caiu no bloco N: o fantasma para e apaga até a próxima tentativa) e "win".
// O estado de cada jogador remoto fica no OnlineMatch, que sobrevive às recargas de cena; esta classe só desenha
// e avisa as mudanças do jogador local. Cada largada (Begin) é uma tentativa nova do jogador local.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public class OnlineGhosts : MonoBehaviour
{
    private const float ResyncSeconds = 3f;
    private const float FlushSeconds = 0.05f;

    private class Ghost
    {
        public int id;
        public string name;
        public SpriteRenderer planetA;
        public SpriteRenderer planetB;
        public Text label;
        public Vector3 focus;  // bloco em que ele está (para a câmera do espectador)
        public ParticleSystem tailA;
        public ParticleSystem tailB;
        public Vector3 lastA;
        public Vector3 lastB;
        public bool racing;    // ainda correndo (não caiu nem terminou)
        public bool placed;    // já posicionado (a primeira vez vai direto; depois o desenho segue suavemente)
    }

    /// <summary>Jogadores que ainda estão na pista (para o espectador escolher quem assistir).</summary>
    public static List<int> RacingIds()
    {
        var ids = new List<int>();
        if (instance != null)
        {
            foreach (var g in instance.ghosts)
            {
                if (g.racing && OnlineClient.Instance.GetPlayer(g.id) != null)
                {
                    ids.Add(g.id);
                }
            }
        }
        return ids;
    }

    /// <summary>Onde está o fantasma deste jogador (bloco atual).</summary>
    public static bool TryGetFocus(int id, out Vector3 position)
    {
        position = Vector3.zero;
        if (instance == null)
        {
            return false;
        }
        var g = instance.ghosts.Find(x => x.id == id);
        if (g == null)
        {
            return false;
        }
        position = g.focus;
        return true;
    }

    /// <summary>Posição da música do jogador remoto agora (no relógio do servidor), ou null se ainda não se sabe.</summary>
    public static double? SongPosOf(int id)
    {
        var run = OnlineMatch.Instance.GetRun(id);
        if (run.run < 0)
        {
            return null;
        }
        return run.SongPosAt(OnlineClient.Instance.ServerNowMs);
    }

    private static OnlineGhosts instance;

    public static bool Exists => instance != null;

    /// <summary>Atraso de desenho atual (s): o espectador toca a música com o mesmo atraso.</summary>
    public static double CurrentDelay { get; private set; } = 0.2;

    private readonly List<Ghost> ghosts = new List<Ghost>();
    private Transform labelCanvas;
    private bool started;

    // tentativa do jogador local nesta cena
    private int localRun = -1;
    private int runStartFloor;
    private int lastSeq = -1;
    private double lastHitTime;
    private JArray pendingHits = new JArray();
    private float nextFlush;
    private float nextResync;
    private bool reportedEnd;

    private OnlineClient client => OnlineClient.Instance;
    private OnlineMatch match => OnlineMatch.Instance;

    /// <summary>Cria os fantasmas na fase atual (a cada tentativa, quando a música começa).</summary>
    public static void Begin()
    {
        if (instance != null)
        {
            // fase personalizada recomeça sem recarregar a cena: só começa uma tentativa nova
            instance.NewAttempt();
            return;
        }
        var go = new GameObject("OnlineGhosts");
        instance = go.AddComponent<OnlineGhosts>();
        instance.Build();
    }

    private void Build()
    {
        labelCanvas = PortUi.MakeCanvas(transform, "GhostLabels", 50);

        // aparência do planeta do jogador local, para os fantasmas terem o mesmo tamanho e desenho
        SpriteRenderer reference = ADOBase.controller.chosenPlanet.planetRenderer.sprite.meshRenderer;

        int slot = 0;
        foreach (int id in match.StartOrder)
        {
            slot++;
            if (id == client.myId)
            {
                continue;
            }
            var player = client.GetPlayer(id);
            // cores dos jogadores 2-4 do modo cooperativo (amarelo, verde...) para não confundir com o local
            var colors = scrPlayerManager.playerColors;
            Color color = colors != null && colors.Length > 0 ? colors[slot % colors.Length].ToRealColor() : Color.white;
            var ghost = new Ghost
            {
                id = id,
                name = player?.name ?? Loc.T("Jogador"),
                planetA = MakePlanet(reference, color),
                planetB = MakePlanet(reference, Color.Lerp(color, Color.white, 0.45f)),
            };
            ghost.tailA = AddTail(ghost.planetA, color);
            ghost.tailB = AddTail(ghost.planetB, Color.Lerp(color, Color.white, 0.45f));
            ghost.label = PortUi.MakeText(labelCanvas, ghost.name, 26, color);
            ghost.label.alignment = TextAnchor.MiddleCenter;
            ghost.label.rectTransform.sizeDelta = new Vector2(400f, 50f);
            var outline = ghost.label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            ghosts.Add(ghost);
        }
        started = true;
        Debug.Log($"[OnlineGhosts] {ghosts.Count} fantasma(s); ordem [{string.Join(",", match.StartOrder)}], eu {client.myId}; " +
                  $"sprite {(reference.sprite != null ? reference.sprite.name : "nenhum")}, camada {reference.gameObject.layer}, " +
                  $"escala {reference.transform.lossyScale}, ordem de desenho {reference.sortingOrder}");
    }

    private SpriteRenderer MakePlanet(SpriteRenderer reference, Color color)
    {
        var go = new GameObject("GhostPlanet");
        go.transform.SetParent(transform, false);
        // a câmera do jogo só desenha certas camadas: usa a mesma do planeta do jogador
        go.layer = reference.gameObject.layer;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerID = reference.sortingLayerID;
        sr.sortingOrder = reference.sortingOrder - 1; // por baixo dos planetas do jogador local
        sr.color = new Color(color.r, color.g, color.b, 0.8f);
        // o planeta do jogo é um pixel branco desenhado como círculo por um efeito próprio: usa um círculo nosso
        sr.sprite = Circle();
        float diameter = reference.bounds.size.x;
        if (diameter < ADOBase.controller.tileSize * 0.2f || diameter > ADOBase.controller.tileSize * 2f)
        {
            diameter = ADOBase.controller.tileSize * 0.5f;
        }
        go.transform.localScale = Vector3.one * diameter;
        return sr;
    }

    // cauda: cópia do sistema de partículas da cauda do planeta do jogador, na cor do fantasma
    private static ParticleSystem AddTail(SpriteRenderer planet, Color color)
    {
        var renderer = ADOBase.controller.chosenPlanet.planetRenderer;
        ParticleSystem source = renderer != null ? renderer.tailParticles : null;
        if (source == null)
        {
            return null;
        }
        GameObject copy = Object.Instantiate(source.gameObject, planet.transform, false);
        copy.name = "GhostTail";
        copy.transform.localPosition = Vector3.zero;
        // mesmo tamanho da cauda original, apesar da escala do círculo do fantasma
        Vector3 want = source.transform.lossyScale;
        Vector3 parent = planet.transform.lossyScale;
        copy.transform.localScale = new Vector3(want.x / Mathf.Max(parent.x, 1e-4f), want.y / Mathf.Max(parent.y, 1e-4f), want.z / Mathf.Max(parent.z, 1e-4f));
        SetLayer(copy, planet.gameObject.layer);
        copy.SetActive(true);
        // no modo espectador os planetas locais (de onde a cauda é copiada) ficam com os desenhos desligados
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
        }
        var ps = copy.GetComponent<ParticleSystem>();
        // as mesmas cores que o jogo usa na cauda (PlanetRenderer.SetTailColor), um pouco mais transparentes
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color * new Color(0.5f, 0.5f, 0.5f, 1f));
        var col = ps.colorOverLifetime;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color * new Color(0.6f, 0.6f, 0.6f), 1f) },
            new[] { new GradientAlphaKey(0.32f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gradient);
        ps.Clear(true);
        ps.Play(true);
        return ps;
    }

    private static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayer(child.gameObject, layer);
        }
    }

    // parado (caiu/terminou) não solta cauda; e um salto grande (recomeço) não pode deixar um rastro na tela
    private static void UpdateTail(ParticleSystem tail, Vector3 position, ref Vector3 last, bool emit, float jump)
    {
        if (tail == null)
        {
            return;
        }
        if ((position - last).sqrMagnitude > jump * jump)
        {
            tail.Clear(true);
        }
        last = position;
        var emission = tail.emission;
        if (emission.enabled != emit)
        {
            emission.enabled = emit;
        }
    }

    private static Sprite circle;

    // círculo branco de 1 unidade de diâmetro, com borda suave
    private static Sprite Circle()
    {
        if (circle != null)
        {
            return circle;
        }
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return circle;
    }

    private void NewAttempt()
    {
        localRun = -1;
        lastSeq = -1;
        reportedEnd = false;
        nextResync = 0f;
        nextFlush = 0f;
        pendingHits = new JArray();
        foreach (var g in ghosts)
        {
            g.placed = false; // nova tentativa: os fantasmas voltam ao início sem deslizar pela pista
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void LateUpdate()
    {
        if (!started || !OnlineMatch.Active)
        {
            return;
        }
        var controller = ADOBase.controller;
        var floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
        if (controller == null || floors == null || floors.Count < 2)
        {
            return;
        }
        ReportLocal(controller);

        double serverNow = client.ServerNowMs;
        // atraso de desenho: tempo para os toques chegarem (metade do ping + envio em pacotes + folga)
        float ping = client.pingMs > 0f ? client.pingMs : 150f;
        double wantDelay = Mathf.Clamp(ping / 2000f + FlushSeconds + 0.07f, 0.12f, 0.4f);
        // o ping oscila: o atraso muda devagar, senão os fantasmas dão pequenos saltos a cada variação
        if (smoothDelay <= 0.0 || System.Math.Abs(wantDelay - smoothDelay) > 0.25)
        {
            smoothDelay = wantDelay;
        }
        smoothDelay += (wantDelay - smoothDelay) * Mathf.Clamp01(Time.unscaledDeltaTime * 1.5f);
        double delay = smoothDelay;
        CurrentDelay = delay;
        float dt = Time.unscaledDeltaTime;
        double localSongPos = ADOBase.conductor.songposition_minusi;
        Camera cam = controller.camy != null && controller.camy.camobj != null ? controller.camy.camobj : Camera.main;
        foreach (var g in ghosts)
        {
            var run = match.GetRun(g.id);
            if (client.GetPlayer(g.id) == null)
            {
                // saiu da sala
                g.planetA.enabled = g.planetB.enabled = g.label.enabled = false;
                continue;
            }
            g.planetA.enabled = g.planetB.enabled = true;

            // antes do primeiro aviso do outro, todos largaram juntos: usa o tempo local
            double t = (run.run >= 0 ? run.SongPosAt(serverNow) : localSongPos) - delay * run.rate;
            bool dead = run.deadFloor >= 0;
            int n = Mathf.Clamp(run.startFloor, 0, floors.Count - 1);
            double nTime = double.MinValue;
            // quem chegou no meio não viu os toques anteriores: parte do bloco informado no último "run"
            if (run.syncFloor >= 0 && run.syncTime <= t)
            {
                n = Mathf.Clamp(run.syncFloor, 0, floors.Count - 1);
                nTime = run.syncTime;
            }
            for (int i = 0; i < run.hitTimes.Count && run.hitTimes[i] <= t; i++)
            {
                if (run.hitTimes[i] >= nTime)
                {
                    n = Mathf.Clamp(run.hitFloors[i], 0, floors.Count - 1);
                    nTime = run.hitTimes[i];
                }
            }
            if (dead)
            {
                n = Mathf.Clamp(run.deadFloor, 0, floors.Count - 1);
            }
            else if (run.finished)
            {
                n = floors.Count - 1;
            }
            scrFloor floor = floors[n];
            Vector3 center = floor.transform.position;
            Vector3 orbit = center;
            bool orbiting = !dead && !run.finished && n + 1 < floors.Count;
            if (orbiting)
            {
                scrFloor next = floors[n + 1];
                double span = next.entryTime - floor.entryTime;
                // sem limitar em 1: se o toque ainda não veio, o planeta passa do ponto, como no jogo
                double frac = span > 1e-6 ? Mathf.Clamp((float)((t - floor.entryTime) / span), -0.5f, 2f) : 1.0;
                double moved = scrMisc.GetAngleMoved(floor.entryangle, floor.exitangle, !floor.isCCW);
                double angle = floor.entryangle + (floor.isCCW ? -1.0 : 1.0) * moved * frac;
                float radius = controller.tileSize * floor.radiusScale;
                orbit = center + radius * new Vector3((float)System.Math.Sin(angle), (float)System.Math.Cos(angle), 0f);
            }
            // os planetas trocam de papel a cada bloco (um fica parado no bloco, o outro gira)
            SpriteRenderer still = n % 2 == 0 ? g.planetA : g.planetB;
            SpriteRenderer moving = n % 2 == 0 ? g.planetB : g.planetA;
            // os toques chegam em pacotes e a hora do outro é estimada: o alvo dá pequenos saltos. O desenho segue o
            // alvo com uma suavização curta (~35 ms) e só pula direto quando é uma troca grande (nova tentativa)
            float snap = controller.tileSize * 3f;
            Follow(still.transform, center, dt, snap, g.placed);
            Follow(moving.transform, orbit, dt, snap, g.placed);
            g.placed = true;
            g.focus = center;
            g.racing = !dead && !run.finished;
            SetAlpha(still, dead ? 0.25f : 0.8f);
            SetAlpha(moving, orbiting ? 0.8f : 0f);
            float jump = controller.tileSize * 3f;
            UpdateTail(g.tailA, g.planetA.transform.position, ref g.lastA, orbiting, jump);
            UpdateTail(g.tailB, g.planetB.transform.position, ref g.lastB, orbiting, jump);

            bool onScreen = false;
            if (cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint((g.planetA.transform.position + g.planetB.transform.position) * 0.5f + Vector3.up * controller.tileSize * 0.9f); // segue o meio do par de planetas: desliza em vez de pular de bloco em bloco
                onScreen = screen.z > 0f;
                g.label.transform.position = screen;
            }
            g.label.enabled = onScreen;
            string text = g.name + (dead ? "  (caiu)" : run.finished ? "  (terminou)" : "");
            if (g.label.text != Loc.T(text))
            {
                PortUi.SetText(g.label, text);
            }
        }
    }

    private static double smoothDelay;

    private static void Follow(Transform tr, Vector3 target, float dt, float snapDistance, bool placed)
    {
        Vector3 cur = tr.position;
        target.z = cur.z;
        if (!placed || (target - cur).sqrMagnitude > snapDistance * snapDistance)
        {
            tr.position = target;
            return;
        }
        tr.position = Vector3.Lerp(cur, target, 1f - Mathf.Exp(-dt * 28f));
    }

    // bloco em que o jogador estaria no tempo t acertando no ritmo (último com entryTime <= t)
    private static int FloorAt(List<scrFloor> floors, double t)
    {
        int lo = 0, hi = floors.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (floors[mid].entryTime <= t)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo;
    }

    private static void SetAlpha(SpriteRenderer sr, float a)
    {
        Color c = sr.color;
        if (!Mathf.Approximately(c.a, a))
        {
            c.a = a;
            sr.color = c;
        }
    }

    // avisa os outros sobre a tentativa do jogador local: começou (e onde está a música), caiu ou terminou
    private void ReportLocal(scrController controller)
    {
        if (reportedEnd || OnlineMatch.SpectatingFromStart)
        {
            // espectador: o jogador local só anda em piloto automático, não manda nada
            return;
        }
        States state = controller.currentState;
        bool playing = state == States.Countdown || state == States.PlayerControl || state == States.Checkpoint;
        if (playing && Time.unscaledTime >= nextResync)
        {
            if (localRun < 0)
            {
                localRun = match.NextLocalRun();
                runStartFloor = controller.currentSeqID;
                lastSeq = runStartFloor;
                lastHitTime = ADOBase.conductor.songposition_minusi;
            }
            nextResync = Time.unscaledTime + ResyncSeconds;
            client.SendGameEvent(new JObject
            {
                ["k"] = "run",
                ["n"] = localRun,
                ["f0"] = runStartFloor,
                ["cf"] = lastSeq,
                ["ct"] = lastHitTime,
                ["pr"] = match.localProgress,
                ["s"] = client.ServerNowMs,
                ["p"] = ADOBase.conductor.songposition_minusi,
                ["r"] = ADOBase.conductor.song != null ? ADOBase.conductor.song.pitch : 1f,
            });
        }
        if (localRun < 0)
        {
            return;
        }
        if (playing && controller.currentSeqID != lastSeq)
        {
            lastSeq = controller.currentSeqID;
            lastHitTime = ADOBase.conductor.songposition_minusi;
            pendingHits.Add(new JArray(lastSeq, lastHitTime));
            int count = ADOBase.lm.listFloors.Count;
            if (count > 1)
            {
                match.localProgress = Mathf.Max(match.localProgress, Mathf.Clamp01((float)lastSeq / (count - 1)));
            }
        }
        bool ending = state == States.Fail || state == States.Fail2 || state == States.Won;
        if (pendingHits.Count > 0 && (ending || Time.unscaledTime >= nextFlush))
        {
            client.SendGameEvent(new JObject { ["k"] = "h", ["n"] = localRun, ["h"] = pendingHits });
            pendingHits = new JArray();
            nextFlush = Time.unscaledTime + FlushSeconds;
        }
        if (state == States.Fail || state == States.Fail2)
        {
            reportedEnd = true;
            match.localDeaths++;
            client.SendGameEvent(new JObject { ["k"] = "die", ["n"] = localRun, ["f"] = controller.currentSeqID, ["pr"] = match.localProgress });
        }
        else if (state == States.Won)
        {
            reportedEnd = true;
            OnlineSpectate.BeginAfterFinish();
            float acc = controller.playerOne != null && controller.playerOne.marginTracker != null
                ? controller.playerOne.marginTracker.percentAcc
                : -1f;
            match.localFinished = true;
            match.localAccuracy = acc;
            match.localProgress = 1f;
            client.SendGameEvent(new JObject { ["k"] = "win", ["n"] = localRun, ["acc"] = acc });
        }
    }
}
