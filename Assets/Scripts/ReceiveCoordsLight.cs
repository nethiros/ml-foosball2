using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

// Skript zur Messung der Roundtriptime. Unwichtig für spätere Ausführung
public class ReceiveCoordsLight : MonoBehaviour
{
    [Header("Objektreferenzen")]
    public GameObject ball;
    public Transform plane;

    [Header("Netzwerkkonfiguration")]
    public int port = 5005;

    [Header("Bewegungskonfiguration")]
    public bool useClamp = true;
    public float interpolationSpeed = 10f;

    [Header("EWMA Konfiguration")]
    [Tooltip("Höher = mehr Rauschunterdrückung, aber mehr Latenz (0.01-0.5)")]
    [Range(0.01f, 0.5f)]
    public float gammaVelocity = 0.2f;
    [Tooltip("Höher = mehr Rauschunterdrückung, aber mehr Latenz (0.01-0.5)")]
    [Range(0.01f, 0.5f)]
    public float gammaPosition = 0.1f;
    [Tooltip("Größe des Positionspuffers (mehr = stabiler, aber mehr Speicher)")]
    [Range(10, 60)]
    public int bufferSize = 30;

    [Header("Bewegungserkennung")]
    [Tooltip("Minimale Positionsänderung für Bewegungserkennung (in normalisiertem Raum)")]
    [Range(0.001f, 0.1f)]
    public float motionThreshold = 0.01f;
    public bool showMotionDebug = false;

    [Header("Visuelle Einstellungen")]
    public Color ballVisibleColor = Color.white;
    public Color ballNotVisibleColor = Color.red;
    public bool hideWhenNotVisible = false;

    [Header("Geschwindigkeitskonfiguration")]
    public float tableWidth = 1.18f;
    public float tableHeight = 0.68f;
    public float velocityScale = 30f;

    private UdpClient udpClient;
    private Renderer ballRenderer;
    private Thread receiveThread;
    private bool running = true;
    private Vector3 targetPosition = Vector3.zero;

    // Thread-sichere Variablen (Position/Sichtbarkeit)
    private volatile float normX;
    private volatile float normZ;
    private volatile bool receivedIsVisible = false;
    private volatile bool newDataAvailable = false;
    private object dataLock = new object();

    // Thread-sichere Variablen (Lichtstatus) - Unverändert
    private volatile bool receivedIsLightOn = false;
    private object lightDataLock = new object();

    // EWMA Puffer
    private List<PositionData> positionBuffer = new List<PositionData>();

    // Statische Eigenschaften für externen Zugriff - Unverändert
    public static float RawVelocityX { get; private set; }
    public static float RawVelocityZ { get; private set; }
    public static float ApproximatedVelocityX { get; private set; }
    public static float ApproximatedVelocityZ { get; private set; }
    public static float NormalizedX { get; private set; }
    public static float NormalizedZ { get; private set; }
    public static bool IsBallVisible { get; private set; } = false;
    public static Vector3 LastCalculatedWorldPosition { get; private set; } = Vector3.zero;
    public static bool IsBallInMotion { get; private set; } = false;
    public static bool IsLightOn { get; private set; } = false; // Behalten wir bei
    public static float TimeSinceLightOn { get; private set; } = -1f; // Behalten wir bei

    // Interne Variable zur Zustandserkennung - Unverändert
    private bool wasLightOnPreviously = false;

    [Header("Timeout")]
    public float receiveTimeout = 0.5f;
    private float timeSinceLastReceive = 0f;

    // Event, das ausgelöst wird, wenn Licht angeht
    public static event Action OnLightTurnedOnDetected;

    // Innere Klasse für Positionsdaten
    private class PositionData
    {
        public Vector2 position;
        public float timestamp;

        public PositionData(Vector2 pos, float time)
        {
            position = pos;
            timestamp = time;
        }
    }

    void Start()
    {
        ballRenderer = ball.GetComponent<Renderer>();
        if (ballRenderer != null)
        {
            UpdateBallAppearance();
        }
        targetPosition = ball.transform.position;

        // UDP Listener-Thread starten
        receiveThread = new Thread(StartUdpListener);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    void StartUdpListener()
    {
        try
        {
            udpClient = new UdpClient(port);
            Debug.Log("UDP Listener gestartet auf Port " + port);
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);

            // Erwartete Größe des Datenpakets ANPASSEN ('iff??')
            // int + float + float + bool + bool
            int expectedDataLength = sizeof(int) + sizeof(float) + sizeof(float) + sizeof(bool) + sizeof(bool); // *** Geändert ***

            while (running)
            {
                try
                {
                    byte[] receivedBytes = udpClient.Receive(ref remoteEP);

                    if (receivedBytes.Length == expectedDataLength)
                    {
                        // Daten wie bisher lesen
                        float receivedX = BitConverter.ToSingle(receivedBytes, 4);
                        float receivedZ = BitConverter.ToSingle(receivedBytes, 8);
                        bool isVisible = BitConverter.ToBoolean(receivedBytes, 12);
                        // *** NEU: Lichtstatus lesen (Offset 13, nach isVisible) ***
                        bool isLightOn = BitConverter.ToBoolean(receivedBytes, 13);

                        // Position und Sichtbarkeit sperren und aktualisieren
                        lock (dataLock)
                        {
                            normX = receivedX;
                            normZ = receivedZ;
                            receivedIsVisible = isVisible;
                            newDataAvailable = true; // Markiere, dass Positionsdaten da sind
                        }

                        // Lichtstatus sperren und aktualisieren
                        lock (lightDataLock)
                        {
                            receivedIsLightOn = isLightOn;
                            // newDataAvailable wird hier *nicht* gesetzt, da es primär für die Position ist.
                            // Die Verarbeitung des Lichtstatus erfolgt in Update() unabhängig davon.
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Empfangenes UDP-Paket hat falsche Länge: {receivedBytes.Length} Bytes (erwartet: {expectedDataLength})");
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.Interrupted && running)
                    {
                        Debug.LogWarning("SocketException im UDP Receive Loop: " + ex.Message);
                        Thread.Sleep(10);
                    }
                }
                catch (System.Exception ex)
                {
                    if (running)
                    {
                        Debug.LogError("Fehler im UDP Listener Thread: " + ex.Message + "\n" + ex.StackTrace);
                        Thread.Sleep(100);
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
            udpClient?.Close();
            Debug.Log("UDP Listener beendet.");
        }
    }

    void Update()
    {
        bool processedNewDataThisFrame = false;
        bool currentIsLightOn;

        // Aktuellen Lichtstatus holen
        lock (lightDataLock)
        {
            currentIsLightOn = receivedIsLightOn;
        }
        IsLightOn = currentIsLightOn; // Statische Variable aktualisieren

        // Nur Event auslösen, keine Zeitmessung hier
        if (currentIsLightOn && !wasLightOnPreviously)
        {
            // Zustand hat sich von AUS (false) zu AN (true) geändert
            Debug.Log($"[{Time.time:F3}] Receiver: Licht AN erkannt.");

            // Event für den LightController (oder andere Interessierte) auslösen
            OnLightTurnedOnDetected?.Invoke();

            // Allgemeine "Licht An"-Zeit starten (falls noch genutzt)
            if (TimeSinceLightOn < 0) TimeSinceLightOn = 0f;
        }
        else if (currentIsLightOn)
        {
            if (TimeSinceLightOn >= 0) TimeSinceLightOn += Time.deltaTime;
        }
        else if (!currentIsLightOn && wasLightOnPreviously)
        {
            Debug.Log($"[{Time.time:F3}] Receiver: Licht AUS erkannt.");
            // TimeSinceLightOn = -1f; // Zurücksetzen?
        }

        wasLightOnPreviously = currentIsLightOn;


        // Vorherigen Zustand für den nächsten Frame merken
        wasLightOnPreviously = currentIsLightOn;


        // Daten vom Netzwerk-Thread verarbeiten (Position) - bleibt gleich
        if (newDataAvailable)
        {
            ProcessNewData();
            newDataAvailable = false;
            timeSinceLastReceive = 0f;
            processedNewDataThisFrame = true;
        }
        else
        {
            // Timeout-Logik
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

        // Ball-Position interpolieren
        ball.transform.position = Vector3.Lerp(
            ball.transform.position,
            targetPosition,
            Time.deltaTime * interpolationSpeed
        );

        // Geschwindigkeit und Bewegungsstatus berechnen
        if (IsBallVisible && processedNewDataThisFrame && positionBuffer.Count >= 2)
        {
            CalculateVelocityEWMA();
            DetectMotion();
        }
        else if (!IsBallVisible)
        {
            RawVelocityX = 0;
            RawVelocityZ = 0;
            ApproximatedVelocityX = 0;
            ApproximatedVelocityZ = 0;
            IsBallInMotion = false;
        }
    }

    // ProcessNewData vereinfacht, da Lichtstatus in Update behandelt wird
    private void ProcessNewData()
    {
        float currentNormX;
        float currentNormZ;
        bool currentIsVisible; // Sichtbarkeit wird hier noch geholt

        // Positions-Daten kopieren
        lock (dataLock)
        {
            currentNormX = normX;
            currentNormZ = normZ;
            currentIsVisible = receivedIsVisible; // Sichtbarkeit hier holen
        }

        // Statische Variablen aktualisieren (nur Position/Sichtbarkeit hier)
        NormalizedX = currentNormX;
        NormalizedZ = currentNormZ;
        IsBallVisible = currentIsVisible; // Sichtbarkeit hier setzen

        // Welt-Koordinaten berechnen
        float planeWidth = plane.localScale.x;
        float planeHeight = plane.localScale.z;

        float posX, posZ;
        if (useClamp)
        {
            float clampedX = Mathf.Clamp(currentNormX, -1f, 1f);
            float clampedZ = Mathf.Clamp(currentNormZ, -1f, 1f);
            posX = clampedX * (planeWidth / 2f) + plane.position.x;
            posZ = clampedZ * (planeHeight / 2f) + plane.position.z;
        }
        else
        {
            posX = currentNormX * (planeWidth / 2f) + plane.position.x;
            posZ = currentNormZ * (planeHeight / 2f) + plane.position.z;
        }

        // Zielposition aktualisieren
        targetPosition = new Vector3(posX, ball.transform.position.y, posZ);
        LastCalculatedWorldPosition = targetPosition;

        // Position zum Puffer hinzufügen (nur wenn Ball sichtbar)
        if (currentIsVisible)
        {
            Vector2 currentPosition = new Vector2(currentNormX, currentNormZ);
            positionBuffer.Insert(0, new PositionData(currentPosition, Time.time));

            // Puffergröße begrenzen
            if (positionBuffer.Count > bufferSize)
            {
                positionBuffer.RemoveAt(positionBuffer.Count - 1);
            }
        }

        UpdateBallAppearance();
    }

    private void UpdateBallAppearance()
    {
        if (ballRenderer != null)
        {
            ballRenderer.material.color = IsBallVisible ? ballVisibleColor : ballNotVisibleColor;
            ballRenderer.enabled = !hideWhenNotVisible || IsBallVisible;
        }
    }

    // Neue EWMA basierte Geschwindigkeitsberechnung
    private void CalculateVelocityEWMA()
    {
        if (positionBuffer.Count < 2) return;

        Vector2 velocityEWMA = Vector2.zero;
        Vector2 positionEWMA = Vector2.zero;
        float denominatorVel = 0f;
        float denominatorPos = 0f;

        // EWMA Berechnung ähnlich dem ersten Code
        for (int i = 0; i < positionBuffer.Count - 1; i++)
        {
            float scaleVel = Mathf.Exp(-gammaVelocity * i);
            float scalePos = Mathf.Exp(-gammaPosition * i);

            denominatorVel += scaleVel;
            denominatorPos += scalePos;

            // Geschwindigkeitskomponente
            Vector2 posDiff = positionBuffer[i].position - positionBuffer[i + 1].position;

            // Bei variabler Zeit zwischen Samples:
            float timeDiff = positionBuffer[i].timestamp - positionBuffer[i + 1].timestamp;
            if (timeDiff > Mathf.Epsilon)
            {
                Vector2 velocity = posDiff / timeDiff;
                velocityEWMA += velocity * scaleVel;
            }

            // Positions-EWMA (für alternative Ansätze)
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
            // Hier könnte man die geglättete Position verwenden
        }

        // Skalierung auf Echtmaßstab
        float realWorldScaleX = tableWidth / 2f;
        float realWorldScaleZ = tableHeight / 2f;

        RawVelocityX = velocityEWMA.x * realWorldScaleX;
        RawVelocityZ = velocityEWMA.y * realWorldScaleZ;

        // Normalisierte Werte für externe Verwendung
        ApproximatedVelocityX = Mathf.Clamp(RawVelocityX / velocityScale, -1f, 1f);
        ApproximatedVelocityZ = Mathf.Clamp(RawVelocityZ / velocityScale, -1f, 1f);
    }

    // Bewegungserkennung basierend auf Varianzmessung
    private void DetectMotion()
    {
        if (positionBuffer.Count < 3) return;

        // Wir verwenden nur die letzten ~10 Positionen für die Bewegungserkennung
        int samplesToCheck = Mathf.Min(10, positionBuffer.Count);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        // Min/Max der Positionen finden
        for (int i = 0; i < samplesToCheck; i++)
        {
            Vector2 pos = positionBuffer[i].position;
            min.x = Mathf.Min(min.x, pos.x);
            min.y = Mathf.Min(min.y, pos.y);
            max.x = Mathf.Max(max.x, pos.x);
            max.y = Mathf.Max(max.y, pos.y);
        }

        // Varianz berechnen
        float diffX = max.x - min.x;
        float diffY = max.y - min.y;

        // Ball ist in Bewegung, wenn die Positions-Varianz über dem Schwellwert liegt
        IsBallInMotion = diffX > motionThreshold || diffY > motionThreshold;

        if (showMotionDebug)
        {
            Debug.Log($"Motion check: ΔX={diffX:F4}, ΔY={diffY:F4}, InMotion={IsBallInMotion}");
        }
    }

    void OnApplicationQuit()
    {
        running = false;
        udpClient?.Close();
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500);
        }
        Debug.Log("Anwendung wird beendet, UDP Listener gestoppt.");
    }

    void OnDisable()
    {
        running = false;
        udpClient?.Close();
        Debug.Log("GameObject deaktiviert, UDP Listener gestoppt.");
    }
}