// Modo espectador. Usado por quem entrou na sala com a partida em andamento e por quem já terminou.
//
// O espectador roda a fase de verdade, com o jogador local em piloto automático e invisível, no mesmo ponto da
// música do jogador assistido. Assim a fase mostra os próprios efeitos (câmera, decorações, filtros...) e toca a
// própria música, exatamente como para quem joga:
//   - a fase começa (do início ou como checkpoint) no bloco em que o assistido está;
//   - durante a fase, a música do jogo é corrigida (scrConductor.ScrubMusicToTime) sempre que se afasta mais de
//     40 ms do assistido (com o mesmo atraso de desenho dos fantasmas);
//   - se a diferença for grande (o assistido caiu e recomeçou, ou trocou de jogador), a fase recomeça no ponto dele.
// ◀ ▶ trocam entre os jogadores que ainda estão na pista. "Sair" volta para o CLS.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OnlineSpectate : MonoBehaviour
{
    private const float FinishDelaySeconds = 2.5f;
    // diferença mínima para corrigir a música (abaixo disso é ruído do relógio sincronizado pela rede)
    private const double SmallDriftSeconds = 0.06;
    private const float DriftCheckSeconds = 0.25f;
    // à frente do assistido por mais que isto: recomeça (para trás não dá para corrigir só com a música)
    private const double AheadRestartSeconds = 1.5;
    private const float ResyncCooldownSeconds = 1f;
    // começa um pouco ATRÁS do assistido: a correção é sempre para a frente, e o piloto automático alcança os blocos
    private const double StartBehindSeconds = 1.0;
    // nunca recomeça duas vezes em menos que isto (a transição de tela bloqueia os botões enquanto acontece)
    private const float MinSecondsBetweenRestarts = 4f;
    // a fase pode começar até este tanto atrás do assistido (o resto a música corrige); mais que isso, recomeça
    private const double MaxStartGapSeconds = 8.0;

    private static OnlineSpectate instance;

    public static bool IsActive => instance != null && instance.activated;

    private bool activated;
    private int targetId = -1;
    private int runningFor = -1;   // jogador para quem a fase atual foi iniciada
    private int runningRun = -1;   // tentativa dele nessa hora
    private bool restarting;
    private int restartWaitCount;
    private float nextResync;
    private float startedAt = -100f;
    private int driftStrikes;   // medições seguidas fora do limite (corrige só com duas)
    private Text nameText;
    private GameObject bar;
    private Text emptyText;
    private readonly List<Renderer> hidden = new List<Renderer>();

    private OnlineClient client => OnlineClient.Instance;

    /// <summary>Entrou com a partida em andamento: assiste desde já (chamado enquanto a fase espera para começar).</summary>
    public static void Begin()
    {
        if (instance != null)
        {
            return;
        }
        instance = new GameObject("OnlineSpectate").AddComponent<OnlineSpectate>();
        DontDestroyOnLoad(instance.gameObject);
        instance.Activate();
    }

    /// <summary>Terminou a fase: depois de um instante, passa a assistir quem ainda está jogando.</summary>
    public static void BeginAfterFinish()
    {
        if (instance != null || !OnlineMatch.Active)
        {
            return;
        }
        instance = new GameObject("OnlineSpectate").AddComponent<OnlineSpectate>();
        DontDestroyOnLoad(instance.gameObject);
        instance.StartCoroutine(instance.ActivateLater());
    }

    private IEnumerator ActivateLater()
    {
        yield return new WaitForSecondsRealtime(FinishDelaySeconds);
        if (OnlineGhosts.RacingIds().Count == 0)
        {
            // ninguém mais na pista: fica na tela de vitória normal
            Destroy(gameObject);
            yield break;
        }
        OnlineMatch.Instance.EnterSpectateMode();
        Activate();
        RestartAtTarget();
    }

    /// <summary>
    /// Chamado pelo OnlineMatch enquanto a fase espera para começar: devolve true quando a fase está montada no
    /// ponto certo (um pouco atrás do jogador assistido) e pode começar, em piloto automático.
    /// A fase é montada no ponto de partida (GCS.checkpointNum) ANTES dessa espera; se o ponto estiver errado
    /// (ainda não se sabia onde o assistido estava), recomeça uma vez no ponto certo.
    /// </summary>
    public static bool ReadyToStart()
    {
        var s = instance;
        if (s == null)
        {
            return false;
        }
        var controller = ADOBase.controller;
        if (s.restarting)
        {
            // esperando o recomeço pedido montar a fase de novo (a espera antiga ainda roda até lá)
            if (WaitCount(controller) == s.restartWaitCount)
            {
                return false;
            }
            s.restarting = false;
        }
        s.PickTarget();
        if (!s.StartFloorFor(s.targetId, out int cp))
        {
            return false;
        }
        double? targetPos = OnlineGhosts.SongPosOf(s.targetId);
        if (cp == 0 && targetPos != null && targetPos.Value - OnlineGhosts.CurrentDelay < 0.0)
        {
            // o assistido ainda está na contagem regressiva; o espectador começa sem ela e ficaria à frente
            return false;
        }
        var floors = ADOBase.lm.listFloors;
        int current = Mathf.Clamp(GCS.checkpointNum, 0, floors.Count - 1);
        // montada atrás do assistido e não longe demais: começa (a música corrige para a frente)
        bool ok = cp >= current && floors[cp].entryTime - floors[current].entryTime <= MaxStartGapSeconds;
        if (!ok)
        {
            s.RestartAt(cp);
            return false;
        }
        RDC.auto = true;
        s.Diag($"começando no bloco {cp}");
        s.runningFor = s.targetId;
        s.runningRun = OnlineMatch.Instance.GetRun(s.targetId).run;
        s.nextResync = Time.unscaledTime + 0.5f;
        s.startedAt = Time.unscaledTime;
        s.driftStrikes = 0;
        return true;
    }

    // bloco de partida (como checkpoint) um pouco atrás de onde o jogador está agora
    private bool StartFloorFor(int id, out int cp)
    {
        cp = 0;
        double? pos = id >= 0 ? OnlineGhosts.SongPosOf(id) : null;
        var floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
        if (pos == null || floors == null || floors.Count < 2)
        {
            return false;
        }
        double t = pos.Value - OnlineGhosts.CurrentDelay - StartBehindSeconds;
        int floor = FloorAt(floors, t);
        cp = floor <= 1 ? 0 : Mathf.Min(floor, floors.Count - 2);
        return true;
    }

    // recomeça a fase montada no bloco cp (o início então passa pelo ReadyToStart)
    private void RestartAt(int cp)
    {
        if (restarting)
        {
            return;
        }
        Diag($"recomeçando no bloco {cp}");
        var controller = ADOBase.controller;
        GCS.checkpointNum = cp;
        restarting = true;
        restartWaitCount = WaitCount(controller);
        runningFor = -1;
        startedAt = Time.unscaledTime;
        controller.StartCoroutine(controller.ResetCustomLevel());
    }

    private void Activate()
    {
        activated = true;
        // o jogador continua "respondendo" (senão o scrPlayer.Hit recusa os acertos do piloto automático);
        // os toques na tela é que são ignorados no espectador (scrPlayer.ValidInputWasTriggered)
        client.OnEnd += OnMatchEnd;
        BuildUi();
    }

    private void OnDestroy()
    {
        RDC.auto = false;
        if (OnlineClient.Exists)
        {
            client.OnEnd -= OnMatchEnd;
        }
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>Saiu da partida (voltou ao CLS): desliga o modo espectador.</summary>
    public static void Stop()
    {
        if (instance != null)
        {
            Destroy(instance.gameObject);
        }
    }

    private void OnMatchEnd(Newtonsoft.Json.Linq.JToken _)
    {
        // a partida acabou: o OnlineMatch leva todos de volta para a sala (onde aparece o placar)
        ShowEmpty("Partida encerrada. Voltando para a sala…");
    }

    private void BuildUi()
    {
        Transform canvas = PortUi.MakeCanvas(transform, "SpectateUi", 430);

        if (client.isHost)
        {
            // o anfitrião pode encerrar a partida para todos (vai para o placar)
            var end = PortUi.MakeButton(canvas, "Encerrar partida", 32, () => OnlineMatch.Instance.EndMatch());
            PortUi.Place((RectTransform)end.transform, new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(340f, 84f));
        }

        var barImg = PortUi.MakePanel(canvas, "Bar", new Color(0.06f, 0.05f, 0.1f, 0.35f));
        bar = barImg.gameObject;
        PortUi.Place(barImg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(560f, 64f));
        var prev = PortUi.MakeButton(barImg.transform, "<", 30, () => Cycle(-1), light: true); Fade(prev);
        PortUi.Place((RectTransform)prev.transform, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(72f, 48f), new Vector2(0f, 0.5f));
        var next = PortUi.MakeButton(barImg.transform, ">", 30, () => Cycle(1), light: true); Fade(next);
        PortUi.Place((RectTransform)next.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(72f, 48f), new Vector2(1f, 0.5f));
        nameText = PortUi.MakeText(barImg.transform, "", 28, Color.white);
        nameText.alignment = TextAnchor.MiddleCenter;
        PortUi.Stretch(nameText.rectTransform);
        nameText.rectTransform.offsetMin = new Vector2(90f, 0f);
        nameText.rectTransform.offsetMax = new Vector2(-90f, 0f);

        emptyText = PortUi.MakeText(canvas, "", 40, Color.white);
        emptyText.alignment = TextAnchor.MiddleCenter;
        PortUi.Place(emptyText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 200f));
        emptyText.gameObject.SetActive(false);
    }

    // botão semitransparente para não atrapalhar a visão da fase
    private static void Fade(Button b)
    {
        var c = b.targetGraphic.color;
        c.a = 0.45f;
        b.targetGraphic.color = c;
        foreach (var t in b.GetComponentsInChildren<Text>())
        {
            var tc = t.color;
            tc.a = 0.8f;
            t.color = tc;
        }
    }

    private void ShowEmpty(string text)
    {
        if (emptyText == null)
        {
            return;
        }
        emptyText.gameObject.SetActive(true);
        PortUi.SetText(emptyText, text);
    }

    private void PickTarget()
    {
        List<int> racing = OnlineGhosts.RacingIds();
        if (!racing.Contains(targetId))
        {
            targetId = racing.Count > 0 ? racing[0] : -1;
        }
    }

    private void Cycle(int dir)
    {
        List<int> racing = OnlineGhosts.RacingIds();
        if (racing.Count == 0)
        {
            return;
        }
        int i = racing.IndexOf(targetId);
        i = i < 0 ? 0 : (i + dir + racing.Count) % racing.Count;
        targetId = racing[i];
    }

    // o contador é um campo privado do scrController: lido por reflexão
    private static int WaitCount(scrController c)
    {
        return AdofaiOnlineMod.Hooks.WaitForStartCount(c);
    }

    private void Leave()
    {
        RDC.auto = false;
        ADOBase.controller.QuitToMainMenu(); // fase personalizada: volta para o CLS
    }

    private void LateUpdate()
    {
        if (!activated || !ADOBase.isScnGame || ADOBase.controller == null)
        {
            return;
        }
        PortUi.EnsureEventSystem();
        HideLocalPlanets();
        QuietLevel();
        PickTarget();
        List<int> racing = OnlineGhosts.RacingIds();
        if (targetId < 0)
        {
            bar.SetActive(false);
            if (!emptyText.gameObject.activeSelf)
            {
                ShowEmpty("Ninguém mais está jogando.");
            }
            return;
        }
        bar.SetActive(true);
        if (emptyText.gameObject.activeSelf && client.inGame)
        {
            emptyText.gameObject.SetActive(false);
        }
        var player = client.GetPlayer(targetId);
        string label = Loc.T("Assistindo  <b>") + (player?.name ?? Loc.T("Jogador")) + "</b>" +
                       (racing.Count > 1 ? $"  <size=30><color=#ffffff88>({racing.IndexOf(targetId) + 1}/{racing.Count})</color></size>" : "");
        if (nameText.text != Loc.T(label))
        {
            PortUi.SetText(nameText, label);
        }
        KeepInSync();
    }

    // mantém a fase local no mesmo ponto da música do jogador assistido
    private float nextDiag;

    // diagnóstico no log (logcat "[Spectate]"): onde a fase local está x onde o assistido está
    private void Diag(string what)
    {
        var conductor = ADOBase.conductor;
        double? pos = targetId >= 0 ? OnlineGhosts.SongPosOf(targetId) : null;
        double desired = pos.HasValue ? pos.Value - OnlineGhosts.CurrentDelay : double.NaN;
        Debug.Log($"[Spectate] {what} estado={ADOBase.controller.currentState} alvo={targetId} run={OnlineMatch.Instance.GetRun(targetId).run}/{runningRun} " +
                  $"local={conductor.songposition_minusi:0.000} desejado={desired:0.000} dif={(desired - conductor.songposition_minusi):0.000} " +
                  $"bloco={ADOBase.controller.currentSeqID} checkpoint={GCS.checkpointNum} reiniciando={restarting} atraso={OnlineGhosts.CurrentDelay:0.000} " +
                  $"ping={client.pingMs:0} auto={RDC.auto}");
    }

    private void KeepInSync()
    {
        if (Time.unscaledTime >= nextDiag)
        {
            nextDiag = Time.unscaledTime + 2f;
            Diag("estado");
        }
        if (restarting)
        {
            return;
        }
        var controller = ADOBase.controller;
        States state = controller.currentState;
        bool playing = state == States.PlayerControl || state == States.Countdown || state == States.Checkpoint;
        var run = OnlineMatch.Instance.GetRun(targetId);
        // trocou de jogador, ou ele recomeçou (caiu): a fase recomeça no ponto dele
        if (runningFor >= 0 && (targetId != runningFor || run.run != runningRun))
        {
            RestartAtTarget();
            return;
        }
        if (!playing || Time.unscaledTime < nextResync)
        {
            return;
        }
        double? pos = OnlineGhosts.SongPosOf(targetId);
        if (pos == null)
        {
            return;
        }
        var conductor = ADOBase.conductor;
        double desired = pos.Value - OnlineGhosts.CurrentDelay;
        double drift = desired - conductor.songposition_minusi;
        float pitch = conductor.song != null ? conductor.song.pitch : 1f;
        if (drift < -AheadRestartSeconds * pitch)
        {
            // a fase local passou do assistido: só recomeçando atrás dele
            RestartAtTarget();
            return;
        }
        if (System.Math.Abs(drift) <= SmallDriftSeconds * pitch || state != States.PlayerControl)
        {
            driftStrikes = 0;
            nextResync = Time.unscaledTime + DriftCheckSeconds;
            return;
        }
        // a diferença precisa se repetir em duas medições seguidas (uma só pode ser oscilação do relógio)
        if (++driftStrikes < 2)
        {
            nextResync = Time.unscaledTime + DriftCheckSeconds;
            return;
        }
        driftStrikes = 0;
        // atrás (qualquer distância) ou um pouco à frente: só a música anda; o piloto automático acompanha.
        // A música passa a tocar 0,1 s depois (ScrubMusicToTime): mira onde ele estará nessa hora. O
        // ScrubMusicToTime também desconta a calibragem de entrada do aparelho (songposition_minusi): soma ela.
        // O ScrubMusicToTime (feito para o editor) também reinicia o "último acerto" dos planetas, o que defasaria
        // o piloto automático dos blocos: guarda e devolve esse valor, para só a música andar.
        var saved = new List<double>();
        foreach (scrPlayer player in ADOBase.playerManager)
        {
            saved.Add(player.lastHit);
        }
        Diag("corrigindo música");
        conductor.ScrubMusicToTime(desired + (0.1 + scrConductor.calibration_i) * pitch);
        int i = 0;
        foreach (scrPlayer player in ADOBase.playerManager)
        {
            player.lastHit = saved[i++];
        }
        nextResync = Time.unscaledTime + ResyncCooldownSeconds;
    }

    // recomeça a fase local no ponto em que o jogador assistido está
    private void RestartAtTarget()
    {
        if (restarting || targetId < 0 || Time.unscaledTime - startedAt < MinSecondsBetweenRestarts)
        {
            return;
        }
        if (StartFloorFor(targetId, out int cp))
        {
            RestartAt(cp);
        }
    }

    // sem sons de acerto nem números de contagem (o PlayHitTimes já não agenda; isto cobre o resto)
    private void QuietLevel()
    {
        ADOBase.conductor.hitSoundVolume = 0f;
        var ui = scrUIController.instance;
        if (ui != null && ui.txtCountdown != null)
        {
            foreach (var g in ui.txtCountdown.GetComponentsInChildren<Graphic>(true))
            {
                if (g.enabled)
                {
                    g.enabled = false;
                }
            }
            foreach (var r in ui.txtCountdown.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled)
                {
                    r.enabled = false;
                }
            }
        }
    }

    // o jogador local está em piloto automático só para a fase andar: os planetas dele não aparecem
    private void HideLocalPlanets()
    {
        if (ADOBase.playerManager == null)
        {
            return;
        }
        foreach (scrPlayer player in ADOBase.playerManager)
        {
            foreach (scrPlanet planet in player.planetarySystem.planetList)
            {
                planet.GetComponentsInChildren(true, hidden);
                foreach (var r in hidden)
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                    }
                }
            }
        }
    }

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
}
