using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AdofaiOnlineMod
{
    [BepInPlugin("adofai.port.online", "ADOFAI Online", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.2.3";
        internal static BepInEx.Logging.ManualLogSource Log;

        /// <summary>Partes do mod que não conseguiram se ligar ao jogo (depois de uma atualização dele, por exemplo).</summary>
        internal static readonly List<string> PatchProblems = new List<string>();
        internal static BepInEx.Configuration.ConfigEntry<bool> CheckForUpdates;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("ADOFAI Online carregado (Unity " + Application.unityVersion + ")");

            // servidores: BepInEx/config/adofai.port.online.cfg (nada fixo no código)
            // por padrão usa os servidores públicos do projeto (Oracle Cloud em São Paulo; Cloudflare de reserva)
            ServerConfig.Primary = (Config.Bind("Server", "Primary", "https://150-230-231-11.sslip.io",
                "Address of the room server to try first. All players in a room must use the same server. Empty = none.").Value ?? "").Trim();
            ServerConfig.Fallback = (Config.Bind("Server", "Fallback", "https://adofai-port-online.adolfheim.workers.dev",
                "Optional second server, used when the first does not answer.").Value ?? "").Trim();
            if (ServerConfig.Primary.Length == 0 && ServerConfig.Fallback.Length == 0)
            {
                Log.LogWarning("No server configured: edit BepInEx/config/adofai.port.online.cfg");
            }

            // idioma das telas do mod: auto = o idioma do jogo (português, coreano ou, nos demais, inglês)
            string language = (Config.Bind("General", "Language", "auto",
                new BepInEx.Configuration.ConfigDescription("Language of the mod's screens: auto (follows the game language), en, pt or ko.",
                    new BepInEx.Configuration.AcceptableValueList<string>("auto", "en", "pt", "ko"))).Value ?? "auto").Trim().ToLowerInvariant();
            Loc.Preferred = language == "en" ? Loc.Mode.English
                : language == "pt" ? Loc.Mode.Portuguese
                : language == "ko" ? Loc.Mode.Korean
                : Loc.Mode.Auto;
            Log.LogInfo("Language setting: " + language);
            CheckForUpdates = Config.Bind("General", "CheckForUpdates", true,
                "Ask GitHub (api.github.com) once at startup whether a newer version of the mod exists, and show a notice in the level selector. No personal data is sent.");

            var harmony = new Harmony("adofai.port.online");
            // um patch por vez: se o jogo mudar e um deles falhar, o resto continua e o jogador é avisado na tela
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    PatchProblems.Add(type.Name);
                    Log.LogError("patch " + type.Name + " falhou: " + e.Message);
                }
            }

            // a espera do início da fase (corrotina) e o recomeço automático depois de cair
            var anyValid = AccessTools.Method(typeof(scrPlayerManager), "AnyValidInputWasTriggered");
            PatchAnyValid(harmony, "WaitForStartCo", () => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(scrController), "WaitForStartCo")),
                AccessTools.Method(typeof(Hooks), nameof(Hooks.AnyOrOnlineStart)), anyValid);
            PatchAnyValid(harmony, "Fail2_Update", () => AccessTools.Method(typeof(scrController), "Fail2_Update"),
                AccessTools.Method(typeof(Hooks), nameof(Hooks.AnyOrAutoRestart)), anyValid);

            if (PatchProblems.Count > 0)
            {
                Log.LogWarning("Partes do mod que não funcionam nesta versão do jogo: " + string.Join(", ", PatchProblems));
            }
            gameObject.AddComponent<UpdateChecker>();
        }

        private static void PatchAnyValid(Harmony harmony, string name, System.Func<MethodBase> findTarget, MethodInfo replacement, MethodInfo anyValid)
        {
            try
            {
                MethodBase target = findTarget();
                if (target == null || anyValid == null)
                {
                    throw new System.Exception("método alvo não encontrado");
                }
                var transpiler = new HarmonyMethod(typeof(Hooks), nameof(Hooks.Replace));
                Hooks.Pending[target] = (anyValid, replacement);
                harmony.Patch(target, transpiler: transpiler);
            }
            catch (System.Exception e)
            {
                PatchProblems.Add(name);
                Log.LogError("patch " + name + " falhou: " + e.Message);
            }
        }
    }

    public static class Hooks
    {
        internal static readonly Dictionary<MethodBase, (MethodInfo from, MethodInfo to)> Pending = new Dictionary<MethodBase, (MethodInfo, MethodInfo)>();

        // troca a chamada de AnyValidInputWasTriggered por uma que também considera a partida online
        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var (from, to) = Pending[original];
            int n = 0;
            foreach (var ins in instructions)
            {
                if (ins.Calls(from))
                {
                    n++;
                    yield return new CodeInstruction(OpCodes.Call, to);
                }
                else
                {
                    yield return ins;
                }
            }
            Plugin.Log.LogInfo(original.DeclaringType?.Name + "." + original.Name + ": " + n + " chamada(s) trocada(s)");
            if (n == 0)
            {
                Plugin.PatchProblems.Add(original.Name); // o jogo mudou: a chamada que trocamos não existe mais
            }
        }

        // partida online: a fase começa na hora marcada pelo anfitrião, não com um toque
        public static bool AnyOrOnlineStart(scrPlayerManager manager)
        {
            return OnlineMatch.Active ? OnlineMatch.ShouldStartNow() : manager.AnyValidInputWasTriggered();
        }

        // recomeça sozinho depois de cair, para continuar a corrida
        public static bool AnyOrAutoRestart(scrPlayerManager manager)
        {
            return manager.AnyValidInputWasTriggered() || OnlineMatch.ShouldAutoRestart();
        }

        private static readonly FieldInfo WaitField = AccessTools.Field(typeof(scrController), "waitForStartCoCallCount");

        public static int WaitForStartCount(scrController c)
        {
            return (int)WaitField.GetValue(c);
        }
    }

    // espectador: só assiste, o planeta dele anda em piloto automático
    [HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.ValidInputWasTriggered))]
    internal static class SpectatorNoInput
    {
        private static void Postfix(ref bool __result)
        {
            if (OnlineMatch.SpectatingFromStart)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(scrConductor), nameof(scrConductor.PlayHitTimes))]
    internal static class SpectatorNoHitsounds
    {
        private static bool Prefix()
        {
            return !OnlineMatch.SpectatingFromStart;
        }
    }

    [HarmonyPatch(typeof(scrCountdown), nameof(scrCountdown.ShowGetReady))]
    internal static class SpectatorNoGetReady
    {
        private static bool Prefix()
        {
            return !OnlineMatch.SpectatingFromStart;
        }
    }

    [HarmonyPatch(typeof(scnGame), nameof(scnGame.Play))]
    internal static class SpectatorNoCountdown
    {
        private static void Postfix()
        {
            if (OnlineMatch.SpectatingFromStart && ADOBase.controller != null)
            {
                ADOBase.controller.forceNoCountdown = true;
            }
        }
    }

    [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
    internal static class SpectatorHideAutoLabel
    {
        private static void Postfix(scrShowIfDebug __instance)
        {
            if (OnlineMatch.SpectatingFromStart && Traverse.Create(__instance).Field("txt").GetValue() is Behaviour txt)
            {
                txt.enabled = false;
            }
        }
    }

    // Esc / voltar fecha primeiro as telas do online (em vez de abrir o menu de pausa)
    [HarmonyPatch(typeof(RDInput), nameof(RDInput.cancelPress), MethodType.Getter)]
    internal static class EscapeClosesOnlineScreens
    {
        private static int handledFrame = -1;

        private static void Postfix(ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            if (!OnlineScreen.IsOpen && !LevelBrowser.IsOpen)
            {
                return;
            }
            __result = false; // o jogo não vê esse Esc
            if (handledFrame == Time.frameCount)
            {
                return;
            }
            handledFrame = Time.frameCount;
            if (LevelBrowser.IsOpen)
            {
                LevelBrowser.Close();
            }
            else
            {
                OnlineScreen.Back();
            }
        }
    }

    // seletor de fases (CLS): liga as telas do online e coloca os botões de entrada
    [HarmonyPatch(typeof(scnCLS), "Start")]
    internal static class ClsAttach
    {
        private static void Postfix()
        {
            Plugin.Log.LogInfo("scnCLS.Start: ligando as telas do online");
            LevelBrowser.AttachToCLS();
            OnlineMatch.OnReturnedToCLS();
            OnlineScreen.AttachToCLS();
            new GameObject("OnlineEntryButtons").AddComponent<EntryButtons>();
        }
    }

    internal class EntryButtons : MonoBehaviour
    {
        private GameObject root;
        private GameObject notice;
        private Text noticeText;
        private string shownNotice;

        private void Start()
        {
            Transform canvas = PortUi.MakeCanvas(transform, "OnlineEntry", 300);
            root = canvas.gameObject;
            var online = PortUi.MakeStoneButton(canvas, "Online", 30, OnlineScreen.Open);
            PortUi.Place((RectTransform)online.transform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(220f, 74f), new Vector2(1f, 1f));
            var levels = PortUi.MakeStoneButton(canvas, "Fases da comunidade", 26, LevelBrowser.Open);
            PortUi.Place((RectTransform)levels.transform, new Vector2(1f, 1f), new Vector2(-24f, -108f), new Vector2(360f, 74f), new Vector2(1f, 1f));

            // aviso no alto, à esquerda, abaixo do título: versão nova do mod ou partes do mod que o jogo atual não aceita
            var panel = PortUi.MakePanel(canvas, "Notice", new Color(0.09f, 0.07f, 0.14f, 0.92f));
            notice = panel.gameObject;
            PortUi.Place(panel.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -120f), new Vector2(760f, 96f), new Vector2(0f, 1f)); // embaixo ficam os nomes dos portais
            noticeText = PortUi.MakeText(panel.transform, "", 28, Color.white);
            noticeText.alignment = TextAnchor.MiddleLeft;
            noticeText.horizontalOverflow = HorizontalWrapMode.Wrap;
            PortUi.Stretch(noticeText.rectTransform);
            noticeText.rectTransform.offsetMin = new Vector2(24f, 6f);
            noticeText.rectTransform.offsetMax = new Vector2(-24f, -6f);
            panel.raycastTarget = true;
            var btn = notice.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Application.OpenURL(UpdateChecker.PageUrl));
            notice.SetActive(false);
        }

        private string CurrentNotice()
        {
            if (Plugin.PatchProblems.Count > 0)
            {
                return "O jogo foi atualizado e parte do mod não funciona (" + string.Join(", ", Plugin.PatchProblems.ToArray()) + "). Clique para procurar uma versão nova.";
            }
            if (UpdateChecker.Available)
            {
                return "Nova versão do mod disponível: " + UpdateChecker.LatestVersion + " (você tem " + Plugin.Version + "). Clique para baixar.";
            }
            return null;
        }

        private void Update()
        {
            if (root == null)
            {
                return;
            }
            bool open = OnlineScreen.IsOpen || LevelBrowser.IsOpen;
            // os botões ficam atrás das telas do online; o aviso também
            foreach (Transform child in root.transform)
            {
                if (child.gameObject != notice)
                {
                    child.gameObject.SetActive(!open);
                }
            }
            string text = CurrentNotice();
            if (text != shownNotice)
            {
                shownNotice = text;
                if (text != null)
                {
                    PortUi.SetText(noticeText, text);
                }
            }
            notice.SetActive(text != null && !open);
        }
    }
}
