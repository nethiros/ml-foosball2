using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

/// <summary>
/// Empfängt Ballpositionen über UDP-Netzwerkverbindung und visualisiert sie in Unity.
/// Implementiert EWMA-Filterung zur Glättung und Bewegungserkennung.
/// </summary>
public class BallPositionReceiverUDP : MonoBehaviour
{
    [Header("Objektreferenzen")]
    public GameObject ball; // Visualisierung der Ballkoordinaten
    public Transform plane; // Zur Berechnung der Platzierung des Balls

    [Header("Netzwerkkonfiguration")]
    public int port = 5005; // UDP-Port, über den die Daten empfangen werden

    [Header("Bewegungskonfiguration")]
    public bool useClamp = true; // Begrenzt Ballposition innerhalb der Plane-Grenzen
    public float interpolationSpeed = 10f; // Geschwindigkeit der Positionsinterpolation (höher = schnellere Bewegung)

    [Header("EWMA Konfiguration")]
    [Tooltip("Höher = mehr Rauschunterdrückung, aber mehr Latenz (0.01-0.5)")]
    [Range(0.01f, 0.5f)]
    public float gammaVelocity = 0.2f; // Gewichtungsfaktor für exponentiell gewichteten Mittelwert der Geschwindigkeit
    [Tooltip("Höher = mehr Rauschunterdrückung, aber mehr Latenz (0.01-0.5)")]
    [Range(0.01f, 0.5f)]
    public float gammaPosition = 0.1f; // Gewichtungsfaktor für exponentiell gewichteten Mittelwert der Position
    [Tooltip("Größe des Positionspuffers (mehr = stabiler, aber mehr Speicher)")]
    [Range(10, 60)]
    public int bufferSize = 30; // Anzahl der zu speichernden Positionswerte für Berechnung

    [Header("Bewegungserkennung")]
    [Tooltip("Minimale Positionsänderung für Bewegungserkennung (in normalisiertem Raum)")]
    [Range(0.001f, 0.1f)]
    public float motionThreshold = 0.01f; // Schwellenwert, ab dem eine Bewegung erkannt wird
    public bool showMotionDebug = false; // Zeigt Debug-Infos zur Bewegungserkennung

    [Header("Visuelle Einstellungen")]
    public Color ballVisibleColor = Color.white;    // Farbe wenn Ball sichtbar ist
    public Color ballNotVisibleColor = Color.red;   // Farbe wenn Ball nicht sichtbar ist
    public bool hideWhenNotVisible = false;         // Option Ball ganz wegzuschalten wenn er nichtt mehr sichtbar ist

    [Header("Geschwindigkeitskonfiguration")]
    public float tableWidth = 1.18f;  // Tatsächliche Tischbreite in Metern
    public float tableHeight = 0.68f; // Tatsächliche Tischhöhe in Metern
    public float velocityScale = 30f; // Skalierungsfaktor für Geschwindigkeitswerte

    private UdpClient udpClient;    // Client für UDP-Verbindung
    private Renderer ballRenderer;  // Um Farbe des Balles zu ändern
    private Thread receiveThread;   // Thread für den UDP-Empfang
    private bool running = true;    // Steuert den Lauf des UDP-Threads
    private Vector3 targetPosition = Vector3.zero; // Zielposition für die Interpolation

    // Thread-sichere Variablen für Datenaustausch zwischen Threads
    private volatile float normX;   // Normalisierte X-Koordinate (zwischen -1 und 1)
    private volatile float normZ;   // Normalisierte Z-Koordinate (zwischen -1 und 1)
    private volatile bool receivedIsVisible = false; // Signalisiert, ob der Ball sichtbar ist
    private volatile bool newDataAvailable = false;  // Signalisiert, dass neue Daten verfügbar sind
    private object dataLock = new object();          // Objekt für Thread-Synchronisation

    // EWMA Puffer für Positionen und Zeiten
    private List<PositionData> positionBuffer = new List<PositionData>();

    // Statische Eigenschaften für externen Zugriff (können von anderen Skripten ausgelesen werden)
    public static float RawVelocityX { get; private set; }             // Ungefilterte X-Geschwindigkeit
    public static float RawVelocityZ { get; private set; }             // Ungefilterte Z-Geschwindigkeit
    public static float ApproximatedVelocityX { get; private set; }    // Gefilterte und skalierte X-Geschwindigkeit
    public static float ApproximatedVelocityZ { get; private set; }    // Gefilterte und skalierte Z-Geschwindigkeit
    public static float NormalizedX { get; private set; }              // Normalisierte X-Position (-1 bis 1)
    public static float NormalizedZ { get; private set; }              // Normalisierte Z-Position (-1 bis 1)
    public static bool IsBallVisible { get; private set; } = false;    // Ist der Ball aktuell sichtbar?
    public static Vector3 LastCalculatedWorldPosition { get; private set; } = Vector3.zero; // Letzte berechnete position
    public static bool IsBallInMotion { get; private set; } = false;   // Ist der Ball in Bewegung?

    [Header("Timeout")]
    public float receiveTimeout = 0.5f;  // Zeit in Sekunden, nach der der Ball als nicht sichtbar gilt
    private float timeSinceLastReceive = 0f; // Zeit seit dem letzten Datenempfang

    /// <summary>
    /// Innere Klasse zum Speichern von Positionsdaten mit Zeitstempel
    /// </summary>
    private class PositionData
    {
        public Vector2 position;    // Normalisierte Position des Balls
        public float timestamp;     // Zeitpunkt der Erfassung

        public PositionData(Vector2 pos, float time)
        {
            position = pos;
            timestamp = time;
        }
    }

    /// <summary>
    /// Initialisierung beim Start der Komponente
    /// </summary>
    void Start()
    {
        // Renderer-Komponente des Balls abrufen
        ballRenderer = ball.GetComponent<Renderer>();
        if (ballRenderer != null)
        {
            UpdateBallAppearance();
        }
        targetPosition = ball.transform.position;

        // UDP Listener-Thread starten (läuft im Hintergrund)
        receiveThread = new Thread(StartUdpListener);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    /// <summary>
    /// Startet den UDP-Listener in einem separaten Thread
    /// </summary>
    void StartUdpListener()
    {
        try
        {
            // UDP-Client auf dem angegebenen Port initialisieren
            udpClient = new UdpClient(port);
            Debug.Log("UDP Listener gestartet auf Port " + port);
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);

            // Erwartete Größe des Datenpakets (Sequenznummer, X, Z, Sichtbarkeit)
            int expectedDataLength = sizeof(int) + sizeof(float) + sizeof(float) + sizeof(bool);

            // Hauptschleife des Empfangsthreads
            while (running)
            {
                try
                {
                    // Auf Daten warten (blockierende Operation)
                    byte[] receivedBytes = udpClient.Receive(ref remoteEP);

                    // Prüfen, ob die empfangenen Daten das erwartete Format haben
                    if (receivedBytes.Length == expectedDataLength)
                    {
                        // Daten aus dem Byte-Array extrahieren
                        float receivedX = BitConverter.ToSingle(receivedBytes, 4);
                        float receivedZ = BitConverter.ToSingle(receivedBytes, 8);
                        bool isVisible = BitConverter.ToBoolean(receivedBytes, 12);

                        // Thread-sicher die Daten in die gemeinsam genutzten Variablen kopieren
                        lock (dataLock)
                        {
                            normX = receivedX;
                            normZ = receivedZ;
                            receivedIsVisible = isVisible;
                            newDataAvailable = true; // Signalisieren, dass neue Daten vorliegen
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Empfangenes UDP-Paket hat falsche Länge: {receivedBytes.Length} Bytes (erwartet: {expectedDataLength})");
                    }
                }
                catch (SocketException ex)
                {
                    // Socket-Fehler abfangen (z.B. wenn der Socket geschlossen wird)
                    if (ex.SocketErrorCode != SocketError.Interrupted && running)
                    {
                        Debug.LogWarning("SocketException im UDP Receive Loop: " + ex.Message);
                        Thread.Sleep(10); // Kurze Pause, um CPU-Last zu reduzieren
                    }
                }
                catch (System.Exception ex)
                {
                    // Andere Fehler abfangen
                    if (running)
                    {
                        Debug.LogError("Fehler im UDP Listener Thread: " + ex.Message + "\n" + ex.StackTrace);
                        Thread.Sleep(100); // Längere Pause bei schwerwiegenden Fehlern
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Fehler beim Starten des UDP Listeners auf Port {port}: {e.Message}");
        }
        finally
        {
            // Aufräumen beim Beenden des Threads
            udpClient?.Close();
            Debug.Log("UDP Listener beendet.");
        }
    }

    /// <summary>
    /// Update-Methode, wird jeden Frame aufgerufen
    /// </summary>
    void Update()
    {
        bool processedNewDataThisFrame = false;

        // Daten vom Netzwerk-Thread verarbeiten, wenn verfügbar
        if (newDataAvailable)
        {
            ProcessNewData();
            newDataAvailable = false;
            timeSinceLastReceive = 0f;
            processedNewDataThisFrame = true;
        }
        else
        {
            // Timeout-Logik: Ball als nicht sichtbar markieren, wenn keine Daten empfangen werden
            timeSinceLastReceive += Time.deltaTime;
            if (timeSinceLastReceive > receiveTimeout)
            {
                if (IsBallVisible)
                {
                    IsBallVisible = false;
                    UpdateBallAppearance();
                }
            }
        }

        // Ball-Position sanft zur Zielposition interpolieren
        ball.transform.position = Vector3.Lerp(
            ball.transform.position,
            targetPosition,
            Time.deltaTime * interpolationSpeed
        );

        // Geschwindgkeit und Bewegungsstatus nur berechnen, wenn der Ball sichtbar ist
        if (IsBallVisible && processedNewDataThisFrame && positionBuffer.Count >= 2)
        {
            CalculateVelocityEWMA(); // Geschwindigkeit mit EWMA-Filter berechnen
            DetectMotion(); // Bewegung des Balls erkennen
        }
        else if (!IsBallVisible)
        {
            // Geschwindigkeiten auf 0 setzen, wenn Ball nicht sichtbar
            RawVelocityX = 0;
            RawVelocityZ = 0;
            ApproximatedVelocityX = 0;
            ApproximatedVelocityZ = 0;
            IsBallInMotion = false;
        }
    }

    /// <summary>
    /// Verarbeitet neu empfangene Daten und aktualisiert die Ballposition
    /// </summary>
    private void ProcessNewData()
    {
        float currentNormX;
        float currentNormZ;
        bool currentIsVisible;

        // Daten thread-sicher kopieren
        lock (dataLock)
        {
            currentNormX = normX;
            currentNormZ = normZ;
            currentIsVisible = receivedIsVisible;
        }

        // Statische Variablen für externen Zugriff aktualisieren
        NormalizedX = currentNormX;
        NormalizedZ = currentNormZ;
        IsBallVisible = currentIsVisible;

        // Welt-Koordinaten basierend auf der Plane-Größe berechnen
        float planeWidth = plane.localScale.x;
        float planeHeight = plane.localScale.z;

        float posX, posZ;
        if (useClamp)
        {
            // Werte auf den Bereich -1 bis 1 begrenzen, falls gewünscht
            float clampedX = Mathf.Clamp(currentNormX, -1f, 1f);
            float clampedZ = Mathf.Clamp(currentNormZ, -1f, 1f);
            posX = clampedX * (planeWidth / 2f) + plane.position.x;
            posZ = clampedZ * (planeHeight / 2f) + plane.position.z;
        }
        else
        {
            // Ohne Begrenzung könnte der Ball außerhalb der Plane sein
            posX = currentNormX * (planeWidth / 2f) + plane.position.x;
            posZ = currentNormZ * (planeHeight / 2f) + plane.position.z;
        }

        // Zielposition für die Interpolation aktualisieren
        targetPosition = new Vector3(posX, ball.transform.position.y, posZ);
        LastCalculatedWorldPosition = targetPosition;

        // Position zum Puffer hinzufügen (nur wenn Ball sichtbar)
        if (currentIsVisible)
        {
            Vector2 currentPosition = new Vector2(currentNormX, currentNormZ);
            positionBuffer.Insert(0, new PositionData(currentPosition, Time.time));

            // Puffergröße begrenzen, um Speicherverbrauch zu kontrollieren
            if (positionBuffer.Count > bufferSize)
            {
                positionBuffer.RemoveAt(positionBuffer.Count - 1);
            }
        }

        // Balldarstellung aktualisieren (Farbe, Sichtbarkeit)
        UpdateBallAppearance();
    }

    /// <summary>
    /// Aktualisiert das Erscheinungsbild des Balls basierend auf der Sichtbarkeit
    /// </summary>
    private void UpdateBallAppearance()
    {
        if (ballRenderer != null)
        {
            // Farbe basierend auf Sichtbarkeit setzen
            ballRenderer.material.color = IsBallVisible ? ballVisibleColor : ballNotVisibleColor;

            // Ball komplett ausblenden, falls konfiguriert und nicht sichtbar
            ballRenderer.enabled = !hideWhenNotVisible || IsBallVisible;
        }
    }

    /// <summary>
    /// Berechnet die Geschwindigkeit des Balls mit EWMA-Filterung (Exponentially Weighted Moving Average (Ansatz von "FromScratch"))
    /// </summary>
    private void CalculateVelocityEWMA()
    {
        if (positionBuffer.Count < 2) return;

        Vector2 velocityEWMA = Vector2.zero;
        Vector2 positionEWMA = Vector2.zero;
        float denominatorVel = 0f;
        float denominatorPos = 0f;

        // EWMA-Berechnung: Neuere Messungen werden stärker gewichtet
        for (int i = 0; i < positionBuffer.Count - 1; i++)
        {
            // Gewichtungsfaktoren exponentiell abfallen lassen (neuere Daten = höheres Gewicht)
            float scaleVel = Mathf.Exp(-gammaVelocity * i);
            float scalePos = Mathf.Exp(-gammaPosition * i);

            denominatorVel += scaleVel;
            denominatorPos += scalePos;

            // Positionsdifferenz zur Geschwindigkeitsberechnung
            Vector2 posDiff = positionBuffer[i].position - positionBuffer[i + 1].position;

            // Zeitdifferenz berücksichtigen für korrekte Geschwindigkeitsberechnung
            float timeDiff = positionBuffer[i].timestamp - positionBuffer[i + 1].timestamp;
            if (timeDiff > Mathf.Epsilon) // Division durch 0 vermeiden
            {
                Vector2 velocity = posDiff / timeDiff;
                velocityEWMA += velocity * scaleVel;
            }

            // Positions-EWMA berechnen (für zukünftige Erweiterungen)
            positionEWMA += positionBuffer[i].position * scalePos;
        }

        // Normalisieren durch Division mit der Summe der Gewichte
        if (denominatorVel > 0)
        {
            velocityEWMA /= denominatorVel;
        }

        if (denominatorPos > 0)
        {
            positionEWMA /= denominatorPos;
            // Geglättete Position könnte hier verwendet werden
        }

        // Skalierung von normalisierten Werten auf reale Tischgröße
        float realWorldScaleX = tableWidth / 2f;
        float realWorldScaleZ = tableHeight / 2f;

        // Rohe Geschwindigkeiten in Meter pro Sekunde
        RawVelocityX = velocityEWMA.x * realWorldScaleX;
        RawVelocityZ = velocityEWMA.y * realWorldScaleZ;

        // Normalisierte Werte für externe Verwendung (begrenzt auf -1 bis 1)
        ApproximatedVelocityX = Mathf.Clamp(RawVelocityX / velocityScale, -1f, 1f);
        ApproximatedVelocityZ = Mathf.Clamp(RawVelocityZ / velocityScale, -1f, 1f);
    }

    /// <summary>
    /// Erkennt, ob der Ball sich bewegt, basierend auf Positionsänderungen
    /// </summary>
    private void DetectMotion()
    {
        if (positionBuffer.Count < 3) return;

        // Nur die letzten ~10 Positionen für die Bewegungserkennung verwenden
        int samplesToCheck = Mathf.Min(10, positionBuffer.Count);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        // Min/Max der Positionen in den letzten Frames finden
        for (int i = 0; i < samplesToCheck; i++)
        {
            Vector2 pos = positionBuffer[i].position;
            min.x = Mathf.Min(min.x, pos.x);
            min.y = Mathf.Min(min.y, pos.y);
            max.x = Mathf.Max(max.x, pos.x);
            max.y = Mathf.Max(max.y, pos.y);
        }

        // Varianz bzw. Spannweite der Positionen berechnen
        float diffX = max.x - min.x;
        float diffY = max.y - min.y;

        // Ball ist in Bewegung, wenn die Positions-Varianz über dem Schwellwert liegt
        IsBallInMotion = diffX > motionThreshold || diffY > motionThreshold;

        // Debug-Ausgabe, falls aktiviert
        if (showMotionDebug)
        {
            Debug.Log($"Motion check: ΔX={diffX:F4}, ΔY={diffY:F4}, InMotion={IsBallInMotion}");
        }
    }

    /// <summary>
    /// Wird beim Beenden der Anwendung aufgerufen
    /// </summary>
    void OnApplicationQuit()
    {
        // Thread stoppen und UDP-Verbindung schließen
        running = false;
        udpClient?.Close();
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500); // Maximal 500ms auf Thread-Ende warten
        }
        Debug.Log("Anwendung wird beendet, UDP Listener gestoppt.");
    }

    /// <summary>
    /// Wird beim Deaktivieren der Komponente aufgerufen
    /// </summary>
    void OnDisable()
    {
        // Thread stoppen und UDP-Verbindung schließen
        running = false;
        udpClient?.Close();
        Debug.Log("GameObject deaktiviert, UDP Listener gestoppt.");
    }
}