using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class FaultHeatmap : MonoBehaviour
{
    private const int NUM_ROBOTS = 10;
    private const float CELL_SIZE = 29f;
    private const float HEADER_SIZE = 40f;
    private const float PADDING = 10f;
    private const float PADDING_Y = 50f;
    private const float FONT_SIZE = 10f;

    private FaultMemory[] faultMemories;

    private Image[,] cellsGBDT = new Image[NUM_ROBOTS, NUM_ROBOTS];
    private Text[,]  cellTextsGBDT = new Text[NUM_ROBOTS, NUM_ROBOTS];
    private Image[,] cellsLSTM = new Image[NUM_ROBOTS, NUM_ROBOTS];
    private Text[,]  cellTextsLSTM = new Text[NUM_ROBOTS, NUM_ROBOTS];

    public bool start = false;

    void Start()
    {
        faultMemories = FindObjectsOfType<FaultMemory>();
        System.Array.Sort(faultMemories, (a, b) =>
            int.Parse(new string(a.name.Where(char.IsDigit).ToArray()))
            .CompareTo(int.Parse(new string(b.name.Where(char.IsDigit).ToArray()))));

        BuildCanvas();
    }

    void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        BuildGrid("GBDT", ref cellsGBDT, ref cellTextsGBDT, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(PADDING, -PADDING_Y));
        BuildGrid("LSTM", ref cellsLSTM, ref cellTextsLSTM, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-PADDING, -PADDING_Y));
    }

    void BuildGrid(string label, ref Image[,] cells, ref Text[,] cellTexts,
                   Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position)
    {
        float totalWidth  = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2;
        float totalHeight = HEADER_SIZE + NUM_ROBOTS * CELL_SIZE + PADDING * 2 + 30f;

        // Background panel
        GameObject panel = CreateUIObject($"Panel_{label}", gameObject);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin        = anchorMin;
        panelRect.anchorMax        = anchorMax;
        panelRect.pivot            = pivot;
        panelRect.anchoredPosition = position;
        panelRect.sizeDelta        = new Vector2(totalWidth, totalHeight);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        // Title
        GameObject title = CreateUIObject($"Title_{label}", panel);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin        = new Vector2(0, 1);
        titleRect.anchorMax        = new Vector2(1, 1);
        titleRect.pivot            = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -PADDING);
        titleRect.sizeDelta        = new Vector2(0, 25f);
        Text titleText = title.AddComponent<Text>();
        titleText.text      = $"{label}  (row=observer, col=observed)";
        titleText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize  = 11;
        titleText.color     = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;

        float gridOffsetY = -PADDING - 30f;

        // Column headers
        for (int j = 0; j < NUM_ROBOTS; j++)
        {
            GameObject header = CreateUIObject($"ColHeader_{label}_{j}", panel);
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
            GameObject rowHeader = CreateUIObject($"RowHeader_{label}_{i}", panel);
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
                GameObject cell = CreateUIObject($"Cell_{label}_{i}_{j}", panel);
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

                GameObject textObj = CreateUIObject($"Text_{label}_{i}_{j}", cell);
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

                // GBDT
                float probGBDT = fm.GetLatestProbability(j, lstm: false);
                if (probGBDT >= 0f)
                {
                    int predGBDT = fm.GetLatestPrediction(j, lstm: false);
                    cellsGBDT[observerId, j].color    = predGBDT == 1 ? Color.red : Color.green;
                    cellTextsGBDT[observerId, j].text = probGBDT.ToString("F2");
                }

                // LSTM
                float probLSTM = fm.GetLatestProbability(j, lstm: true);
                if (probLSTM >= 0f)
                {
                    int predLSTM = fm.GetLatestPrediction(j, lstm: true);
                    cellsLSTM[observerId, j].color    = predLSTM == 1 ? Color.red : Color.green;
                    cellTextsLSTM[observerId, j].text = probLSTM.ToString("F2");
                }
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