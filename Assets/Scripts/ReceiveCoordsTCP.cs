using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

//Alter Ansatz -> NEU: Daten über UDP empfangen (siehe ReceiveCoordsUDP)

public class BallPositionReceiver : MonoBehaviour
{
    [Header("Objektreferenzen")]
    public GameObject ball;
    public Transform plane;

    [Header("Netzwerkkonfiguration")]
    public int port = 6969;

    [Header("Bewegungskonfiguration")]
    public bool useClamp = true;
    public float interpolationSpeed = 10f;

    [Header("Visuelle Einstellungen")]
    public Color visibleBallColor = Color.white;
    public Color invisibleBallColor = new Color(1f, 1f, 1f, 0.5f);

    [Header("Geschwindigkeitskonfiguration")]
    public float tableWidth = 1.2f;
    public float tableHeight = 0.7f;
    public float cameraFPS = 30f;
    public float velocityScale = 40f;

    private TcpListener listener;
    private Renderer ballRenderer;
    private bool running = true;
    private Vector3 targetPosition = Vector3.zero;

    // Thread-sichere Variablen
    private volatile float normX;
    private volatile float normZ;
    private volatile bool isBallVisible = true;
    private volatile bool newDataAvailable = false;

    // Für die Geschwindigkeitsberechnung
    private const int HISTORY_SIZE = 4;
    private Queue<Vector2> positionHistory = new Queue<Vector2>();
    private Queue<float> timeHistory = new Queue<float>();
    private float[] weights = { 0.6f, 0.25f, 0.1f, 0.05f }; // Neueste zu ältesten Messungen

    // Statische Eigenschaften für externen Zugriff
    public static float RawVelocityX { get; private set; }
    public static float RawVelocityZ { get; private set; }
    public static float ApproximatedVelocityX { get; private set; }
    public static float ApproximatedVelocityZ { get; private set; }
    public static float NormalizedX { get; private set; }
    public static float NormalizedZ { get; private set; }
    public static bool IsBallVisible { get; private set; }

    void Start()
    {
        ballRenderer = ball.GetComponent<Renderer>();

        // Server-Thread starten
        Thread serverThread = new Thread(StartServer);
        serverThread.IsBackground = true;
        serverThread.Start();
    }

    void StartServer()
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Debug.Log("Server gestartet auf Port " + port);

        while (running)
        {
            TcpClient client = null;
            NetworkStream stream = null;

            try
            {
                client = listener.AcceptTcpClient();
                stream = client.GetStream();

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    string[] values = receivedData.Split(',');

                    if (values.Length >= 2 &&
                        float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float receivedX) &&
                        float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float receivedZ))
                    {
                        // Thread-sichere Aktualisierung
                        normX = receivedX;
                        normZ = receivedZ;

                        // Sichtbarkeits-Flag auswerten
                        if (values.Length >= 3 && int.TryParse(values[2], out int visibilityFlag))
                        {
                            isBallVisible = (visibilityFlag == 1);
                        }

                        // Geschwindigkeitswerte empfangen
                        if (values.Length >= 5 &&
                            float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float velX) &&
                            float.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float velZ))
                        {
                            // Direkt die Geschwindigkeitswerte aus Python verwenden
                            RawVelocityX = velX;
                            RawVelocityZ = velZ;

                            // Normalisierte Geschwindigkeit (angepasst an das Geschwindigkeitsskalenkonzept)
                            ApproximatedVelocityX = Mathf.Clamp(velX / velocityScale, -1f, 1f);
                            ApproximatedVelocityZ = Mathf.Clamp(velZ / velocityScale, -1f, 1f);
                        }

                        newDataAvailable = true;
                    }
                    else
                    {
                        Debug.LogWarning("Ungültiges Datenformat empfangen: " + receivedData);
                    }

                    // Bestätigung senden
                    byte[] response = Encoding.UTF8.GetBytes("Received");
                    stream.Write(response, 0, response.Length);
                }
            }
            catch (SocketException ex)
            {
                Debug.LogWarning("Socket-Fehler: " + ex.Message);
                // Kurze Pause bei Verbindungsproblemen
                Thread.Sleep(100);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Serverfehler: " + ex.Message);
                // Kurze Pause bei allgemeinen Fehlern
                Thread.Sleep(100);
            }
            finally
            {
                // Ressourcen aufräumen
                stream?.Close();
                client?.Close();
            }
        }
    }

    void Update()
    {
        // Daten vom Netzwerk-Thread verarbeiten
        if (newDataAvailable)
        {
            ProcessNewData();
            newDataAvailable = false;
        }

        // Ball-Position interpolieren
        ball.transform.position = Vector3.Lerp(
            ball.transform.position,
            targetPosition,
            Time.deltaTime * interpolationSpeed
        );

        // Geschwindigkeit aus Positionsänderungen berechnen
        //CalculateVelocityFromPositionHistory();
    }

    private void ProcessNewData()
    {
        // Statische Variablen aktualisieren (für externe Zugriffe)
        NormalizedX = normX;
        NormalizedZ = normZ;
        IsBallVisible = isBallVisible;

        // Plane-Dimensionen berechnen
        float width = plane.localScale.x;
        float height = plane.localScale.z;

        float posX, posZ;

        if (useClamp)
        {
            // Option 1: Begrenzte Bewegung
            float clampedX = Mathf.Clamp(normX, -1f, 1f);
            float clampedZ = Mathf.Clamp(normZ, -1f, 1f);
            posX = clampedX * (width / 2) + plane.position.x;
            posZ = clampedZ * (height / 2) + plane.position.z;
        }
        else
        {
            // Option 2: Unbegrenzte Bewegung
            posX = normX * (width / 2) + plane.position.x;
            posZ = normZ * (height / 2) + plane.position.z;
        }

        // Zielposition aktualisieren (y-Koordinate beibehalten)
        targetPosition = new Vector3(posX, ball.transform.position.y, posZ);

        // Aktuelle Position für Geschwindigkeitsberechnung speichern
        Vector2 currentPosition = new Vector2(normX, normZ);
        float currentTime = Time.time;

        positionHistory.Enqueue(currentPosition);
        timeHistory.Enqueue(currentTime);

        // History-Größe begrenzen
        if (positionHistory.Count > HISTORY_SIZE)
        {
            positionHistory.Dequeue();
            timeHistory.Dequeue();
        }

        // Ball-Erscheinungsbild basierend auf Sichtbarkeit ändern
        UpdateBallAppearance();
    }

    private void UpdateBallAppearance()
    {
        if (ballRenderer != null)
        {
            ballRenderer.material.color = isBallVisible ? visibleBallColor : invisibleBallColor;
        }
    }

    private void CalculateVelocityFromPositionHistory()
    {
        if (positionHistory.Count < 2) return;

        Vector2[] positions = positionHistory.ToArray();
        float[] times = timeHistory.ToArray();

        // Umrechnung von normalisierten Koordinaten in Meter
        float realWorldScaleX = tableWidth / 2f;
        float realWorldScaleZ = tableHeight / 2f;

        float velocityX = 0f;
        float velocityZ = 0f;
        float totalWeight = 0f;

        // Wichtig: Die Indizes so anpassen, dass die neuesten Datenpunkte 
        // die höchsten Gewichte bekommen
        for (int i = positions.Length - 1; i > 0; i--)
        {
            // Bestimme den Index für das Gewicht (neueste Messungen zuerst)
            int weightIndex = positions.Length - 1 - i;

            // Position in Metern
            float posXDiff = (positions[i].x - positions[i - 1].x) * realWorldScaleX;
            float posZDiff = (positions[i].y - positions[i - 1].y) * realWorldScaleZ;

            // Zeit in Sekunden
            float timeDiff = times[i] - times[i - 1];

            if (timeDiff > 0)
            {
                // Geschwindigkeit in Meter pro Sekunde
                float vx = posXDiff / timeDiff;
                float vz = posZDiff / timeDiff;

                if (weightIndex < weights.Length)
                {
                    velocityX += vx * weights[weightIndex];
                    velocityZ += vz * weights[weightIndex];
                    totalWeight += weights[weightIndex];
                }
            }
        }

        // Durchschnitt berechnen
        if (totalWeight > 0)
        {
            velocityX /= totalWeight;
            velocityZ /= totalWeight;
        }

        // Speichere die Rohgeschwindigkeiten (in m/s)
        RawVelocityX = velocityX;
        RawVelocityZ = velocityZ;

        // Normalisieren wie im Training (-30 bis 30 → -1 bis 1)
        ApproximatedVelocityX = Mathf.Clamp(velocityX / velocityScale, -1f, 1f);
        ApproximatedVelocityZ = Mathf.Clamp(velocityZ / velocityScale, -1f, 1f);

        // Debug-Logs nur bei Bedarf aktivieren
        // Debug.Log($"Normalisierte Geschwindigkeit: X={ApproximatedVelocityX}, Z={ApproximatedVelocityZ}");
    }

    void OnApplicationQuit()
    {
        running = false;
        listener?.Stop();
    }
}