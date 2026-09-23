using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dummy BFV sim (dummy_bfv_sim_brief.md, Section 9) — copy/parallel of HeatmapDanger.cs,
/// visualizing DummyBFVSwarm's output instead of the live DangerCascadeController fleet's. Same
/// grid layout (row = observer robot, col = observed robot), same cell-coloring convention (red =
/// faulty, orange = under investigation, green = cleared/normal), same BFV-bits/index + ladder
/// state text per cell. Sized to the swarm's actual (inspector-configured) robot count instead of
/// a fixed NUM_ROBOTS. Original HeatmapDanger.cs is untouched.
/// </summary>
[ExecuteAlways]
public class DummyHeatmapDanger : MonoBehaviour
{
    private const float CELL_SIZE       = 50f;
    private const float HEADER_SIZE     = 60f;
    private const float COL_LABEL_ROW_HEIGHT = 14f;
    private const float ROW_LABEL_WIDTH = 70f;
    private const float ROW_NUM_COL_WIDTH = 20f;
    private const float PADDING         = 10f;
    private const float PADDING_X       = 0f;
    private const float PADDING_Y       = 130f;
    private const float FONT_SIZE       = 9f;
    private const float TITLE_ROW_HEIGHT = 30f;
    private const float TOP5_ROW_HEIGHT  = 18f;
    private const int   TOP5_COUNT       = 5;
    private const int   ROW_TOP_COUNT    = 3;
    private const int   COL_TOP_COUNT    = 3;

    public DummyBFVSwarm swarm;

    private int numRobots;

    private Image[,] cells;
    private Text[,]  cellTexts;
    private int[,]   cellFvIndex; // decimal FV index currently shown in each cell (-1 = no data)

    private Text top5Text;
    private Text[] rowBfvTexts;
    private Text[] colBfvTexts;

    public bool start = false;

    // In Play mode, sizing needs the swarm's actual robot count, which DummyBFVSwarm.Awake()
    // only populates once Play starts — Unity guarantees all Awake calls finish before any
    // Start, so Start() (not OnEnable) is what does the real build here.
    void Start()
    {
        if (swarm == null)
            swarm = FindObjectOfType<DummyBFVSwarm>();
        if (swarm == null)
        {
            Debug.LogError("DummyHeatmapDanger found no DummyBFVSwarm in the scene.");
            return;
        }

        numRobots = swarm.robots.Count;
        RebuildPanel();
    }

    // Edit mode has no Awake-populated swarm.robots to size off of, so this previews using the
    // sum of the inspector-configured bfvGroups.robotCount instead — same total BuildSwarm()
    // would produce, without needing to press Play. Skips (leaves any existing panel alone) if
    // that can't be determined yet, e.g. no swarm assigned or no groups configured.
    void OnEnable()
    {
        if (Application.isPlaying) return;

        if (swarm == null)
            swarm = FindObjectOfType<DummyBFVSwarm>();
        if (swarm == null) return;

        int previewCount = swarm.robots != null && swarm.robots.Count > 0
            ? swarm.robots.Count
            : SumGroupRobotCounts(swarm);
        if (previewCount <= 0) return;

        numRobots = previewCount;
        RebuildPanel();
    }

    static int SumGroupRobotCounts(DummyBFVSwarm s)
    {
        int total = 0;
        if (s.bfvGroups != null)
            foreach (var g in s.bfvGroups)
                total += g.robotCount;
        return total;
    }

    // Always tears down and rebuilds Panel_DummyDanger from scratch, so editing
    // PADDING_X/PADDING_Y/CELL_SIZE/etc., or the swarm's robot count, and toggling this
    // component off/on (or re-entering Play mode) always reflects the latest values instead of
    // leaving a stale panel in place.
    void RebuildPanel()
    {
        cells       = new Image[numRobots, numRobots];
        cellTexts   = new Text[numRobots, numRobots];
        cellFvIndex = new int[numRobots, numRobots];
        rowBfvTexts = new Text[numRobots];
        colBfvTexts = new Text[numRobots];

        Transform existing = transform.Find("Panel_DummyDanger");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else                        DestroyImmediate(existing.gameObject);
        }

        BuildCanvas();
    }

    void BuildCanvas()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
        }
        if (GetComponent<CanvasScaler>() == null)
            gameObject.AddComponent<CanvasScaler>();
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        BuildGrid();
    }

    void BuildGrid()
    {
        float totalWidth  = ROW_LABEL_WIDTH + numRobots * CELL_SIZE + PADDING * 2;
        float totalHeight = HEADER_SIZE + numRobots * CELL_SIZE + PADDING * 2 + TITLE_ROW_HEIGHT + TOP5_ROW_HEIGHT;

        GameObject panel = CreateUIObject("Panel_DummyDanger", gameObject);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0f);
        panelRect.anchorMax        = new Vector2(0.5f, 0f);
        panelRect.pivot            = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(PADDING_X, PADDING_Y);
        panelRect.sizeDelta        = new Vector2(totalWidth, totalHeight);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        GameObject title = CreateUIObject("Title_DummyDanger", panel);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0, 1);
        titleRect.anchorMax        = new Vector2(1, 1);
        titleRect.pivot            = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -PADDING);
        titleRect.sizeDelta        = new Vector2(0, 25f);
        Text titleText = title.AddComponent<Text>();
        titleText.text      = "Dummy BFV Danger CRM  (row=observer, col=observed)";
        titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize  = 11;
        titleText.color     = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;

        GameObject top5 = CreateUIObject("Top5_DummyDanger", panel);
        RectTransform top5Rect = top5.GetComponent<RectTransform>();
        top5Rect.anchorMin        = new Vector2(0, 1);
        top5Rect.anchorMax        = new Vector2(1, 1);
        top5Rect.pivot            = new Vector2(0.5f, 1);
        top5Rect.anchoredPosition = new Vector2(0, -PADDING - TITLE_ROW_HEIGHT);
        top5Rect.sizeDelta        = new Vector2(0, TOP5_ROW_HEIGHT);
        top5Text = top5.AddComponent<Text>();
        top5Text.text      = "Top BFV: —";
        top5Text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        top5Text.fontSize  = 10;
        top5Text.color     = Color.cyan;
        top5Text.alignment = TextAnchor.MiddleCenter;
        top5Text.horizontalOverflow = HorizontalWrapMode.Overflow;

        float gridOffsetY = -PADDING - TITLE_ROW_HEIGHT - TOP5_ROW_HEIGHT;

        for (int j = 0; j < numRobots; j++)
        {
            GameObject header = CreateUIObject($"ColHeader_DummyDanger_{j}", panel);
            RectTransform r = header.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0, 1);
            r.anchorMax        = new Vector2(0, 1);
            r.pivot            = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(PADDING + ROW_LABEL_WIDTH + j * CELL_SIZE, gridOffsetY);
            r.sizeDelta        = new Vector2(CELL_SIZE, COL_LABEL_ROW_HEIGHT);
            Text t = header.AddComponent<Text>();
            t.text      = $"r{j}";
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = (int)FONT_SIZE;
            t.color     = Color.white;
            t.alignment = TextAnchor.MiddleCenter;

            GameObject colBfv = CreateUIObject($"ColBFV_DummyDanger_{j}", panel);
            RectTransform cb = colBfv.GetComponent<RectTransform>();
            cb.anchorMin        = new Vector2(0, 1);
            cb.anchorMax        = new Vector2(0, 1);
            cb.pivot            = new Vector2(0, 1);
            cb.anchoredPosition = new Vector2(PADDING + ROW_LABEL_WIDTH + j * CELL_SIZE, gridOffsetY - COL_LABEL_ROW_HEIGHT);
            cb.sizeDelta        = new Vector2(CELL_SIZE, HEADER_SIZE - COL_LABEL_ROW_HEIGHT);
            Text cbt = colBfv.AddComponent<Text>();
            cbt.text      = "";
            cbt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cbt.fontSize  = (int)FONT_SIZE;
            cbt.resizeTextForBestFit = true;
            cbt.resizeTextMinSize    = 4;
            cbt.resizeTextMaxSize    = (int)FONT_SIZE;
            cbt.color     = Color.yellow;
            cbt.alignment = TextAnchor.UpperCenter;
            colBfvTexts[j] = cbt;
        }

        for (int i = 0; i < numRobots; i++)
        {
            GameObject rowLabel = CreateUIObject($"RowLabel_DummyDanger_{i}", panel);
            RectTransform rl = rowLabel.GetComponent<RectTransform>();
            rl.anchorMin        = new Vector2(0, 1);
            rl.anchorMax        = new Vector2(0, 1);
            rl.pivot            = new Vector2(0, 1);
            rl.anchoredPosition = new Vector2(PADDING, gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
            rl.sizeDelta        = new Vector2(ROW_NUM_COL_WIDTH, CELL_SIZE);
            Text rlt = rowLabel.AddComponent<Text>();
            rlt.text      = $"r{i}";
            rlt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rlt.fontSize  = (int)FONT_SIZE;
            rlt.resizeTextForBestFit = true;
            rlt.resizeTextMinSize    = 4;
            rlt.resizeTextMaxSize    = (int)FONT_SIZE;
            rlt.color     = Color.white;
            rlt.alignment = TextAnchor.MiddleLeft;

            GameObject rowBfv = CreateUIObject($"RowBFV_DummyDanger_{i}", panel);
            RectTransform rb = rowBfv.GetComponent<RectTransform>();
            rb.anchorMin        = new Vector2(0, 1);
            rb.anchorMax        = new Vector2(0, 1);
            rb.pivot            = new Vector2(0, 1);
            rb.anchoredPosition = new Vector2(PADDING + ROW_NUM_COL_WIDTH, gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
            rb.sizeDelta        = new Vector2(ROW_LABEL_WIDTH - ROW_NUM_COL_WIDTH, CELL_SIZE);
            Text rbt = rowBfv.AddComponent<Text>();
            rbt.text      = "";
            rbt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rbt.fontSize  = (int)FONT_SIZE;
            rbt.resizeTextForBestFit = true;
            rbt.resizeTextMinSize    = 4;
            rbt.resizeTextMaxSize    = (int)FONT_SIZE;
            rbt.color     = Color.yellow;
            rbt.alignment = TextAnchor.UpperLeft;
            rowBfvTexts[i] = rbt;

            for (int j = 0; j < numRobots; j++)
            {
                GameObject cell = CreateUIObject($"Cell_DummyDanger_{i}_{j}", panel);
                RectTransform cr = cell.GetComponent<RectTransform>();
                cr.anchorMin        = new Vector2(0, 1);
                cr.anchorMax        = new Vector2(0, 1);
                cr.pivot            = new Vector2(0, 1);
                cr.anchoredPosition = new Vector2(
                    PADDING + ROW_LABEL_WIDTH + j * CELL_SIZE,
                    gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
                cr.sizeDelta = new Vector2(CELL_SIZE - 2f, CELL_SIZE - 2f);

                Image img = cell.AddComponent<Image>();
                img.color = (i == j) ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.25f, 0.25f, 0.25f);
                cells[i, j] = img;

                GameObject textObj = CreateUIObject($"Text_DummyDanger_{i}_{j}", cell);
                RectTransform tr = textObj.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.sizeDelta = Vector2.zero;
                Text ct = textObj.AddComponent<Text>();
                ct.text      = (i == j) ? "—" : "";
                ct.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ct.fontSize  = (int)FONT_SIZE;
                ct.resizeTextForBestFit = true;
                ct.resizeTextMinSize    = 4;
                ct.resizeTextMaxSize    = (int)FONT_SIZE;
                ct.color     = Color.white;
                ct.alignment = TextAnchor.MiddleCenter;
                cellTexts[i, j] = ct;
            }
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!start || swarm == null || cells == null) return;

        for (int i = 0; i < numRobots; i++)
            for (int j = 0; j < numRobots; j++)
                cellFvIndex[i, j] = -1;

        foreach (var observer in swarm.robots)
        {
            int i = observer.id;
            if (i < 0 || i >= numRobots) continue;

            foreach (var target in swarm.robots)
            {
                int j = target.id;
                if (j < 0 || j >= numRobots || i == j) continue;

                int fvIndex = target.currentBFV;
                // F1 first (bit0), matching HeatmapDanger's own "$"{F1}{F2}{F3}{F4}{F5}{F6}""
                // display order — NOT standard MSB-first binary notation.
                string bits = $"{fvIndex & 1}{(fvIndex >> 1) & 1}{(fvIndex >> 2) & 1}" +
                              $"{(fvIndex >> 3) & 1}{(fvIndex >> 4) & 1}{(fvIndex >> 5) & 1}";

                bool hasState = observer.states.TryGetValue(j, out var state);

                if (observer.verdicts.TryGetValue(j, out bool faulty))
                {
                    if (faulty)
                        cells[i, j].color = Color.red;
                    else if (hasState && IsUnderInvestigation(state))
                        cells[i, j].color = new Color(1.0f, 0.6f, 0.0f, 1f); // orange
                    else
                        cells[i, j].color = new Color(0.3f, 0.8f, 0.3f, 1f); // green
                }

                string stateText = hasState ? state.ToString() : "";
                cellTexts[i, j].text = $"{bits}\n{fvIndex}\n{stateText}";
                cellFvIndex[i, j]    = fvIndex;
            }
        }

        UpdateTop5();
        UpdateRowTopBFVs();
        UpdateColTopBFVs();
    }

    void UpdateColTopBFVs()
    {
        for (int j = 0; j < numRobots; j++)
        {
            Dictionary<int, int> colTally = new();
            for (int i = 0; i < numRobots; i++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;
                colTally.TryGetValue(fv, out int count);
                colTally[fv] = count + 1;
            }

            if (colTally.Count == 0) { colBfvTexts[j].text = ""; continue; }

            var top = colTally.OrderByDescending(kvp => kvp.Value).Take(COL_TOP_COUNT);
            colBfvTexts[j].text = string.Join("\n", top.Select(kvp => $"{kvp.Key}x{kvp.Value}"));
        }
    }

    void UpdateRowTopBFVs()
    {
        for (int i = 0; i < numRobots; i++)
        {
            Dictionary<int, int> rowTally = new();
            for (int j = 0; j < numRobots; j++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;
                rowTally.TryGetValue(fv, out int count);
                rowTally[fv] = count + 1;
            }

            if (rowTally.Count == 0) { rowBfvTexts[i].text = ""; continue; }

            var top = rowTally.OrderByDescending(kvp => kvp.Value).Take(ROW_TOP_COUNT);
            rowBfvTexts[i].text = string.Join("\n", top.Select(kvp => $"{kvp.Key}x{kvp.Value}"));
        }
    }

    void UpdateTop5()
    {
        Dictionary<int, int> fvTally = new();
        for (int i = 0; i < numRobots; i++)
            for (int j = 0; j < numRobots; j++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;
                fvTally.TryGetValue(fv, out int count);
                fvTally[fv] = count + 1;
            }

        if (fvTally.Count == 0) { top5Text.text = "Top BFV: —"; return; }

        var top5 = fvTally.OrderByDescending(kvp => kvp.Value).Take(TOP5_COUNT);
        top5Text.text = "Top BFV: " + string.Join("   ", top5.Select(kvp => $"{kvp.Key} (x{kvp.Value})"));
    }

    static bool IsUnderInvestigation(DangerCascadeController.RobotFaultState state)
    {
        return state == DangerCascadeController.RobotFaultState.PRIMED
            || state == DangerCascadeController.RobotFaultState.DISCREPANCY_PASSED
            || state == DangerCascadeController.RobotFaultState.PROBING;
    }

    GameObject CreateUIObject(string name, GameObject parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }
}
