using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visualizes CRM Phase B fault detection output as a heatmap.
/// Row = observer robot, Col = observed robot.
/// Red = faulty (attack), Green = normal (tolerate), Grey = undecided or no data.
/// Feeds from FeatureExtractor.isFaulty — separate from the ML-based FaultHeatmap.
/// </summary>
public class CRMHeatmap : MonoBehaviour
{
    private const int   NUM_ROBOTS  = 10;
    private const float CELL_SIZE   = 29f;
    private const float HEADER_SIZE = 40f;
    private const float PADDING     = 10f;
    private const float PADDING_Y   = 50f;
    private const float FONT_SIZE   = 10f;

    // All FeatureExtractor components in the scene — one per robot
    private FeatureExtractor[] featureExtractors;

    // Grid cells and labels
    private Image[,] cells     = new Image[NUM_ROBOTS, NUM_ROBOTS];
    private Text[,]  cellTexts = new Text[NUM_ROBOTS, NUM_ROBOTS];

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
        float totalWidth  = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2;
        float totalHeight = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2 + 30f;

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

        float gridOffsetY = -PADDING - 30f;

        // Column headers
        for (int j = 0; j < NUM_ROBOTS; j++)
        {
            GameObject header = CreateUIObject($"ColHeader_CRM_{j}", panel);
            RectTransform r = header.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0, 1);
            r.anchorMax        = new Vector2(0, 1);
            r.pivot            = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(PADDING + HEADER_SIZE + j * CELL_SIZE, gridOffsetY);
            r.sizeDelta        = new Vector2(CELL_SIZE, HEADER_SIZE);
            Text t = header.AddComponent<Text>();
            t.text      = $"r{j}";
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = (int)FONT_SIZE;
            t.color     = Color.white;
            t.alignment = TextAnchor.LowerCenter;
        }

        // Row headers + cells
        for (int i = 0; i < NUM_ROBOTS; i++)
        {
            // Row header
            GameObject rowHeader = CreateUIObject($"RowHeader_CRM_{i}", panel);
            RectTransform rh = rowHeader.GetComponent<RectTransform>();
            rh.anchorMin        = new Vector2(0, 1);
            rh.anchorMax        = new Vector2(0, 1);
            rh.pivot            = new Vector2(0, 1);
            rh.anchoredPosition = new Vector2(PADDING, gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
            rh.sizeDelta        = new Vector2(HEADER_SIZE, CELL_SIZE);
            Text rht = rowHeader.AddComponent<Text>();
            rht.text      = $"r{i}";
            rht.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rht.fontSize  = (int)FONT_SIZE;
            rht.color     = Color.white;
            rht.alignment = TextAnchor.MiddleRight;

            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                GameObject cell = CreateUIObject($"Cell_CRM_{i}_{j}", panel);
                RectTransform cr = cell.GetComponent<RectTransform>();
                cr.anchorMin        = new Vector2(0, 1);
                cr.anchorMax        = new Vector2(0, 1);
                cr.pivot            = new Vector2(0, 1);
                cr.anchoredPosition = new Vector2(
                    PADDING + HEADER_SIZE + j * CELL_SIZE,
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

        for (int i = 0; i < featureExtractors.Length; i++)
        {
            FeatureExtractor fe = featureExtractors[i];
            if (fe == null || fe.isFaulty == null) continue;

            // derive observer index from robot name, not array position
            int observerIdx = int.Parse(new string(
                fe.transform.root.name.Where(char.IsDigit).ToArray()));

            if (observerIdx < 0 || observerIdx >= NUM_ROBOTS) continue;

            foreach (var kvp in fe.isFaulty)
            {
                GameObject observedRobot = kvp.Key;
                bool faulty              = kvp.Value;

                int j = int.Parse(new string(
                    observedRobot.transform.root.name.Where(char.IsDigit).ToArray()));

                if (j < 0 || j >= NUM_ROBOTS || observerIdx == j) continue;

                cells[observerIdx, j].color    = faulty ? Color.red : Color.green;
                cellTexts[observerIdx, j].text = faulty ? "F" : "N";
            }
        }
    }

    GameObject CreateUIObject(string name, GameObject parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }
}