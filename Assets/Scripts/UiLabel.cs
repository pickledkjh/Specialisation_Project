using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Crisp UI text for the HUD and tutorial. Uses TextMeshPro when its essential
/// resources are imported (Window > TextMeshPro > Import TMP Essential Resources),
/// otherwise falls back to legacy Text rendered at double size and scaled down,
/// which kills most of the blur. Import TMP and every label upgrades automatically.
/// </summary>
public class UiLabel
{
    private Text legacy;
    private TMP_Text tmp;

    public GameObject gameObject { get; private set; }
    public RectTransform rectTransform { get; private set; }

    public string text
    {
        get { return tmp != null ? tmp.text : legacy != null ? legacy.text : ""; }
        set { if (tmp != null) tmp.text = value; else if (legacy != null) legacy.text = value; }
    }

    public Color color
    {
        set { if (tmp != null) tmp.color = value; else if (legacy != null) legacy.color = value; }
    }

    /// <summary>True once TMP's essential resources exist in the project.</summary>
    public static bool TmpReady
    {
        get
        {
            try { return TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null; }
            catch { return false; }
        }
    }

    public static UiLabel Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
                                 Vector2 pos, Vector2 size, int fontSize, bool bold, TextAnchor align)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        UiLabel label = new UiLabel { gameObject = go };

        if (TmpReady)
        {
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.alignment = MapAlign(align);
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            label.tmp = t;
        }
        else
        {
            Text t = go.AddComponent<Text>();
            t.font = LoadBuiltinFont();
            t.fontSize = fontSize * 2;               // render big...
            go.transform.localScale = Vector3.one * 0.5f; // ...display small = sharp
            size *= 2f;                               // keep the same on-screen box
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = align;
            t.color = Color.white;
            label.legacy = t;
        }

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        label.rectTransform = rt;
        return label;
    }

    private static TextAlignmentOptions MapAlign(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            default: return TextAlignmentOptions.Center;
        }
    }

    public static Font LoadBuiltinFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("LegacySans.ttf"); } catch { } }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }
}
