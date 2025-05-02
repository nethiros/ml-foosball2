using UnityEngine;
using System.Collections.Generic;

// Geschwindigkeit des Balles visualisieren (gut für debugging der Geschwindigkeit des "echten" Balls.
public class VelocityGraphVisualizer : MonoBehaviour
{
    [Header("Grafik-Einstellungen")]
    public Vector2 graphSize = new Vector2(400, 200);
    public float graphDuration = 10f; // Zeitdauer des Graphen in Sekunden
    public float updateInterval = 0.1f; // Aktualisierungsintervall in Sekunden
    public Vector2 graphPosition = new Vector2(20, 20); // Position vom linken unteren Bildschirmrand

    [Header("Stil-Einstellungen")]
    public Color normalizedVelocityXColor = Color.red;
    public Color normalizedVelocityZColor = Color.blue;
    public Color rawVelocityXColor = new Color(1f, 0.5f, 0.5f, 0.5f); // Helles Rot, transparent
    public Color rawVelocityZColor = new Color(0.5f, 0.5f, 1f, 0.5f); // Helles Blau, transparent
    public Color backgroundColor = new Color(0, 0, 0, 0.7f);
    public Color gridColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
    public Color zeroLineColor = new Color(1f, 1f, 1f, 0.5f);
    public int gridLines = 4;
    // Wird jetzt in OnGUI initialisiert, falls nicht im Inspector zugewiesen
    public GUIStyle labelStyle;

    [Header("Verweis auf BallPositionReceiverUDP")]
    public BallPositionReceiverUDP ballReceiverUDP; // Direkte Referenz zum BallPositionReceiverUDP

    [Header("Geschwindigkeitsanzeige")]
    public float maxRawVelocity = 5f; // Maximale Roh-Geschwindigkeit für die Skalierung des Graphs (in m/s), anpassen!

    // Private Variablen
    private List<float> normalizedVelocityXHistory = new List<float>();
    private List<float> normalizedVelocityZHistory = new List<float>();
    private List<float> rawVelocityXHistory = new List<float>();
    private List<float> rawVelocityZHistory = new List<float>();
    private List<float> timeHistory = new List<float>();
    private float nextUpdateTime = 0f;

    private void Start()
    {
        // Suche nach BallPositionReceiverUDP
        if (ballReceiverUDP == null)
        {
            ballReceiverUDP = FindObjectOfType<BallPositionReceiverUDP>();
            if (ballReceiverUDP == null)
            {
                Debug.LogError("VelocityGraphVisualizer: Kein BallPositionReceiverUDP gefunden! Der Graph kann nicht aktualisiert werden.");
            }
            else
            {
                Debug.Log("VelocityGraphVisualizer: BallPositionReceiverUDP automatisch gefunden.");
            }
        }

    }

    private void Update()
    {
        // Aktualisiere den Graphen nur, wenn der Receiver vorhanden ist
        if (ballReceiverUDP == null && Time.time < 1f)
        {
            ballReceiverUDP = FindObjectOfType<BallPositionReceiverUDP>();
            if (ballReceiverUDP == null) return;
        }
        else if (ballReceiverUDP == null)
        {
            return;
        }

        // Daten sammeln im Intervall
        if (Time.time >= nextUpdateTime)
        {
            float normalizedVelX = BallPositionReceiverUDP.ApproximatedVelocityX;
            float normalizedVelZ = BallPositionReceiverUDP.ApproximatedVelocityZ;
            float rawVelX = BallPositionReceiverUDP.RawVelocityX;
            float rawVelZ = BallPositionReceiverUDP.RawVelocityZ;
            float currentTime = Time.time;

            normalizedVelocityXHistory.Add(normalizedVelX);
            normalizedVelocityZHistory.Add(normalizedVelZ);
            rawVelocityXHistory.Add(rawVelX);
            rawVelocityZHistory.Add(rawVelZ);
            timeHistory.Add(currentTime);

            float cutoffTime = currentTime - graphDuration;
            while (timeHistory.Count > 0 && timeHistory[0] < cutoffTime)
            {
                normalizedVelocityXHistory.RemoveAt(0);
                normalizedVelocityZHistory.RemoveAt(0);
                rawVelocityXHistory.RemoveAt(0);
                rawVelocityZHistory.RemoveAt(0);
                timeHistory.RemoveAt(0);
            }
            nextUpdateTime = Time.time + updateInterval;
        }
    }

    private void OnGUI()
    {
        if (labelStyle == null || labelStyle.normal.background == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
        }

        if (ballReceiverUDP == null && enabled)
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(10, 10, 400, 20), "Fehler: BallPositionReceiverUDP nicht gefunden!");
            return;
        }
        else if (!enabled)
        {
            return;
        }

        Color originalColor = GUI.color;

        GUI.color = backgroundColor;
        GUI.DrawTexture(
            new Rect(graphPosition.x, Screen.height - graphPosition.y - graphSize.y, graphSize.x, graphSize.y),
            Texture2D.whiteTexture
        );

        DrawGrid();

        GUI.color = zeroLineColor;
        float zeroY = Screen.height - graphPosition.y - graphSize.y / 2;
        GUI.DrawTexture(
            new Rect(graphPosition.x, zeroY, graphSize.x, 1),
            Texture2D.whiteTexture
        );

        DrawGraph(normalizedVelocityXHistory, normalizedVelocityXColor);
        DrawGraph(normalizedVelocityZHistory, normalizedVelocityZColor);
        DrawRawGraph(rawVelocityXHistory, rawVelocityXColor);
        DrawRawGraph(rawVelocityZHistory, rawVelocityZColor);
        DrawLegend();
        DrawCurrentValues();

        GUI.color = originalColor;
    }

    private void DrawGrid()
    {
        GUI.color = gridColor;
        float graphBottom = Screen.height - graphPosition.y;
        float graphTop = Screen.height - graphPosition.y - graphSize.y;
        float graphLeft = graphPosition.x;
        float graphRight = graphPosition.x + graphSize.x;

        // Vertikale Linien & Zeitbeschriftung
        for (int i = 0; i <= gridLines; i++)
        {
            float x = graphLeft + (graphSize.x / gridLines) * i;
            GUI.DrawTexture(
                new Rect(x, graphTop, 1, graphSize.y),
                Texture2D.whiteTexture
            );

            float time = (gridLines - i) * (graphDuration / gridLines); // Korrigierte Zeitberechnung
            string timeLabel = "-" + time.ToString("F1") + "s";
            if (i == gridLines) timeLabel = "Jetzt";

            // Verwende GUIStyle für Ausrichtung
            Vector2 labelSize = labelStyle.CalcSize(new GUIContent(timeLabel));
            GUI.Label(
                new Rect(x - labelSize.x / 2, graphBottom, labelSize.x, labelSize.y), // Zentriert unter der Linie
                timeLabel,
                labelStyle
            );
        }

        // Horizontale Linien & Wertbeschriftung
        for (int i = 0; i <= gridLines; i++)
        {
            float y = graphTop + (graphSize.y / gridLines) * i;
            GUI.DrawTexture(
                new Rect(graphLeft, y, graphSize.x, 1),
                Texture2D.whiteTexture
            );

            // Wertbeschriftung (-1 bis 1 für normalisierte Werte)
            float normValue = 1.0f - (float)i / (gridLines / 2.0f); // Skala von +1 (oben) bis -1 (unten)
            // Nur anzeigen, wenn sinnvoll im Bereich oder an den Rändern
            if (Mathf.Abs(normValue) <= 1.01f) // Kleine Toleranz
            {
                // Verwende GUIStyle für Ausrichtung
                string labelText = normValue.ToString("F1");
                Vector2 labelSize = labelStyle.CalcSize(new GUIContent(labelText));
                GUI.Label(
                    new Rect(graphLeft - labelSize.x - 5, y - labelSize.y / 2, labelSize.x, labelSize.y), // Links neben der Linie
                    labelText,
                    labelStyle
                );
            }
        }
    }

    // Zeichnet Graphen für Werte im Bereich [-1, 1]
    private void DrawGraph(List<float> values, Color color)
    {
        if (values.Count < 2 || timeHistory.Count != values.Count) return;

        float currentTime = Time.time;
        float graphCenterY = Screen.height - graphPosition.y - graphSize.y / 2;
        float graphHeightHalf = graphSize.y / 2;

        // Zeichne Liniensegmente
        for (int i = 1; i < values.Count; i++)
        {
            float timeRatio1 = Mathf.Clamp01((timeHistory[i - 1] - (currentTime - graphDuration)) / graphDuration);
            float timeRatio2 = Mathf.Clamp01((timeHistory[i] - (currentTime - graphDuration)) / graphDuration);

            float x1 = graphPosition.x + graphSize.x * timeRatio1;
            float x2 = graphPosition.x + graphSize.x * timeRatio2;

            // Werte auf Graph-Höhe abbilden (Clamp stellt sicher, dass Linien nicht über den Rand gehen)
            float y1 = graphCenterY - Mathf.Clamp(values[i - 1], -1f, 1f) * graphHeightHalf;
            float y2 = graphCenterY - Mathf.Clamp(values[i], -1f, 1f) * graphHeightHalf;

            DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), color, 2f);
        }
    }

    // Zeichnet Graphen für Roh-Werte, skaliert durch maxRawVelocity
    private void DrawRawGraph(List<float> values, Color color)
    {
        if (values.Count < 2 || timeHistory.Count != values.Count || maxRawVelocity <= 0) return;

        float currentTime = Time.time;
        float graphCenterY = Screen.height - graphPosition.y - graphSize.y / 2;
        float graphHeightHalf = graphSize.y / 2;

        // Zeichne Liniensegmente
        for (int i = 1; i < values.Count; i++)
        {
            float timeRatio1 = Mathf.Clamp01((timeHistory[i - 1] - (currentTime - graphDuration)) / graphDuration);
            float timeRatio2 = Mathf.Clamp01((timeHistory[i] - (currentTime - graphDuration)) / graphDuration);

            float x1 = graphPosition.x + graphSize.x * timeRatio1;
            float x2 = graphPosition.x + graphSize.x * timeRatio2;

            // Skaliere und clamp Rohwerte auf den Bereich [-1, 1] basierend auf maxRawVelocity
            float scaledValue1 = Mathf.Clamp(values[i - 1] / maxRawVelocity, -1f, 1f);
            float scaledValue2 = Mathf.Clamp(values[i] / maxRawVelocity, -1f, 1f);

            // Werte auf Graph-Höhe abbilden
            float y1 = graphCenterY - scaledValue1 * graphHeightHalf;
            float y2 = graphCenterY - scaledValue2 * graphHeightHalf;

            DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), color, 1.5f); // Etwas dünner für Rohdaten
        }
    }

    // Helper to draw a line using GUI.DrawTexture
    private void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float thickness)
    {
        // Speichere aktuelle Matrix und Farbe
        Matrix4x4 matrix = GUI.matrix;
        Color savedColor = GUI.color;
        GUI.color = color;

        // Handle vertical lines to avoid NaN issues with angle calculation if pointA.x == pointB.x
        if (Mathf.Approximately(pointA.x, pointB.x))
        {
            GUI.DrawTexture(new Rect(pointA.x - thickness / 2, Mathf.Min(pointA.y, pointB.y), thickness, Mathf.Abs(pointB.y - pointA.y)), Texture2D.whiteTexture);
        }
        // Handle horizontal lines slightly optimized
        else if (Mathf.Approximately(pointA.y, pointB.y))
        {
            GUI.DrawTexture(new Rect(Mathf.Min(pointA.x, pointB.x), pointA.y - thickness / 2, Mathf.Abs(pointB.x - pointA.x), thickness), Texture2D.whiteTexture);
        }
        else // Angled lines
        {
            float angle = Vector2.Angle(pointB - pointA, Vector2.right);
            // Korrektur für Winkel > 180 Grad
            if (pointB.y < pointA.y) { angle = -angle; }

            // Skaliere und rotiere um den Mittelpunkt der Linie
            Vector2 center = (pointA + pointB) / 2f;
            GUIUtility.ScaleAroundPivot(new Vector2((pointB - pointA).magnitude, thickness), center);
            GUIUtility.RotateAroundPivot(angle, center);

            // Zeichne einen 1x1 Pixel an der rotierten/skalierten Position pointA
            // Die Länge wird durch die Skalierung abgedeckt.
            // Y-Koordinate muss um die halbe Dicke verschoben werden.
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - thickness / 2f, 1, 1), Texture2D.whiteTexture);
        }


        // Wiederherstellen
        GUI.matrix = matrix;
        GUI.color = savedColor;
    }


    private void DrawLegend()
    {
        float legendX = graphPosition.x + 10;
        // Positioniere die Legende über dem Graphen
        float legendY = Screen.height - graphPosition.y - graphSize.y - 95;
        float itemHeight = 20;
        float legendWidth = 230; // Etwas breiter für längere Labels
        float legendHeight = 90;


        // Hintergrund
        Color legendBg = backgroundColor; // Verwende gleiche Hintergrundfarbe
        GUI.color = legendBg;
        GUI.DrawTexture(
            new Rect(legendX - 5, legendY - 5, legendWidth, legendHeight),
            Texture2D.whiteTexture
        );

        Rect labelRect = new Rect(legendX + 30, legendY - 8, legendWidth - 40, itemHeight);

        // Legende für normalisierte Geschwindigkeit X
        GUI.color = normalizedVelocityXColor;
        GUI.DrawTexture(new Rect(legendX, legendY, 20, 2), Texture2D.whiteTexture);
        GUI.Label(labelRect, "Norm. Geschw. X [-1, 1]", labelStyle);

        // Legende für normalisierte Geschwindigkeit Z
        labelRect.y += itemHeight;
        GUI.color = normalizedVelocityZColor;
        GUI.DrawTexture(new Rect(legendX, legendY + itemHeight, 20, 2), Texture2D.whiteTexture);
        GUI.Label(labelRect, "Norm. Geschw. Z [-1, 1]", labelStyle);

        // Legende für Rohgeschwindigkeit X
        labelRect.y += itemHeight;
        GUI.color = rawVelocityXColor;
        GUI.DrawTexture(new Rect(legendX, legendY + itemHeight * 2, 20, 2), Texture2D.whiteTexture);
        GUI.Label(labelRect, $"Roh-Geschw. X [+/-{maxRawVelocity:F1} m/s]", labelStyle);

        // Legende für Rohgeschwindigkeit Z
        labelRect.y += itemHeight;
        GUI.color = rawVelocityZColor;
        GUI.DrawTexture(new Rect(legendX, legendY + itemHeight * 3, 20, 2), Texture2D.whiteTexture);
        GUI.Label(labelRect, $"Roh-Geschw. Z [+/-{maxRawVelocity:F1} m/s]", labelStyle);

    }

    private void DrawCurrentValues()
    {
        if (normalizedVelocityXHistory.Count == 0) return; // Überprüfe nur eine Liste, sie sollten synchron sein

        // Positioniere die Werte rechts neben dem Graphen
        float valueX = graphPosition.x + graphSize.x + 10;
        float valueY = Screen.height - graphPosition.y - graphSize.y;
        float valueWidth = 210;
        float valueHeight = 110; // Angepasst für 5 Zeilen

        // Aktuelle Werte abrufen (letzter Eintrag in der Liste)
        float normVelX = normalizedVelocityXHistory[normalizedVelocityXHistory.Count - 1];
        float normVelZ = normalizedVelocityZHistory[normalizedVelocityZHistory.Count - 1];
        float rawVelX = rawVelocityXHistory[rawVelocityXHistory.Count - 1];
        float rawVelZ = rawVelocityZHistory[rawVelocityZHistory.Count - 1];

        // Hintergrund
        GUI.color = backgroundColor;
        GUI.DrawTexture(
            new Rect(valueX - 5, valueY - 5, valueWidth, valueHeight),
            Texture2D.whiteTexture
        );

        Rect labelRect = new Rect(valueX, valueY, valueWidth - 10, 20);

        // Überschrift
        GUI.color = Color.white;
        GUI.Label(labelRect, "Aktuelle Werte:", labelStyle);

        // Norm. VelX
        labelRect.y += 20;
        GUI.color = normalizedVelocityXColor;
        GUI.Label(labelRect, "Norm. X: " + normVelX.ToString("F3"), labelStyle);

        // Norm. VelZ
        labelRect.y += 20;
        GUI.color = normalizedVelocityZColor;
        GUI.Label(labelRect, "Norm. Z: " + normVelZ.ToString("F3"), labelStyle);

        // Roh VelX
        labelRect.y += 20;
        GUI.color = rawVelocityXColor;
        GUI.Label(labelRect, "Roh X: " + rawVelX.ToString("F2") + " m/s", labelStyle);

        // Roh VelZ
        labelRect.y += 20;
        GUI.color = rawVelocityZColor;
        GUI.Label(labelRect, "Roh Z: " + rawVelZ.ToString("F2") + " m/s", labelStyle);

    }


}