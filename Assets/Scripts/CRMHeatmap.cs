using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visualizes CRM Phase B fault detection output as a heatmap.
/// Row = observer robot, Col = observed robot.
/// Cell color: Red = faulty (attack), Green = normal (tolerate), Grey = undecided or no data.
/// Cell text: the observer's latest binary feature vector for the observed robot — shown as
/// bits (F1F2F3F4F5F6) on the first line and the equivalent decimal FV index on the second,
/// i.e. the same value CRM classifies on (see FeatureExtractor.fvIndex).
/// Feeds from FeatureExtractor.isFaulty / feature_list_history — separate from the ML-based FaultHeatmap.
/// </summary>
public class CRMHeatmap : MonoBehaviour
{
    private const int   NUM_ROBOTS      = 10;
    private const float CELL_SIZE       = 50f;
    private const float HEADER_SIZE     = 60f;
    private const float COL_LABEL_ROW_HEIGHT = 14f;
    private const float ROW_LABEL_WIDTH = 70f;
    private const float ROW_NUM_COL_WIDTH = 20f;
    private const float PADDING         = 10f;
    private const float PADDING_Y       = 130f;
    private const float FONT_SIZE       = 9f;
    private const float TITLE_ROW_HEIGHT = 30f;
    private const float TOP5_ROW_HEIGHT  = 18f;
    private const int   TOP5_COUNT       = 5;
    private const int   ROW_TOP_COUNT    = 3;
    private const int   COL_TOP_COUNT    = 3;

    // All FeatureExtractor components in the scene — one per robot
    private FeatureExtractor[] featureExtractors;

    // Grid cells and labels
    private Image[,] cells     = new Image[NUM_ROBOTS, NUM_ROBOTS];
    private Text[,]  cellTexts = new Text[NUM_ROBOTS, NUM_ROBOTS];

    // Decimal FV index currently shown in each cell (-1 = no data), used to tally top BFVs
    private int[,] cellFvIndex = new int[NUM_ROBOTS, NUM_ROBOTS];

    // "Top 5 most popular active BFV" readout, shown below the title
    private Text top5Text;

    // Per-row readout (2nd column, next to the row number) — each observer's own top BFVs
    // across everything it currently observes
    private Text[] rowBfvTexts = new Text[NUM_ROBOTS];

    // Per-column readout (extra lines below the column label) — top BFVs that robot is seen
    // exhibiting, across every observer currently watching it
    private Text[] colBfvTexts = new Text[NUM_ROBOTS];

    public bool start = false;

    void Start()
    {
        featureExtractors = FindObjectsOfType<FeatureExtractor>();

        // Sort by robot index so array position matches robot ID
        System.Array.Sort(featureExtractors, (a, b) =>
            int.Parse(new string(a.transform.root.name.Where(char.IsDigit).ToArray()))
            .CompareTo(int.Parse(new string(b.transform.root.name.Where(char.IsDigit).ToArray()))));

        BuildCanvas();
    }

    void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        BuildGrid();
    }

    void BuildGrid()
    {
        float totalWidth  = ROW_LABEL_WIDTH + NUM_ROBOTS * CELL_SIZE + PADDING * 2;
        float totalHeight = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2 + TITLE_ROW_HEIGHT + TOP5_ROW_HEIGHT;

        // Background panel — positioned at bottom center of screen
        GameObject panel = CreateUIObject("Panel_CRM", gameObject);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = new Vector2(0.5f, 0f);
        panelRect.anchorMax        = new Vector2(0.5f, 0f);
        panelRect.pivot            = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, PADDING_Y);
        panelRect.sizeDelta        = new Vector2(totalWidth, totalHeight);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        // Title
        GameObject title = CreateUIObject("Title_CRM", panel);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0, 1);
        titleRect.anchorMax        = new Vector2(1, 1);
        titleRect.pivot            = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -PADDING);
        titleRect.sizeDelta        = new Vector2(0, 25f);
        Text titleText = title.AddComponent<Text>();
        titleText.text      = "CRM  (row=observer, col=observed)";
        titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize  = 11;
        titleText.color     = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;

        // Top 5 most popular active BFV — shown right below the title
        GameObject top5 = CreateUIObject("Top5_CRM", panel);
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

        // Column headers — label line (static) + BFV lines below it (updated live)
        for (int j = 0; j < NUM_ROBOTS; j++)
        {
            GameObject header = CreateUIObject($"ColHeader_CRM_{j}", panel);
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

            GameObject colBfv = CreateUIObject($"ColBFV_CRM_{j}", panel);
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
            cbt.color     = Color.yellow; // matches the fleet-wide Top BFV readout
            cbt.alignment = TextAnchor.UpperCenter;
            colBfvTexts[j] = cbt;
        }

        // Row headers + cells
        for (int i = 0; i < NUM_ROBOTS; i++)
        {
            // Row header — column 1: robot number (static)
            GameObject rowLabel = CreateUIObject($"RowLabel_CRM_{i}", panel);
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

            // Row header — column 2: this observer's own top BFVs (updated live)
            GameObject rowBfv = CreateUIObject($"RowBFV_CRM_{i}", panel);
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
            rbt.color     = Color.yellow; // matches the fleet-wide Top BFV readout
            rbt.alignment = TextAnchor.UpperLeft;
            rowBfvTexts[i] = rbt;

            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                GameObject cell = CreateUIObject($"Cell_CRM_{i}_{j}", panel);
                RectTransform cr = cell.GetComponent<RectTransform>();
                cr.anchorMin        = new Vector2(0, 1);
                cr.anchorMax        = new Vector2(0, 1);
                cr.pivot            = new Vector2(0, 1);
                cr.anchoredPosition = new Vector2(
                    PADDING + ROW_LABEL_WIDTH + j * CELL_SIZE,
                    gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
                cr.sizeDelta = new Vector2(CELL_SIZE - 2f, CELL_SIZE - 2f);

                Image img = cell.AddComponent<Image>();
                // Diagonal = self, grey
                img.color  = (i == j) ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.25f, 0.25f, 0.25f);
                cells[i, j] = img;

                GameObject textObj = CreateUIObject($"Text_CRM_{i}_{j}", cell);
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
        if (!start)
        {
            featureExtractors = FindObjectsOfType<FeatureExtractor>();
            return;
        }

        // reset — stale entries (robot left view, etc.) shouldn't count toward the tally
        for (int i = 0; i < NUM_ROBOTS; i++)
            for (int j = 0; j < NUM_ROBOTS; j++)
                cellFvIndex[i, j] = -1;

        for (int i = 0; i < featureExtractors.Length; i++)
        {
            FeatureExtractor fe = featureExtractors[i];
            if (fe == null || fe.isFaulty == null) continue;

            // derive observer index from robot name, not array position
            int observerIdx = int.Parse(new string(
                fe.transform.root.name.Where(char.IsDigit).ToArray()));

            if (observerIdx < 0 || observerIdx >= NUM_ROBOTS) continue;

            foreach (var kvp in fe.feature_list_history)
            {
                GameObject observedRobot = kvp.Key;
                var samples              = kvp.Value;
                if (samples == null || samples.Count == 0) continue;

                int j = int.Parse(new string(
                    observedRobot.transform.root.name.Where(char.IsDigit).ToArray()));

                if (j < 0 || j >= NUM_ROBOTS || observerIdx == j) continue;

                var latest = samples[^1];

                // same 6-bit binary FV that CRM classifies on
                string bits = $"{latest.F1}{latest.F2}{latest.F3}{latest.F4}{latest.F5}{latest.F6}";
                int fvIndex = (latest.F1 << 0) | (latest.F2 << 1) | (latest.F3 << 2) |
                              (latest.F4 << 3) | (latest.F5 << 4) | (latest.F6 << 5);

                if (fe.isFaulty.TryGetValue(observedRobot, out bool faulty))
                    // cells[observerIdx, j].color = faulty ? Color.red : Color.green;
                    cells[observerIdx, j].color = faulty ? Color.red : new Color(0.3f, 0.8f, 0.3f, 1f);

                cellTexts[observerIdx, j].text = $"{bits}\n{fvIndex}";
                cellFvIndex[observerIdx, j]    = fvIndex;
            }
        }

        UpdateTop5();
        UpdateRowTopBFVs();
        UpdateColTopBFVs();
    }

    // Per column (observed robot), tally the FV indices every observer currently sees it
    // exhibiting, and show the top few as extra lines below the column label.
    void UpdateColTopBFVs()
    {
        for (int j = 0; j < NUM_ROBOTS; j++)
        {
            Dictionary<int, int> colTally = new();

            for (int i = 0; i < NUM_ROBOTS; i++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;

                colTally.TryGetValue(fv, out int count);
                colTally[fv] = count + 1;
            }

            if (colTally.Count == 0)
            {
                colBfvTexts[j].text = "";
                continue;
            }

            var top = colTally.OrderByDescending(kvp => kvp.Value).Take(COL_TOP_COUNT);
            colBfvTexts[j].text = string.Join("\n", top.Select(kvp => $"{kvp.Key}x{kvp.Value}"));
        }
    }

    // Per row (observer), tally the FV indices it currently sees across all robots it's
    // observing, and show its own top few next to the row label.
    void UpdateRowTopBFVs()
    {
        for (int i = 0; i < NUM_ROBOTS; i++)
        {
            Dictionary<int, int> rowTally = new();

            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;

                rowTally.TryGetValue(fv, out int count);
                rowTally[fv] = count + 1;
            }

            if (rowTally.Count == 0)
            {
                rowBfvTexts[i].text = "";
                continue;
            }

            var top = rowTally.OrderByDescending(kvp => kvp.Value).Take(ROW_TOP_COUNT);
            rowBfvTexts[i].text = string.Join("\n", top.Select(kvp => $"{kvp.Key}x{kvp.Value}"));
        }
    }

    // Tally the FV index currently shown in every cell and surface the most common ones —
    // i.e. which behavioral feature vectors the fleet is observing most right now.
    void UpdateTop5()
    {
        Dictionary<int, int> fvTally = new();

        for (int i = 0; i < NUM_ROBOTS; i++)
        {
            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                int fv = cellFvIndex[i, j];
                if (fv < 0) continue;

                fvTally.TryGetValue(fv, out int count);
                fvTally[fv] = count + 1;
            }
        }

        if (fvTally.Count == 0)
        {
            top5Text.text = "Top BFV: —";
            return;
        }

        var top5 = fvTally.OrderByDescending(kvp => kvp.Value).Take(TOP5_COUNT);
        top5Text.text = "Top BFV: " + string.Join("   ", top5.Select(kvp => $"{kvp.Key} (x{kvp.Value})"));
    }

    GameObject CreateUIObject(string name, GameObject parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }
}