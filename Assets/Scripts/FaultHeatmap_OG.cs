using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class FaultHeatmap_OG : MonoBehaviour
{
    private const int NUM_ROBOTS = 10;
    private const float CELL_SIZE = 40f;
    private const float HEADER_SIZE = 50f;
    private const float PADDING = 10f;
    private const float FONT_SIZE = 10f;

    private FaultMemory[] faultMemories;
    private Image[,] cells = new Image[NUM_ROBOTS, NUM_ROBOTS];
    private Text[,] cellTexts = new Text[NUM_ROBOTS, NUM_ROBOTS];

    public bool start = false;

    void Start()
    {
        // Find all FaultMemory components in scene
        faultMemories = FindObjectsOfType<FaultMemory>();
        Debug.Log($"Found {faultMemories.Length} FaultMemory components");
        System.Array.Sort(faultMemories, (a, b) =>
            int.Parse(new string(a.name.Where(char.IsDigit).ToArray()))
            .CompareTo(int.Parse(new string(b.name.Where(char.IsDigit).ToArray()))));

        BuildHeatmap();
    }

    void BuildHeatmap()
    {
        float totalWidth  = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2;
        float totalHeight = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2 + 30f; // 30 for title

        // Canvas
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        // Background panel
        GameObject panel = CreateUIObject("Panel", gameObject);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot     = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-PADDING, -PADDING);
        panelRect.sizeDelta = new Vector2(totalWidth, totalHeight);

        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        // Title
        GameObject title = CreateUIObject("Title", panel);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0, 1);
        titleRect.anchorMax        = new Vector2(1, 1);
        titleRect.pivot            = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -PADDING);
        titleRect.sizeDelta        = new Vector2(0, 25f);
        Text titleText = title.AddComponent<Text>();
        titleText.text      = "Fault Detection Heatmap  (row = observer, col = observed)";
        titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize  = 11;
        titleText.color     = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;

        float gridOffsetY = -PADDING - 30f;

        // Column headers (observed)
        for (int j = 0; j < NUM_ROBOTS; j++)
        {
            GameObject header = CreateUIObject($"ColHeader_{j}", panel);
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

        // Row headers (observer) + cells
        for (int i = 0; i < NUM_ROBOTS; i++)
        {
            // Row header
            GameObject rowHeader = CreateUIObject($"RowHeader_{i}", panel);
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

            // Cells
            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                GameObject cell = CreateUIObject($"Cell_{i}_{j}", panel);
                RectTransform cr = cell.GetComponent<RectTransform>();
                cr.anchorMin        = new Vector2(0, 1);
                cr.anchorMax        = new Vector2(0, 1);
                cr.pivot            = new Vector2(0, 1);
                cr.anchoredPosition = new Vector2(
                    PADDING + HEADER_SIZE + j * CELL_SIZE,
                    gridOffsetY - HEADER_SIZE - i * CELL_SIZE);
                cr.sizeDelta = new Vector2(CELL_SIZE - 2f, CELL_SIZE - 2f);

                Image img = cell.AddComponent<Image>();
                img.color = (i == j) ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.25f, 0.25f, 0.25f);
                cells[i, j] = img;

                // Cell text
                GameObject textObj = CreateUIObject($"Text_{i}_{j}", cell);
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
            faultMemories = FindObjectsOfType<FaultMemory>();
            return;
        }

        foreach (FaultMemory fm in faultMemories)
        {
            int observerId = int.Parse(new string(fm.name.Where(char.IsDigit).ToArray()));
            if (observerId < 0 || observerId >= NUM_ROBOTS) continue;

            for (int j = 0; j < NUM_ROBOTS; j++)
            {
                if (observerId == j) continue;
                float prob = fm.GetLatestProbability(j);
                if (prob < 0f) continue;
                // cells[observerId, j].color    = ProbToColor(prob);
                int pred = fm.GetLatestPrediction(j);
                cells[observerId, j].color    = pred == 1 ? Color.red : Color.green;
                cellTexts[observerId, j].text = prob.ToString("F2");
            }
        }
    }

    Color ProbToColor(float p)
    {
        if (p < 0.5f)
            return Color.Lerp(Color.green, Color.yellow, p * 2f);
        else
            return Color.Lerp(Color.yellow, Color.red, (p - 0.5f) * 2f);
    }

    GameObject CreateUIObject(string name, GameObject parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }
}