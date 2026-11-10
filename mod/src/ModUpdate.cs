// Aviso de versão nova do mod: ao abrir o jogo, pergunta ao GitHub qual é a última release (uma vez, sem enviar
// dados pessoais) e guarda o resultado para o aviso do seletor de fases (EntryButtons em Plugin.cs).
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AdofaiOnlineMod
{
    internal class UpdateChecker : MonoBehaviour
    {
        private const string Api = "https://api.github.com/repos/CopperFrame/adofai-online/releases/latest";
        public const string PageUrl = "https://github.com/CopperFrame/adofai-online/releases/latest";

        public static bool Available { get; private set; }
        public static string LatestVersion { get; private set; }

        private IEnumerator Start()
        {
            if (!OnlineOptions.CheckForUpdates)
            {
                yield break;
            }
            yield return new WaitForSecondsRealtime(8f); // não atrapalha a abertura do jogo
            using (var req = UnityWebRequest.Get(Api))
            {
                req.timeout = 10;
                req.SetRequestHeader("User-Agent", "ADOFAI-Online-mod/" + Plugin.Version);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogInfo("Verificação de versão nova não respondeu: " + req.error);
                    yield break;
                }
                string tag = null;
                try
                {
                    tag = JObject.Parse(req.downloadHandler.text).Value<string>("tag_name");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogInfo("Resposta da verificação de versão ilegível: " + e.Message);
                    yield break;
                }
                if (IsNewer(tag, Plugin.Version))
                {
                    LatestVersion = tag.TrimStart('v', 'V');
                    Available = true;
                    Plugin.Log.LogInfo("Há uma versão nova do mod: " + LatestVersion + " (instalada: " + Plugin.Version + ")");
                }
            }
        }

        /// <summary>"v0.2.4" é mais nova que "0.2.3"? Qualquer coisa que não se leia como versão conta como "não".</summary>
        internal static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (string.IsNullOrEmpty(latest) || !Version.TryParse(latest.TrimStart('v', 'V'), out a) || !Version.TryParse(current, out b))
            {
                return false;
            }
            return a > b;
        }
    }
}
