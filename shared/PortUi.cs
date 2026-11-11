// Port: kit de interface montada por código (UGUI) para as telas novas do port (download de fases, online).
// Visual do menu de pausa: blocos cinza-claros de cantos arredondados com texto escuro sobre fundo escuro, e a
// fonte do jogo (com troca automática para uma fonte do sistema quando o texto tem caracteres que ela não tem).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public static class PortUi
{
    public static readonly Color Bg = new Color(0.035f, 0.03f, 0.06f, 1f);
    public static readonly Color Card = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color Tile = new Color(0.82f, 0.82f, 0.83f, 1f);
    public static readonly Color TileText = new Color(0.2f, 0.2f, 0.23f, 1f);
    public static readonly Color DarkTile = new Color(1f, 1f, 1f, 0.1f);
    public static readonly Color Muted = new Color(1f, 1f, 1f, 0.6f);
    public static readonly Color Accent = new Color(0.36f, 0.62f, 1f, 1f);
    public static readonly Color Danger = new Color(1f, 0.42f, 0.32f, 1f);

    // a fonte do jogo desenha as letras bem menores que uma fonte do sistema no mesmo tamanho
    private const float GameFontScale = 1.45f;

    private static Font gameFont;
    private static Font osFont;
    private static Sprite rounded;

    public static Font GameFont
    {
        get
        {
            if (gameFont == null)
            {
                gameFont = RDString.GetFontDataForLanguage(RDString.language).font;
                if (gameFont == null)
                {
                    gameFont = OsFont;
                }
            }
            return gameFont;
        }
    }

    public static Font OsFont
    {
        get
        {
            if (osFont == null)
            {
                osFont = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Roboto", "Noto Sans", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK SC", "Droid Sans Fallback",
                    "Arial", "Segoe UI", "Meiryo", "Malgun Gothic", "Microsoft YaHei",
                }, 32);
            }
            return osFont;
        }
    }

    // quadrado de cantos arredondados gerado em código, usado como sprite fatiado (9-slice); vive enquanto o app rodar
    public static Sprite Rounded
    {
        get
        {
            if (rounded == null)
            {
                rounded = MakeRoundedSprite();
            }
            return rounded;
        }
    }

    private static Sprite MakeRoundedSprite()
    {
        const int size = 64;
        const float r = 22f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    public static string StripTags(string s) => Regex.Replace(s ?? "", "<[^>]*>", "").Trim();

    // ------------------------------------------------------------------ arte do jogo (pedra)
    // Sprites do próprio jogo que a cena do CLS já carrega: as placas de pedra dos botões (comprida e curta) e o
    // botão redondo de voltar. Eles são usados inteiros, na proporção original (como o jogo usa); esticar ou fatiar
    // deforma a pedra. Fora do CLS podem não estar carregados: aí os botões caem no estilo arredondado simples.

    private static readonly Dictionary<string, Sprite> gameSprites = new Dictionary<string, Sprite>();

    /// <summary>Sprite do jogo já carregado na memória, pelo nome (ou null).</summary>
    public static Sprite GameSprite(string name)
    {
        if (gameSprites.TryGetValue(name, out var cached) && cached != null)
        {
            return cached;
        }
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (s.name == name)
            {
                gameSprites[name] = s;
                return s;
            }
        }
        return null;
    }

    /// <summary>Painel escuro translúcido (colunas e janelas das telas do port).</summary>
    public static Image MakeStonePanel(Transform parent, string name) => MakePanel(parent, name, new Color(0f, 0f, 0f, 0.4f));

    /// <summary>Botão de pedra do jogo, com texto branco e sombra (ou o arredondado simples, se a arte não estiver carregada).</summary>
    public static Button MakeStoneButton(Transform parent, string label, int size, Action onClick, bool bright = true)
    {
        Sprite longStone = GameSprite("small_button_long");
        Sprite shortStone = GameSprite("small_button_short");
        if (longStone == null || shortStone == null)
        {
            return MakeButton(parent, label, size, onClick, light: bright);
        }
        var rt = NewRect("Button " + label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = longStone;
        img.type = Image.Type.Simple;
        img.preserveAspect = true; // a pedra nunca deforma; a placa (comprida ou curta) é escolhida pelo formato
        img.color = bright ? Color.white : StoneDim;
        var fit = rt.gameObject.AddComponent<StoneFit>();
        fit.longStone = longStone;
        fit.shortStone = shortStone;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(() => onClick());
        if (!string.IsNullOrEmpty(label))
        {
            var t = MakeText(rt, label, size, Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }
        return b;
    }

    /// <summary>Escolhe a placa comprida ou a curta conforme o formato do botão.</summary>
    public class StoneFit : MonoBehaviour
    {
        public Sprite longStone;
        public Sprite shortStone;

        private void Start() => Apply();

        private void OnRectTransformDimensionsChange() => Apply();

        private void Apply()
        {
            var img = GetComponent<Image>();
            Rect r = ((RectTransform)transform).rect;
            if (img == null || r.height <= 0f)
            {
                return;
            }
            // proporções das placas: comprida ~5,6:1, curta ~3,9:1
            img.sprite = r.width / r.height >= 4.7f ? longStone : shortStone;
        }
    }

    /// <summary>Pedra escura = não selecionado; pedra clara = selecionado.</summary>
    public static readonly Color StoneDim = new Color(0.55f, 0.55f, 0.6f, 1f);

    public static void SetStoneSelected(Button b, bool selected)
    {
        if (b.GetComponent<StoneFit>() != null)
        {
            b.GetComponent<Image>().color = selected ? Color.white : StoneDim;
            var t = b.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.75f);
            }
        }
        else
        {
            SetSelected(b, selected, DarkTile);
        }
    }

    /// <summary>Botão redondo de voltar do jogo (back_button), ou um botão simples com a seta.</summary>
    public static Button MakeBackButton(Transform parent, Action onClick)
    {
        Sprite sprite = GameSprite("back_button");
        if (sprite == null)
        {
            var simple = MakeButton(parent, "", 0, onClick, light: true);
            AddIcon(simple.transform, Resources.Load<Sprite>("new/icon-previous"), 60f);
            return simple;
        }
        var img = NewRect("Back", parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        return b;
    }

    /// <summary>Canvas em tela cheia por cima do jogo, escalada para telas de celular.</summary>
    /// <summary>A cena do jogo pode não ter EventSystem; sem ele os botões das telas do port não recebem toques.</summary>
    public static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            return;
        }
        var go = new GameObject("PortEventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
        UnityEngine.Object.DontDestroyOnLoad(go);
    }

    public static Transform MakeCanvas(Transform parent, string name, int sortingOrder)
    {
        EnsureEventSystem();
        var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Loc.Register(canvasGo.transform);
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f); // ~20% maior que 1080p: telas de celular são pequenas
        scaler.matchWidthOrHeight = 1f;
        return canvasGo.transform;
    }

    // fonte do jogo quando ela tem todos os caracteres; senão, uma fonte do sistema (títulos em CJK etc.)
    public static void SetText(Text t, string s)
    {
        if (Loc.IsShown(t, s))
        {
            return; // nada mudou (a tela se atualiza a cada clique): reatribuir faria o texto piscar
        }
        t.text = s;
        Font f = GameFont;
        foreach (char c in StripTags(s))
        {
            if (!char.IsWhiteSpace(c) && !f.HasCharacter(c))
            {
                f = OsFont;
                break;
            }
        }
        t.font = f;
        // o tamanho-base fica em resizeTextMaxSize (bestFit não é usado aqui)
        int size = t.resizeTextMaxSize;
        if (size > 0)
        {
            t.fontSize = Mathf.RoundToInt(size * (f == GameFont && f != OsFont ? GameFontScale : 1f));
        }
    }

    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static Image MakePanel(Transform parent, string name, Color color)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = Rounded;
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    public static Text MakeText(Transform parent, string text, int size, Color color)
    {
        var t = NewRect("Text", parent).gameObject.AddComponent<Text>();
        t.fontSize = size;
        t.color = color;
        t.supportRichText = true;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.resizeTextMaxSize = size;
        SetText(t, text);
        return t;
    }

    public static Image AddIcon(Transform parent, Sprite sprite, float size)
    {
        var img = NewRect("Icon", parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.color = TileText;
        img.raycastTarget = false;
        Place(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        return img;
    }

    public static Button MakeButton(Transform parent, string label, int size, Action onClick, bool light = false)
    {
        var rt = NewRect("Button " + label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Rounded;
        img.type = Image.Type.Sliced;
        img.color = light ? Tile : DarkTile;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(() => onClick());
        if (!string.IsNullOrEmpty(label))
        {
            var t = MakeText(rt, label, size, light ? TileText : Color.white);
            t.alignment = TextAnchor.MiddleCenter;
            Stretch(t.rectTransform);
        }
        return b;
    }

    /// <summary>Selecionado = bloco claro com texto escuro (como os botões da pausa); senão, texto claro.</summary>
    public static void SetSelected(Button b, bool selected, Color idle)
    {
        b.GetComponent<Image>().color = selected ? Tile : idle;
        var t = b.GetComponentInChildren<Text>();
        if (t != null)
        {
            t.color = selected ? TileText : Color.white;
        }
    }

    public static InputField MakeInputField(Transform parent, string placeholder, float rightPadding = 100f, int size = 38)
    {
        var rt = NewRect("Input", parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Rounded;
        img.type = Image.Type.Sliced;
        img.color = DarkTile;
        var field = rt.gameObject.AddComponent<InputField>();
        field.targetGraphic = img;
        var text = MakeText(rt, "", size, Color.white);
        text.supportRichText = false;
        text.font = OsFont; // o que o jogador digita pode ter qualquer alfabeto
        text.resizeTextMaxSize = 0;
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(30f, 0f);
        text.rectTransform.offsetMax = new Vector2(-rightPadding, 0f);
        var ph = MakeText(rt, placeholder, size, Muted);
        Stretch(ph.rectTransform);
        ph.rectTransform.offsetMin = new Vector2(30f, 0f);
        ph.rectTransform.offsetMax = new Vector2(-rightPadding, 0f);
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }
}
