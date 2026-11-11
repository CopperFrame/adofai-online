// Peças (métodos de scnCLS e ImportLevelsCLS) que o mod precisa e que o jogo original não tem.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ADOFAI;
using ADOFAI.CLS;
using HarmonyLib;

namespace AdofaiOnlineMod
{
    /// <summary>Endereços dos servidores, lidos do arquivo de configuração do BepInEx (preenchidos pelo Plugin).</summary>
    public static class ServerConfig
    {
        public static string Primary = "";
        public static string Fallback = "";
    }

    public static class ClsExtensions
    {
        // abre o painel de importação e instala os .zip já baixados
        public static void ImportAndInstall(this ImportLevelsCLS panel, string[] zipPaths)
        {
            panel.OnOpenImportPanel();
            var addContent = AccessTools.Method(typeof(ImportLevelsCLS), "AddContent");
            foreach (string zipPath in zipPaths)
            {
                panel.StartCoroutine((IEnumerator)addContent.Invoke(panel, new object[] { zipPath, null, false }));
            }
            panel.ShowContent(ImportLevelsCLS.ContentType.InstallContent);
            AccessTools.Method(typeof(ImportLevelsCLS), "InstallContent").Invoke(panel, null);
        }

        private static readonly FieldInfo LevelDirs = AccessTools.Field(typeof(scnCLS), "loadedLevelDirs");

        private static IEnumerable<KeyValuePair<string, GenericDataCLS>> LocalLevelEntries(scnCLS cls, out Dictionary<string, string> dirs)
        {
            dirs = (Dictionary<string, string>)LevelDirs.GetValue(cls);
            return cls.loadedLevels;
        }

        public static string FindLevelFileByHash(this scnCLS cls, string hash)
        {
            foreach (var item in LocalLevelEntries(cls, out var dirs))
            {
                if (item.Value != null && item.Value.isLevel && item.Value.Hash == hash &&
                    dirs.TryGetValue(item.Key, out string dir) && dir != null)
                {
                    return AdoPackageInstaller.FindLevelFile(dir).Value;
                }
            }
            return null;
        }

        public static string FindLevelFileByMeta(this scnCLS cls, string title, string artist, string author)
        {
            string wantTitle = Simplify(title);
            string wantArtist = Simplify(artist);
            string wantAuthor = Simplify(author);
            if (wantTitle.Length == 0)
            {
                return null;
            }
            foreach (var item in LocalLevelEntries(cls, out var dirs))
            {
                if (item.Value == null || !item.Value.isLevel || !dirs.TryGetValue(item.Key, out string dir) || dir == null)
                {
                    continue;
                }
                LevelDataCLS lv = item.Value.level;
                if (Simplify(lv.title) != wantTitle)
                {
                    continue;
                }
                string haveAuthor = Simplify(lv.author);
                string haveArtist = Simplify(lv.artist);
                bool match = wantAuthor.Length > 0 && haveAuthor.Length > 0
                    ? haveAuthor.Contains(wantAuthor) || wantAuthor.Contains(haveAuthor)
                    : wantArtist.Length > 0 && haveArtist.Length > 0 && (haveArtist.Contains(wantArtist) || wantArtist.Contains(haveArtist));
                if (match)
                {
                    return AdoPackageInstaller.FindLevelFile(dir).Value;
                }
            }
            return null;
        }

        public static string GetLevelDir(this scnCLS cls, string key)
        {
            LocalLevelEntries(cls, out var dirs);
            return dirs.TryGetValue(key, out string dir) ? dir : null;
        }

        private static readonly MethodInfo ScanLevelsMethod = AccessTools.Method(typeof(scnCLS), "ScanLevels");
        private static bool indexingLevels;

        /// <summary>
        /// O jogo só lê as pastas de fases (locais e da oficina do Steam) ao entrar numa categoria; com a tela inicial do
        /// seletor aberta a lista está vazia. Lê as pastas sem mexer na tela, para o online enxergar as fases instaladas.
        /// </summary>
        public static IEnumerator EnsureLevelsIndexed(this scnCLS cls)
        {
            if (ScanLevelsMethod == null || cls.featuredLevelsMode || indexingLevels || cls.GetLocalLevels().Count > 0)
            {
                yield break;
            }
            indexingLevels = true;
            System.Threading.Tasks.Task scan = null;
            try
            {
                scan = (System.Threading.Tasks.Task)ScanLevelsMethod.Invoke(cls, new object[] { System.Threading.CancellationToken.None });
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Não deu para ler as fases instaladas: " + e.Message);
            }
            while (scan != null && !scan.IsCompleted)
            {
                yield return null;
            }
            indexingLevels = false;
        }

        public static List<KeyValuePair<string, LevelDataCLS>> GetLocalLevels(this scnCLS cls)
        {
            var list = new List<KeyValuePair<string, LevelDataCLS>>();
            foreach (var item in LocalLevelEntries(cls, out var dirs))
            {
                if (item.Value != null && item.Value.isLevel && dirs.TryGetValue(item.Key, out string dir) && dir != null)
                {
                    list.Add(new KeyValuePair<string, LevelDataCLS>(item.Key, item.Value.level));
                }
            }
            return list;
        }

        private static string Simplify(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in PortUi.StripTags(s ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
