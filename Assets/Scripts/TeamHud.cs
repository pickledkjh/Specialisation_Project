using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The 2v2 overlay: who is on which side, how much armour each unit has left,
/// a floating call-sign over every AI on screen, and the TAB prompt.
///
/// It is a separate canvas from BattleHUD on purpose. BattleHUD is the tuned
/// single-target layout (big armour number, boost, cooldown slots) and it is the
/// last thing that should get destabilised to add a mode. This sits above it and
/// switches itself off entirely outside a team battle.
/// </summary>
public class TeamHud : MonoBehaviour
{
    public static TeamHud Instance { get; private set; }

    private class Row
    {
        public GameObject go;
        public UiLabel name;
        public UiLabel hpNum;
        public RectTransform fill;
        public Image fillImg;
        public MechHealth unit;
    }

    private Canvas canvas;
    private RectTransform root;
    private readonly List<Row> allyRows = new List<Row>();
    private readonly List<Row> foeRows = new List<Row>();
    private readonly List<UiLabel> tags = new List<UiLabel>();
    private UiLabel tabHint;
    private MechHealth playerHealth;

    private const int MaxPerSide = 3;

    private void Awake()
    {
        Instance = this;
        Build();
        SetVisible(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public static TeamHud Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("Team HUD").AddComponent<TeamHud>();
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("Team HUD Canvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30; // above the battle HUD, below the game-flow panels
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        root = canvasGo.GetComponent<RectTransform>();

        Header("ALLIED", new Vector2(0f, 1f), new Vector2(28f, -120f), TeamBattleSetup.AllyColor, TextAnchor.MiddleLeft);
        Header("HOSTILE", new Vector2(1f, 1f), new Vector2(-28f, -120f), TeamBattleSetup.HostileColor, TextAnchor.MiddleRight);

        for (int i = 0; i < MaxPerSide; i++)
        {
            allyRows.Add(MakeRow(new Vector2(0f, 1f), new Vector2(28f, -152f - i * 40f), true, TeamBattleSetup.AllyColor));
            foeRows.Add(MakeRow(new Vector2(1f, 1f), new Vector2(-28f, -152f - i * 40f), false, TeamBattleSetup.HostileColor));
        }

        tabHint = UiLabel.Create(root, "Tab Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                 new Vector2(0f, 26f), new Vector2(700f, 26f), 17, true, TextAnchor.MiddleCenter);
        tabHint.color = new Color(0.75f, 0.85f, 1f, 0.9f);
        tabHint.text = "TAB  -  switch target";

        for (int i = 0; i < 4; i++)
        {
            UiLabel tag = UiLabel.Create(root, "Call Sign " + i, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                                         Vector2.zero, new Vector2(300f, 26f), 17, true, TextAnchor.MiddleCenter);
            tag.gameObject.SetActive(false);
            tags.Add(tag);
        }
    }

    private void Header(string text, Vector2 anchor, Vector2 pos, Color color, TextAnchor align)
    {
        UiLabel h = UiLabel.Create(root, "Hdr " + text, anchor, anchor, pos, new Vector2(300f, 26f), 18, true, align);
        h.color = color;
        h.text = text;
    }

    private Row MakeRow(Vector2 anchor, Vector2 pos, bool leftAligned, Color color)
    {
        Row row = new Row();
        row.go = new GameObject("Row");
        row.go.transform.SetParent(root, false);
        RectTransform rt = row.go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(leftAligned ? 0f : 1f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(330f, 34f);

        TextAnchor align = leftAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        Vector2 inner = new Vector2(leftAligned ? 0f : 1f, 1f);

        row.name = UiLabel.Create(row.go.transform, "Name", inner, inner,
                                  new Vector2(leftAligned ? 2f : -2f, 0f), new Vector2(250f, 20f), 16, true, align);
        row.name.color = color;

        // bar plate
        GameObject bg = new GameObject("Bar");
        bg.transform.SetParent(row.go.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.07f, 0.11f, 0.9f);
        bgImg.raycastTarget = false;
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(leftAligned ? 0f : 1f, 0f);
        bgRt.pivot = new Vector2(leftAligned ? 0f : 1f, 0f);
        bgRt.anchoredPosition = new Vector2(leftAligned ? 2f : -2f, 0f);
        bgRt.sizeDelta = new Vector2(250f, 10f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(bg.transform, false);
        row.fillImg = fill.AddComponent<Image>();
        row.fillImg.raycastTarget = false;
        row.fill = fill.GetComponent<RectTransform>();
        row.fill.anchorMin = Vector2.zero;
        row.fill.anchorMax = Vector2.one;
        row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;

        row.hpNum = UiLabel.Create(row.go.transform, "Num", new Vector2(leftAligned ? 1f : 0f, 1f),
                                   new Vector2(leftAligned ? 1f : 0f, 1f),
                                   new Vector2(leftAligned ? -4f : 4f, 0f), new Vector2(70f, 20f), 16, true,
                                   leftAligned ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        row.hpNum.color = Color.white;

        row.go.SetActive(false);
        return row;
    }

    public void SetVisible(bool on)
    {
        if (canvas != null) canvas.gameObject.SetActive(on);
    }

    private void LateUpdate()
    {
        if (canvas == null || !canvas.gameObject.activeSelf) return;
        if (!TeamRules.TeamModeActive) { SetVisible(false); return; }

        if (playerHealth == null)
        {
            MechController pc = FindFirstObjectByType<MechController>();
            if (pc != null) playerHealth = pc.GetComponent<MechHealth>();
        }
        Team myTeam = playerHealth != null ? playerHealth.team : Team.Team1;
        Team foeTeam = myTeam == Team.Team1 ? Team.Team2 : Team.Team1;

        List<MechHealth> all = BattleRoster.All;

        // The player is always the first allied row, so your own bar never moves.
        int a = 0, f = 0;
        if (playerHealth != null) BindRow(allyRows, ref a, playerHealth, "YOU");
        for (int i = 0; i < all.Count; i++)
        {
            MechHealth m = all[i];
            if (m == null || m == playerHealth) continue;
            if (m.team == myTeam) BindRow(allyRows, ref a, m, m.name);
            else if (m.team == foeTeam) BindRow(foeRows, ref f, m, m.name);
        }
        for (int i = a; i < allyRows.Count; i++) allyRows[i].go.SetActive(false);
        for (int i = f; i < foeRows.Count; i++) foeRows[i].go.SetActive(false);

        UpdateCallSigns(myTeam);
        UpdateTabHint(myTeam);
    }

    private void BindRow(List<Row> rows, ref int index, MechHealth unit, string label)
    {
        if (index >= rows.Count || unit == null) return;
        Row row = rows[index++];
        row.unit = unit;
        row.go.SetActive(true);

        float frac = unit.maxHealth > 0f ? Mathf.Clamp01(unit.currentHealth / unit.maxHealth) : 0f;
        bool dead = unit.currentHealth <= 0f;

        row.name.text = dead ? label + "   [ DESTROYED ]" : label;
        row.hpNum.text = Mathf.Max(0, Mathf.CeilToInt(unit.currentHealth)).ToString();
        row.hpNum.color = dead ? new Color(0.5f, 0.5f, 0.55f) : Color.white;

        row.fill.anchorMin = Vector2.zero;
        row.fill.anchorMax = new Vector2(frac, 1f);
        row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;
        row.fillImg.color = dead ? new Color(0.35f, 0.35f, 0.4f)
                          : frac > 0.5f ? new Color(0.4f, 0.9f, 0.55f)
                          : frac > 0.25f ? new Color(1f, 0.8f, 0.3f)
                                         : new Color(1f, 0.4f, 0.35f);
    }

    // Floating call-signs so you can tell your partner from a hostile at a glance
    // without reading the corner panels mid-fight.
    private void UpdateCallSigns(Team myTeam)
    {
        Camera cam = Camera.main;
        List<MechHealth> all = BattleRoster.All;
        Transform locked = TargetSwitcher.Instance != null ? TargetSwitcher.Instance.Current : null;
        MechHealth lockedHealth = TeamRules.FindHealth(locked);

        int t = 0;
        if (cam != null)
        {
            for (int i = 0; i < all.Count && t < tags.Count; i++)
            {
                MechHealth m = all[i];
                if (m == null || m == playerHealth || m.currentHealth <= 0f) continue;

                Vector3 world = m.transform.position + Vector3.up * 4.2f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;

                UiLabel tag = tags[t++];
                bool ally = m.team == myTeam;
                bool isTarget = lockedHealth == m;
                tag.gameObject.SetActive(true);
                tag.text = isTarget ? "> " + m.name + " <" : m.name;
                tag.color = ally ? TeamBattleSetup.AllyColor
                          : isTarget ? new Color(1f, 0.9f, 0.35f)
                                     : TeamBattleSetup.HostileColor;

                RectTransform rt = tag.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                float sx = root.rect.width / Mathf.Max(1f, Screen.width);
                float sy = root.rect.height / Mathf.Max(1f, Screen.height);
                rt.anchoredPosition = new Vector2(sp.x * sx, sp.y * sy);
            }
        }
        for (int i = t; i < tags.Count; i++) tags[i].gameObject.SetActive(false);
    }

    private void UpdateTabHint(Team myTeam)
    {
        if (tabHint == null) return;
        int foes = BattleRoster.Opponents(myTeam, transform.position).Count;
        tabHint.gameObject.SetActive(foes > 1);
        if (foes <= 1) return;

        // flash briefly right after a switch so the input reads as registered
        float since = TargetSwitcher.Instance != null ? Time.time - TargetSwitcher.Instance.LastSwitchAt : 99f;
        tabHint.color = since < 0.35f
            ? new Color(1f, 0.9f, 0.4f)
            : new Color(0.75f, 0.85f, 1f, 0.9f);
        tabHint.text = since < 0.35f ? "TARGET SWITCHED" : "TAB  -  switch target   (" + foes + " hostiles)";
    }
}
