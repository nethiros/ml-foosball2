using System.Collections;
using System.Collections.Generic;
using System.Linq; // Added for Sum method
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using TMPro;
using static UnityEngine.GraphicsBuffer;

public class AgentTorwart : Agent
{

    // Füge dies zu den anderen Header-Variablen am Anfang der Klasse hinzu:
    [Header("Observation Noise")]
    [SerializeField] private float observationNoiseStdDev = 0.01f; // Standardabweichung für Gauss'sches Rauschen

    [Header("Ball Position Tracking")]
    [SerializeField] public bool useRealBallData = false; // Checkbox für Datenquelle

    [Header("Action Smoothing")]
    [SerializeField] private float actionSmoothingFactor = 0.3f;    // Smoothing der Bewegungen (Aktuell noch deutlich ausbaufähig)
    private float lastMoveInput = 0f;

    [Header("Shooting Mode")]
    private bool useDiscreteAngles = true; // When false, use hardcoded shot (auf true setzen, weil andere umsetzung aktuell nicht funktioniert)
    [SerializeField] private float maxAngularVelocity = 22f; // Maximum rotation speed for discrete angles

    [Header("Goalkeeper Figure")]
    // Add to AgentTorwart class variables
    [SerializeField] private GameObject goalkeeperPosition; // Reference to position tracker

    [Header("Continuous Positioning Reward")]
    [SerializeField] private float maxPositioningReward = 0.01f; // Max positive Belohnung bei Distanz 0
    [SerializeField] private float positioningPenaltyFactor = 0.5f; // Wie stark die Strafe ansteigt
    [SerializeField] private float positioningRewardFalloffFactor = 5.0f; // Wie schnell die Belohnung abfällt (größer = schneller)
    [SerializeField] private float minBallSpeedForPrediction = 1.0f; // Mindestgeschwindigkeit für Vorhersage

    // Maximale Größe für den Buffer definieren
    [SerializeField] private int maxBufferSize = 100;

    [Header("Ball Information Displays")]
    [SerializeField] private TextMeshProUGUI posXText;  //BallpositionX
    [SerializeField] private TextMeshProUGUI posZText;  //BallpositionZ
    [SerializeField] private TextMeshProUGUI velocityXText; // Geschwindigket
    [SerializeField] private TextMeshProUGUI velocityZText; // Geschwindigkeit
    [SerializeField] private TextMeshPro debugInformation; //Textanzeige (UI)

    // Neue Klassenvariablen am Anfang der AgentTorwart-Klasse hinzufügen
    [Header("Debug Controls")]
    [SerializeField] private bool enableDebugCycling = true;    //Anzeige für Debugging
    private string[] observationNames;  //Obersationsnamen
    private float[] observationValues;  //Observationswerte
    private int currentObservationIndex = 0;
    private bool debugInitialized = false;

    [Header("Reward Display")]
    [SerializeField] private TextMeshPro currentStepRewardText; //Anzeige des aktuellen Rewards der Episode
    [SerializeField] private TextMeshPro rewardText;
    private float displayedReward = 0f;

    [Header("Reward History")]
    [SerializeField] private TextMeshPro cumulativeRewardText; // Kumulative Belohnung Anzeuge
    [SerializeField] private int rewardHistorySize = 10; // Episodenanzahl zur Berechnung der durchschnittlichen Rewards
    private Queue<float> rewardHistory = new Queue<float>(); // Store recent episode rewards
    private float lastEpisodeReward = 0f; // Store the reward from the last episode
    [SerializeField] private TextMeshPro lastEpisodeRewardText; // Anzeige für Reward der letzten Episode

    [Header("Gaussian Shot Distribution")]
    [SerializeField] private float gaussianShotWidth = 0.2f; // Initial width (will be controlled by curriculum)
    [SerializeField] private float shotProbabilityPerSecond = 0.5f; // Base probability per second
    [SerializeField] private float shotPositionTolerance = 0.1f; // Tolerance for shot positions

    [Header("Areas")]
    [HideInInspector] public int currentBallFrontZone = 0; // 0 = keine Zone, 1 = Zone 1, 2 = Zone 2, etc.
    [HideInInspector] public int ballInFrontOfGoalZone = 0; // Variable im Agenten
    [SerializeField] private GameObject SpawnArea1;    // Bereich für Direktschüsse
    [SerializeField] private GameObject SpawnArea2;    // Bereich für verzögerte Schüsse #1
    [SerializeField] private GameObject SpawnArea3;    // Bereich für verzögerte Schüsse #2
    [SerializeField] private GameObject[] TargetAreas; // Zielbereiche für Schüsse
    [SerializeField] private GameObject Torwart;
    [SerializeField] private GameObject PlayArea;      // Referenz auf das PlayArea-Objekt

    [Header("Latency Simulation")]
    [SerializeField] private bool simulateLatency = true;
    // Ersetzt: [SerializeField] private float simulatedLatencySeconds = 0.015f;
    [SerializeField] private float minSimulatedLatencySeconds = 0.035f; // 35ms
    [SerializeField] private float maxSimulatedLatencySeconds = 0.060f; // 60ms
    private Queue<BallDataFrame> ballPositionBuffer = new Queue<BallDataFrame>();
    private float bufferUpdateTimer = 0f;
    private float bufferUpdateInterval = 0.005f;

    [Header("Ball & Rod Settings")]
    [SerializeField] private GameObject Ball; 
    [SerializeField] private GameObject realBall;
    [SerializeField] private float minBallSpeed = 5f;
    [SerializeField] private float maxBallSpeed = 30f;  
    [SerializeField] private float maxRodSpeed = 8f;   // Nur lineare Geschwindigkeit
    [SerializeField] private float velocityScale = 30f; // Skalierungsfaktor wie in BallReceive.txt [cite: 291]


    [SerializeField] private bool visualizeTrajectory = false; // Debug option to visualize the trajectory

    private GameObject trajectoryLine; // For debug visualization

    // Predicted position where ball will cross the goalkeeper line
    private float predictedCrossingZ = float.NaN; // NaN = keine gültige Vorhersage

    // Private Variablen
    // Curriculum-gesteuerte Standardabweichung für die Trigger-Z-Position
    private float currentTriggerZStdDev = 0.1f; // Standardwert für den Fall, dass Curriculum nicht lädt

    // Die für die aktuelle Episode (Situation 2) gewählte normalisierte Trigger-Z-Position
    private float episodeTriggerZ = float.NaN;

    private float lastPositioningReward = 0f; // Neue Variable für den posReward
    private float currentZScaleFactorMax = 0.3f; // Standardwert, falls kein Curriculum aktiv
    private int currentDirectShotTargetIndex = 0; // Standardwert
    private float lastStepReward = 0f;
    private MoveRodTorwart moveRodTorwart;
    private Coroutine delayedShotCoroutine = null;
    private int currentEpisodeId = 0;    // Aktuelle Episode-ID für Coroutine-Tracking
    private Rigidbody ball_rb;
    private float lastShotTime = -10f;   // Zeitpunkt des letzten Schusses, initial weit in der Vergangenheit
    private float shotCooldown = 1.0f;   // Cooldown-Zeit für den Schuss

    private int ballInShotzone = 0; //Platzhalter, kann in Zukunft verwendet werden zur Auskunft wo sich der Ball befindet
    private int ballInBehindGoalkeeper = 0; //Platzhalter, kann in Zukunft verwendet werden zur Auskunft wo sich der Ball befindet
    private int ballInImpactArea = 0; //Platzhalter, kann in Zukunft verwendet werden zur Auskunft wo sich der Ball befindet
    private int ballInFrontOfGoal = 0; //Platzhalter, kann in Zukunft verwendet werden zur Auskunft wo sich der Ball befindet

    // Status-Variablen
    public bool busyRespawning; //Variable, die dafür sorgt, dass beim Respawnen des Balles keine der OnTriggerExit Methoden aufgerufen werden (es kommt sonst zu doppelten Respawns)
    public bool spawnedAt;               // Zeigt an, welche Spawnsituation genutzt wurde
    public bool trackingBall;
    public float cumulative = 0f;

        // Klasse zum Speichern von Ballpositionen und -geschwindigkeiten als innere Klasse
        private class BallDataFrame
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Timestamp;

        public BallDataFrame(Vector3 pos, Vector3 vel, float time)
        {
            Position = pos;
            Velocity = vel;
            Timestamp = time;
        }
    }

    void Start()
    {
        //Komponenten für später
        moveRodTorwart = Torwart.GetComponent<MoveRodTorwart>();
        ball_rb = Ball.GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Wird zum Start jeder neuen Episode aufgerufen
    /// </summary>

    public override void OnEpisodeBegin()
    {

        // Coroutinen-Management
        if (delayedShotCoroutine != null)
        {
            StopCoroutine(delayedShotCoroutine);
            delayedShotCoroutine = null;
        }

        // --- Curriculum Parameter auslesen ---
        if (Academy.IsInitialized) // Sicherstellen, dass die Academy bereit ist
        {
            // Lies die Werte aus den Umgebungsparametern, die durch die YAML-Datei gesetzt werden.
            // Nutze GetWithDefault, um Standardwerte zu haben, wenn die Parameter fehlen
            gaussianShotWidth = Academy.Instance.EnvironmentParameters.GetWithDefault("gaussian_shot_width", 0.2f);
            currentZScaleFactorMax = Academy.Instance.EnvironmentParameters.GetWithDefault("z_scale_factor_max", 0.3f);
            // Debug.Log($"Current required front zone: {currentRequiredFrontZone}");

            //Standardabweichung für Trigger-Z lesen
            currentTriggerZStdDev = Academy.Instance.EnvironmentParameters.GetWithDefault("trigger_z_stddev", 0.1f); // Standardwert 0.1


            // Sicherheitshalber prüfen, ob der Index gültig ist
            if (TargetAreas == null || TargetAreas.Length == 0)
            {
                Debug.LogError("TargetAreas sind nicht zugewiesen oder leer!");
                currentDirectShotTargetIndex = 0; // Fallback
            }
            else if (currentDirectShotTargetIndex < 0 || currentDirectShotTargetIndex >= TargetAreas.Length)
            {
                Debug.LogWarning($"Ungültiger direct_shot_target_index ({currentDirectShotTargetIndex}) vom Curriculum erhalten. Nutze Index 0.");
                currentDirectShotTargetIndex = 0; // Fallback auf sicheren Index
            }
            // Debug Ausgabe (optional)
            // Debug.Log($"Lesson Params: zScaleMax={currentZScaleFactorMax}, targetIndex={currentDirectShotTargetIndex}");
        }
        // --- Ende Curriculum Parameter ---

        episodeTriggerZ = float.NaN;

        // Coroutine stoppen, falls sie noch läuft
        if (delayedShotCoroutine != null)
        {
            StopCoroutine(delayedShotCoroutine);
            delayedShotCoroutine = null;
        }

        // Anzeigen aktualisieren
        UpdateRewardHistoryDisplay();

        lastStepReward = 0;
        currentStepRewardText.text = $"Step Reward: {lastStepReward:F4}";

        // Reset reward
        cumulative = 0f;

        //Der Ball respawnt und darf KEINE ontriggerexit Funktionen aufrufen (ansonsten kommt es zu problemen mit der respawnlogik)
        busyRespawning = true; 
        SpawnBall();    // Ball spwanen
        RandomisePosition();    // Aktuell Platzhalter. Kann später hinzugefügt werden, um Stangen in zufällige Anfangsposition zu bringen. Unsicher, ob das sinvoll ist

        // Schuss-Cooldown zurücksetzen
        lastShotTime = -10f;

        // Smoothing-Variablen zurücksetzen
        lastMoveInput = 0f;
    }

    /// <summary>
    /// Initialisiert das Debug-Array mit allen Observationen
    /// </summary>
    private void InitializeDebugObservations()
    {
        // Liste aller zu beobachtenden Werte und ihrer Namen definieren
        observationNames = new string[]
        {
        "Ball Position X",
        "Ball Position Z",
        "Ball Velocity X",
        "Ball Velocity Z",
        "Keeper Position",
        "Keeper Rotation",
        "Position Reward",
        "Ball in Impact"
        // Füge weitere Observationen hier hinzu
        };

        // Array für die entsprechenden Werte erstellen
        observationValues = new float[observationNames.Length];
        debugInitialized = true;
    }

    /// <summary>
    /// Aktualisiert die Observation-Werte im Debug-Array
    /// </summary>
    private void UpdateDebugObservations()
    {
        if (!debugInitialized)
            InitializeDebugObservations();

        // Hole aktuelle Ball-Position und Geschwindigkeit
        Vector3 ballPos, ballVel;
        if (useRealBallData)
        {
            ballPos = new Vector3
                (BallPositionReceiverUDP.NormalizedX,
                0f,
                BallPositionReceiverUDP.NormalizedZ
            );
            ballVel = new Vector3(
                BallPositionReceiverUDP.ApproximatedVelocityX,
                0f,
                BallPositionReceiverUDP.ApproximatedVelocityZ
            );
        }
        else
        {
            (ballPos, ballVel) = GetDelayedBallData();
        }

        // Abstand zwischen Torwart und Ball berechnen
        float keeperToBallDistance = Mathf.Abs(goalkeeperPosition.transform.position.z - ballPos.z);

        // Torwart-Position und -Rotation
        float torPos, torRot;
        (torPos, torRot) = moveRodTorwart.GetPosAndRot();

        // Werte in unser Array schreiben
        observationValues[0] = ballPos.x;
        observationValues[1] = ballPos.z;
        observationValues[2] = ballVel.x;
        observationValues[3] = ballVel.z;
        observationValues[4] = torPos;
        observationValues[5] = torRot;
        observationValues[6] = lastPositioningReward;
        observationValues[7] = ballInImpactArea;
        // Weitere Observationen hier hinzufügen
    }

    /// <summary>
    /// Berechnet die Z-Koordinate, an der der Ball voraussichtlich die Torlinie (X-Position des Torwarts) kreuzt.
    /// Gibt float.NaN zurück, wenn keine sinnvolle Vorhersage möglich ist.
    /// </summary>
    /// <returns>Die vorhergesagte Z-Koordinate oder float.NaN</returns>
    private float CalculatePredictedCrossingZ()
    {
        Vector3 ballPosition;
        Vector3 ballVelocity;

        // Wähle die richtige Ballquelle (Simulation oder Echtzeit) und hole Daten (ggf. verzögert)
        if (useRealBallData)
        {
            // Annahme: realBall ist das GameObject für den echten Ball
            if (realBall == null) return float.NaN; // Sicherstellen, dass Referenz existiert
            ballPosition = realBall.transform.position;
            // Verwende die approximierten Geschwindigkeiten aus BallPositionReceiver
            ballVelocity = new Vector3(
               BallPositionReceiver.ApproximatedVelocityX,
               0f, // Ignoriere Y-Geschwindigkeit für die 2D-Vorhersage
               BallPositionReceiver.ApproximatedVelocityZ
           );
        }
        else
        {
            if (Ball == null || ball_rb == null) return float.NaN; // Sicherstellen, dass Referenzen existieren
                                                                   // Hole ggf. verzögerte Daten aus dem Puffer
            (ballPosition, ballVelocity) = GetDelayedBallData();
        }


        // Ignoriere sehr langsame Bälle oder reine vertikale/horizontale Bewegungen (X ist relevant, also die Bewegung zum Tor)
        if (ballVelocity.magnitude < minBallSpeedForPrediction || Mathf.Abs(ballVelocity.x) < 0.1f)
        {
            return float.NaN; // Keine zuverlässige Vorhersage möglich
        }

        // Torwart-X-Position (Tiefe im Feld)
        if (Torwart == null) return float.NaN; // Sicherstellen, dass Referenz existiert
        float goalkeeperX = Torwart.transform.position.x;

        // Distanz, die der Ball in X-Richtung zurücklegen muss
        float distanceToGoalkeeperX = goalkeeperX - ballPosition.x;

        // Prüfen, ob sich der Ball auf das Tor zubewegt (oder zumindest nicht klar davon weg)
        // Ball bewegt sich nach vorne (negatives X) und ist noch davor (positives distanceToGoalkeeperX)
        // ODER Ball bewegt sich nach hinten (positives X) und ist schon dahinter (negatives distanceToGoalkeeperX) -> Auch dies könnte relevant sein, wenn der Ball ZUERST hinter den TW kam und nun zurückkommt.
        // Einfacher: Wenn die Geschwindigkeit in X-Richtung 0 ist oder in die "falsche" Richtung zeigt relativ zur Distanz, ist keine Kreuzung zu erwarten.
        if (ballVelocity.x < 0 && distanceToGoalkeeperX != 0)
        {
            // Ball bewegt sich von der Torlinie weg (gleiches Vorzeichen für Velocity.x und Distanz)
            return float.NaN;
        }

        // Zeit bis zum Erreichen der Torwart-X-Position
        // Vermeide Division durch Null, obwohl der Check oben das meiste abfangen sollte
        if (Mathf.Approximately(ballVelocity.x, 0f)) return float.NaN;
        float timeToReachGoalkeeper = distanceToGoalkeeperX / ballVelocity.x;

        // Wenn die Zeit negativ ist, ist der Ball bereits über die Linie hinaus (aus Sicht der Ballrichtung)
        if (timeToReachGoalkeeper < 0) return float.NaN;


        // Berechne die Position, an der der Ball die Linie kreuzen wird
        Vector3 predictedPosition = ballPosition + ballVelocity * timeToReachGoalkeeper;

        // Visualisierung (optional, kann hier oder woanders aufgerufen werden)
        if (visualizeTrajectory)
        {
            VisualizeTrajectory(ballPosition, predictedPosition);
        }

        return predictedPosition.z; // Nur die Z-Koordinate ist relevant für die Positionierung
    }

    //Gaussche Funktion
    // Helper function to generate a random number from a standard normal distribution (mean 0, stddev 1)
    // Using Box-Muller transform
    private static float GenerateStandardNormal()
    {
        // Use UnityEngine.Random.value which is [0, 1]
        float u1 = 1.0f - UnityEngine.Random.value; // Avoid log(0)
        float u2 = UnityEngine.Random.value;
        // Standard Box-Muller transform
        float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
        return randStdNormal;
    }

    // Function to generate Gaussian random number with specific mean and stddev
    public static float GenerateGaussian(float mean, float stdDev)
    {
        float stdNormal = GenerateStandardNormal();
        float randNormal = mean + stdDev * stdNormal;
        return randNormal;
    }

    /// <summary>
    /// Erweiterte Debug-Methode mit Navigation durch Observations
    /// </summary>
    private void debugText(float input, string label = "")
    {
        if (!enableDebugCycling)
        {
            // Alte Funktionalität beibehalten
            debugInformation.text = $"{label}: {input:F3}";
            return;
        }

        // Observations aktualisieren
        UpdateDebugObservations();

        // Input-Verarbeitung für Navigation
        if (Input.GetKeyDown(KeyCode.L))
        {
            // Nach oben navigieren
            currentObservationIndex = (currentObservationIndex - 1 + observationNames.Length) % observationNames.Length;
        }
        else if (Input.GetKeyDown(KeyCode.O))
        {
            // Nach unten navigieren
            currentObservationIndex = (currentObservationIndex + 1) % observationNames.Length;
        }

        // Debug-Text mit aktuellem Index und Info anzeigen
        string currentName = observationNames[currentObservationIndex];
        float currentValue = observationValues[currentObservationIndex];

        debugInformation.text = $"[{currentObservationIndex + 1}/{observationNames.Length}] {currentName}: {currentValue:F3}\n(O: Up, L: Down)";
    }

    // Rufe diese Methode in Update auf, um auch ohne direkten Aufruf von debugText die Navigation zu ermöglichen
    private void UpdateDebugNavigation()
    {
        if (enableDebugCycling && debugInformation != null)
        {
            debugText(0f);
        }
    }

    private void UpdateRewardHistoryDisplay()
    {
        // Update last episode reward display
        if (lastEpisodeRewardText != null)
            lastEpisodeRewardText.text = $"Last Episode: {lastEpisodeReward:F2}";

        // Calculate average of recent rewards
        float averageReward = 0f;
        if (rewardHistory.Count > 0)
            averageReward = rewardHistory.Sum() / rewardHistory.Count;

        // Update cumulative reward display
        if (cumulativeRewardText != null)
            cumulativeRewardText.text = $"Avg ({rewardHistorySize}): {averageReward:F2}";
    }

    /// <summary>
    /// Bereiche in denen sich der Ball befindet. Wird aktuell nicht verwendet.
    /// </summary>

    public void UpdateZoneStatus(int shotzone, int behindGoalkeeper, int impactArea, int frontOfGoalZoneID)
    {
        ballInShotzone = shotzone;
        ballInBehindGoalkeeper = behindGoalkeeper;
        ballInImpactArea = impactArea;
        ballInFrontOfGoalZone = frontOfGoalZoneID;
    }

    /// <summary>
    /// Episode beenden und Rewards für Anzeige loggen
    /// </summary>

    private void EndEpisodeWithReward()
    {
        lastEpisodeReward = cumulative;

        // Add to reward history
        if (rewardHistory.Count >= rewardHistorySize)
            rewardHistory.Dequeue();
        rewardHistory.Enqueue(lastEpisodeReward);

        // Now end the episode
        EndEpisode();
    }

    /// <summary>
    /// Aktualisiert das Textfeld für die Belohnung des letzten Schritts.
    /// </summary>
    private void UpdateCurrentStepRewardDisplay()
    {
        if (currentStepRewardText != null)
        {
            // Zeige die zuletzt gespeicherte Schritt-Belohnung an
            currentStepRewardText.text = $"Step Reward: {lastStepReward:F4}"; // 4 Nachkommastellen für kleine Belohnungen
        }
    }

    /// <summary>
    /// Reward hinzufügen und Anzeige aktualisieren
    /// </summary>
    /// 

    public new void AddReward(float reward)
    {
        lastStepReward += reward;
        UpdateCurrentStepRewardDisplay(); // Anzeige aktualisieren

        base.AddReward(reward);  // Rufe die ursprüngliche Methode auf
        cumulative += reward;    // Add to cumulative counter for this episode
    }

    /// <summary>
    /// Trajektorie anzeigen lassen des Balles
    /// </summary>
    private void VisualizeTrajectory(Vector3 startPoint, Vector3 endPoint)
    {
        // Clean up previous line if it exists
        if (trajectoryLine != null)
        {
            Destroy(trajectoryLine);
        }

        // Create a new line game object
        trajectoryLine = new GameObject("TrajectoryLine");
        LineRenderer lineRenderer = trajectoryLine.AddComponent<LineRenderer>();

        // Configure the line
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);

        // Set material to a default material
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = Color.red;

        // Destroy after 1 second
        Destroy(trajectoryLine, 1.0f);
    }

    /// <summary>
    /// Normalisiert eine Position relativ zum Spielfeld auf einen Wert zwischen -1 und 1. Nur notwendig für simulierten Ball! Der "echte" Ball wurde bereits normalisiert
    /// </summary>
    private float NormalizePosition(float position, bool isXAxis)
    {
        if (PlayArea == null)
        {
            Debug.LogError("PlayArea nicht gesetzt!");
            return 0f;
        }

        // Ermitteln der Spielfeldgrenzen
        float halfWidth = PlayArea.transform.localScale.x / 2f;
        float halfHeight = PlayArea.transform.localScale.z / 2f;

        // Offset des Spielfelds
        float centerX = PlayArea.transform.position.x;
        float centerZ = PlayArea.transform.position.z;

        // Normalisierung
        float normalizedValue = isXAxis
            ? (position - centerX) / halfWidth
            : (position - centerZ) / halfHeight;

        // Sicherstellen, dass der Wert zwischen -1 und 1 liegt
        return Mathf.Clamp(normalizedValue, -1f, 1f);
    }

    /// <summary>
    /// Normalisiert eine Geschwindigkeit auf einen Wert zwischen -1 und 1. Nur notwendig für simulierten Ball!
    /// </summary>
    private float NormalizeVelocity(float velocity)
    {
        // Normalisierung mit demselben Skalierungsfaktor wie in BallPositionReceiverUDP
        return Mathf.Clamp(velocity / velocityScale, -1f, 1f); // Verwende velocityScale um Geschwindigkeit zu skalieren. Wichtig: Wert muss bei realen Ball GLEICH sein
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float ballPosX, ballPosZ, ballVelX, ballVelZ;

        if (useRealBallData) //Echter Ball
        {
            // Normierte Daten vom BallPositionReceiver
            ballPosX = BallPositionReceiverUDP.NormalizedX;
            ballPosZ = BallPositionReceiverUDP.NormalizedZ;
            ballVelX = BallPositionReceiverUDP.ApproximatedVelocityX;
            ballVelZ = BallPositionReceiverUDP.ApproximatedVelocityZ;

            // Abstand zwischen Torwart und Ball berechnen (nur Z-Achse/horizontale Position) Aktuell entfernt entfernt, da es Probleme gemacht hat
            // float keeperToBallDistance = Mathf.Abs(goalkeeperPosition.transform.position.z - realBall.transform.position.z);
            // Normalize the distance relative to field width
            // float normalizedDistance = keeperToBallDistance / (PlayArea.transform.localScale.z * 0.5f);
            // normalizedDistance = Mathf.Clamp01(normalizedDistance); // Ensure it's between 0-1
            // sensor.AddObservation(normalizedDistance); Ist zwar sinnvoll, muss jedoch anders umgesetzt werden (direkt mit UDP Daten). Diese Methode hat möglicherweise zu hohe Latenz 
        }
        else    //Simulierter Ball
        {
            // Simulationsdaten mit optionaler Verzögerung
            Vector3 ballPos, ballVel;
            (ballPos, ballVel) = GetDelayedBallData();

            // Simulationsdaten normalisieren
            ballPosX = randomizeNumber(NormalizePosition(ballPos.x, true));
            ballPosZ = randomizeNumber(NormalizePosition(ballPos.z, false));
            ballVelX = randomizeNumber(NormalizeVelocity(ballVel.x));
            ballVelZ = randomizeNumber(NormalizeVelocity(ballVel.z));

            // Abstand zwischen Torwart und Ball berechnen (nur Z-Achse/horizontale Position) Aktuell entfernt entfernt, da es Probleme gemacht hat
            // float keeperToBallDistance = Mathf.Abs(goalkeeperPosition.transform.position.z - Ball.transform.position.z);
            // Normalize the distance relative to field width
            // float normalizedDistance = keeperToBallDistance / (PlayArea.transform.localScale.z * 0.5f);
            // normalizedDistance = Mathf.Clamp01(normalizedDistance); // Ensure it's between 0-1
            // sensor.AddObservation(normalizedDistance); Synchron entfernen, weil Umsetzung mit echten Ball aktuell zu hohe Latenz hat
        }

        // Text-Displays aktualisieren
        UpdateBallInfoTexts(ballPosX, ballPosZ, ballVelX, ballVelZ);

        // Zonendaten hinzufügen
        // sensor.AddObservation(ballInBehindGoalkeeper);  // Ball in BehindGoalkeeper (0 oder 1)
        sensor.AddObservation(ballInImpactArea);        // Ball in ImpactArea (0 oder 1)

        //Balldaten
        sensor.AddObservation(ballPosX);               // Ball Position X
        sensor.AddObservation(ballPosZ);               // Ball Position Z
        sensor.AddObservation(ballVelX);               // Ball Geschwindigkeit X
        sensor.AddObservation(ballVelZ);               // Ball Geschwindigkeit Z

        // Spieler-Informationen
        float torPos;
        float torRot;
        (torPos, torRot) = moveRodTorwart.GetPosAndRot();
        sensor.AddObservation(torPos);                 // Torwart Position 
        sensor.AddObservation(torRot);                 // Torwart Rotation

        //Für früheren Ansteuerungsansatz mit hardgecodeten Schuss. Aktuell nicht mehr benötigt
        // Schuss-Status hinzufügen (0 = idle, 1 = aktiv)
        // sensor.AddObservation(moveRodTorwart.GetShotState()); Wird nicht mehr genutzt

        // Cooldown-Status hinzufügen (0 = bereit, 1 = nicht bereit)
        // float cooldownStatus = Time.time - lastShotTime < shotCooldown ? 1.0f : 0.0f;
        // sensor.AddObservation(cooldownStatus);
    }

    /// <summary>
    /// Fügt kleine Zufallsschwankungen (Gauss'sches Rauschen) zu normalisierten Werten hinzu, um Rauschen zu simulieren
    /// </summary>
    private float randomizeNumber(float input)
    {
        // Generiere Gauss'sches Rauschen mit Mittelwert 0 und konfigurierter Standardabweichung
        float noise = GenerateGaussian(0f, observationNoiseStdDev);
        // Addiere Rauschen und klemme das Ergebnis auf den Bereich [-1, 1]
        float output = Mathf.Clamp(input + noise, -1f, 1f);
        return output;
    }

    /// <summary>
    /// Aktualisiert die TextMeshPro-Anzeigen mit den Ball-Informationen
    /// </summary>
    private void UpdateBallInfoTexts(float posX, float posZ, float velX, float velZ)
    {
        if (posXText != null)
            posXText.text = $"Position X: {posX:F3}";

        if (posZText != null)
            posZText.text = $"Position Z: {posZ:F3}";

        if (velocityXText != null)
            velocityXText.text = $"Velocity X: {velX:F3}";

        if (velocityZText != null)
            velocityZText.text = $"Velocity Z: {velZ:F3}";
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Kontinuierliche Aktion für horizontale Bewegung mit Smoothing
        float rawMoveInput = actions.ContinuousActions[0];
        float moveInputTorwart = Mathf.Lerp(lastMoveInput, rawMoveInput, actionSmoothingFactor);
        lastMoveInput = moveInputTorwart;

        // Diskrete Aktion für Geschwindigkeit OHNE zufälliges Smoothing
        int rawSpeedLevel = actions.DiscreteActions[0];
        int speedLevel = rawSpeedLevel; // Einfach die gewählte Aktion übernehmen

        float movementSpeed;
        switch (speedLevel)
        {
            case 0: // Geschwindigkeitsstufe 0 - Langsam
                movementSpeed = 0.1f * maxRodSpeed;
                break;
            case 1: // Geschwindigkeitsstufe 1 - Mittelschnell
                movementSpeed = 0.5f * maxRodSpeed;
                break;
            case 2: // Geschwindigkeitsstufe 2 - Schnell
                movementSpeed = 1f * maxRodSpeed;
                break;
            default:
                movementSpeed = 0.5f * maxRodSpeed;
                break;
        }
        //Diskrete Rotationssteuerung
        if (useDiscreteAngles)
        {
            // Diskrete Aktionen für Rotation bei Verwendung diskreter Winkel
            float angularVel = 0f;
            switch (actions.DiscreteActions[1])
            {
                case 0:
                    angularVel = 0.1f * maxAngularVelocity;
                    break;
                case 1:
                    angularVel = 0.5f * maxAngularVelocity;
                    break;
                case 2:
                    angularVel = 1.0f * maxAngularVelocity;
                    break;
            }

            float angularPos = 0f;
            switch (actions.DiscreteActions[2])
            {
                case 0:
                    angularPos = 0f;
                    break;
                case 1:
                    angularPos = -0.2f;
                    break;
                case 2:
                    angularPos = 0.2f;
                    break;
                case 3:
                    angularPos = -0.45f;
                    break;
                case 4:
                    angularPos = 0.45f;
                    break;
                case 5:
                    angularPos = -0.9f;
                    break;
                case 6:
                    angularPos = 0.9f;
                    break;
            }

            // Bestrafung für falsche Rotation außerhalb der ImpactArea
            if (useDiscreteAngles && ballInImpactArea == 0)
            {
                // Prüfen, ob eine Rotation ungleich 0 Grad gewählt wurde (actions.DiscreteActions[2] != 0)
                if (actions.DiscreteActions[2] != 0)
                {
                    // Existenzielle Bestrafung für falsche Grundstellung
                    AddReward(-0.001f);
                }
            }

            // --- Kontinuierliche Positionierungsbelohnung / Bestrafung ---
            if (!float.IsNaN(predictedCrossingZ) && goalkeeperPosition != null) // Nur wenn Vorhersage gültig ist
            {
                float keeperZ = goalkeeperPosition.transform.position.z;
                float distanceZ = Mathf.Abs(keeperZ - predictedCrossingZ);

                // Berechne die Belohnung/Bestrafung: Max Belohnung bei 0 Distanz, quadratisch fallend ins Negative
                // Beispiel: reward = 0.01 - 0.5 * distanceZ^2
                float reward = maxPositioningReward - positioningPenaltyFactor * distanceZ * distanceZ;

                // Optional: Clampen, um extreme Strafen zu vermeiden (z.B. nicht kleiner als -0.1)
                reward = Mathf.Clamp(reward, -0.05f, maxPositioningReward);

                AddReward(2f*reward); // Füge die Belohnung/Bestrafung hinzu
                lastPositioningReward = reward; // Update für Debugging
            }
            else // Optional: Kleine Strafe, wenn keine Vorhersage möglich ist, aber der Ball relevant ist?
            {
                lastPositioningReward = 0f; // Kein Reward, wenn keine Vorhersage
            }

            // In AgentTorwart.cs in der OnActionReceived Methode bei useDiscreteAngles
            moveRodTorwart.SetTargetsAndSpeeds(
                movementSpeed, moveInputTorwart,
                angularVel, angularPos, 0f); // shotSignal ist 0
        }
        else
        {
            // Original-Modus mit Hardcoded Schuss (Aktuell nicht auf aktuellsten Stand, also NICHT nutzen)
            bool triggerShot = actions.DiscreteActions[1] > 0;

            float shotSignal = 0f;
            if (triggerShot && Time.time - lastShotTime >= shotCooldown && moveRodTorwart.GetShotState() == 0)
            {
                shotSignal = 1f;
                lastShotTime = Time.time; // Cooldown starten, damit nicht permant "geschossen" werden kann
            }

            // Bewegungswerte mit hardcoded Schuss weitergeben
            moveRodTorwart.SetTargetsAndSpeeds(
                movementSpeed, moveInputTorwart,
                0f, 0f, shotSignal);
        }
    }

    /// <summary>
    /// Ermöglicht manuelle Steuerung für Tests und Debugging
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continousActions = actionsOut.ContinuousActions;
        continousActions[0] = Input.GetAxisRaw("Horizontal");

        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 2; // Maximale Geschwindigkeit für manuelles Testen

        if (useDiscreteAngles)
        {
            // Steuerung für diskrete Winkel
            discreteActions[1] = 2; // Hohe Winkelgeschwindigkeit

            if (Input.GetKey(KeyCode.Keypad0)) discreteActions[2] = 0;
            else if (Input.GetKey(KeyCode.Keypad1)) discreteActions[2] = 1;
            else if (Input.GetKey(KeyCode.Keypad2)) discreteActions[2] = 2;
            else if (Input.GetKey(KeyCode.Keypad3)) discreteActions[2] = 3;
            else if (Input.GetKey(KeyCode.Keypad4)) discreteActions[2] = 4;
            else if (Input.GetKey(KeyCode.Keypad5)) discreteActions[2] = 5;
            else if (Input.GetKey(KeyCode.Keypad6)) discreteActions[2] = 6;
        }
        else
        {
            // Standard-Schuss-Steuerung (Aktuell NICHT funktionsfähig Implementiert)
            discreteActions[1] = Input.GetKey(KeyCode.Space) ? 1 : 0; // Schuss mit Leertaste
        }
    }

    /// <summary>
    /// Spawnt den Ball und setzt eine Bewegung basierend auf der gewählten Situation
    /// </summary>
    private void SpawnBall()
    {

        // Sicherstellen, dass keine alte Coroutine mehr läuft
        if (delayedShotCoroutine != null)
        {
            StopCoroutine(delayedShotCoroutine);
            delayedShotCoroutine = null;
        }

        // Bestimmen, welche Situation verwendet werden soll (20% Situation 1, 80% Situation 2)
        float situationRoll = UnityEngine.Random.value;
        bool useSituation1 = situationRoll < 0.2f;

        if (useSituation1)
        {
            // Situation 1 - Direktschuss aufs Tor von etwas weiter hinten
            spawnedAt = false;
            ball_rb.angularVelocity = Vector3.zero;

            // SpawnArea1 verwenden
            Transform selectedSpawnArea = SpawnArea1.transform;
            // --- Zielbereich über Curriculum wählen ---
            if (TargetAreas == null || TargetAreas.Length <= currentDirectShotTargetIndex)
            {
                Debug.LogError("TargetAreas nicht korrekt konfiguriert für Curriculum!");
                EndEpisode(); // Episode beenden, da Konfiguration fehlt
                return;
            }

            Transform selectedTargetArea = TargetAreas[currentDirectShotTargetIndex].transform; // Index aus Curriculum

            // Zufällige Position im Spawnbereich
            float spawnX = UnityEngine.Random.Range(
                selectedSpawnArea.localPosition.x - selectedSpawnArea.localScale.x / 2,
                selectedSpawnArea.localPosition.x + selectedSpawnArea.localScale.x / 2);
            float spawnZ = UnityEngine.Random.Range(
                selectedSpawnArea.localPosition.z - selectedSpawnArea.localScale.z / 2,
                selectedSpawnArea.localPosition.z + selectedSpawnArea.localScale.z / 2);
            float spawnY = selectedSpawnArea.localPosition.y;

            Ball.transform.localPosition = new UnityEngine.Vector3(spawnX, spawnY, spawnZ);

            Rigidbody BallRB = Ball.GetComponent<Rigidbody>();

            // Zufällige Zielposition im Torbereich
            float targetX = UnityEngine.Random.Range(
                selectedTargetArea.localPosition.x - selectedTargetArea.localScale.x / 2,
                selectedTargetArea.localPosition.x + selectedTargetArea.localScale.x / 2);
            float targetZ = UnityEngine.Random.Range(
                selectedTargetArea.localPosition.z - selectedTargetArea.localScale.z / 2,
                selectedTargetArea.localPosition.z + selectedTargetArea.localScale.z / 2);

            // Geschwindigkeit berechnen - geradlinig auf das Ziel
            UnityEngine.Vector3 direction = new UnityEngine.Vector3(
                targetX - spawnX, 0, targetZ - spawnZ).normalized;
            BallRB.velocity = direction * UnityEngine.Random.Range(minBallSpeed, maxBallSpeed);
        }
        else
        {
            // Situation 2 - Vorbereitung und dann schneller Schuss
            spawnedAt = true;
            ball_rb.angularVelocity = Vector3.zero;

            episodeTriggerZ = GenerateGaussian(0f, currentTriggerZStdDev); // Mittelwert 0, stddev aus Curriculum
            // Begrenze den Wert auf den gültigen normalisierten Bereich [-1, 1]
            episodeTriggerZ = Mathf.Clamp(episodeTriggerZ, -1f, 1f);
            //Debug.Log($"Situation 2 selected. Waiting for ball to reach Z_norm = {episodeTriggerZ:F3} (StdDev: {currentTriggerZStdDev:F2})");

            // Zwischen SpawnArea2 und SpawnArea3 wählen (50/50) (links oder rechts einwerfen lassen)
            bool useSpawnArea2 = UnityEngine.Random.value < 0.5f;
            Transform selectedSpawnArea = useSpawnArea2 ? SpawnArea2.transform : SpawnArea3.transform;
            Transform selectedTargetArea = TargetAreas[1].transform;

            // Zufällige Position im gewählten Spawnbereich
            float spawnX = UnityEngine.Random.Range(
                selectedSpawnArea.localPosition.x - selectedSpawnArea.localScale.x / 2,
                selectedSpawnArea.localPosition.x + selectedSpawnArea.localScale.x / 2);
            float spawnZ = UnityEngine.Random.Range(
                selectedSpawnArea.localPosition.z - selectedSpawnArea.localScale.z / 2,
                selectedSpawnArea.localPosition.z + selectedSpawnArea.localScale.z / 2);
            float spawnY = selectedSpawnArea.localPosition.y;

            Ball.transform.localPosition = new UnityEngine.Vector3(spawnX, spawnY, spawnZ);

            Rigidbody BallRB = Ball.GetComponent<Rigidbody>();

            // Initiale langsame Bewegung in Z-Richtung
            float initialZVelocity = useSpawnArea2 ? 2.0f : -2.0f; // Positiv für SpawnArea2, negativ für SpawnArea3
            BallRB.velocity = new UnityEngine.Vector3(UnityEngine.Random.Range(-0.1f,0.1f), 0f, initialZVelocity);

            // Aktuelle Episode-ID für die Überprüfung speichern
            int episodeIdForCoroutine = currentEpisodeId;

            // Verzögerten Schuss zum Tor planen (nach ca. 0.5-1 Sekunden)
            delayedShotCoroutine = StartCoroutine(
                DelayedShot(0.1f, selectedTargetArea, episodeIdForCoroutine) // Kurze Startverzögerung, damit Physik greift
            );
        }

        // Ball zurücksetzen
        Ball.GetComponent<BallBehaviourTorwart>().resetBall();

    }

    private IEnumerator DelayedShot(float initialDelay, Transform targetArea, int episodeId)
    {
        // 1. Kurze initiale Wartezeit (optional, kann auch 0 sein)
        yield return new WaitForSeconds(initialDelay);

        // 2. Prüfen, ob wir noch in derselben Episode sind
        if (episodeId != currentEpisodeId)
        {
            Debug.LogWarning($"[{Time.frameCount}] DelayedShot: Episode changed ({currentEpisodeId}) before waiting. Aborting shot for old episode {episodeId}.");
            yield break;
        }

        // Prüfen, ob episodeTriggerZ gesetzt wurde (Sicherheitscheck)
        if (float.IsNaN(episodeTriggerZ))
        {
            Debug.LogError($"[{Time.frameCount}] DelayedShot Ep{episodeId}: episodeTriggerZ was not set! Aborting shot.");
            delayedShotCoroutine = null;
            yield break;
        }

        //Debug.Log($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Waiting for ball to reach Z_norm = {episodeTriggerZ:F3}...");
        float timeElapsed = 0f;
        float maxWaitTime = 7f; // Etwas längere Wartezeit, da Ball vielleicht langsamer ist
        float triggerTolerance = 0.15f; // Toleranz für die Z-Position (normalisiert)

        while (timeElapsed < maxWaitTime)
        {
            // Prüfen, ob die Episode während des Wartens gewechselt hat
            if (episodeId != currentEpisodeId)
            {
                Debug.LogWarning($"[{Time.frameCount}] DelayedShot: Episode changed ({currentEpisodeId}) while waiting for Z_norm={episodeTriggerZ:F3}. Aborting shot for old episode {episodeId}.");
                delayedShotCoroutine = null;
                yield break;
            }

            // Aktuelle Z-Position des Balls holen (ggf. verzögert)
            Vector3 currentBallPos = Ball.transform.position; // Oder GetDelayedBallData().Position;
            //  Normalisiere die Z-Position
            // Wichtig: Der zweite Parameter bei NormalizePosition ist 'isXAxis'. Für Z muss er 'false' sein!
            float ballNormalizedZ = NormalizePosition(currentBallPos.z, false); // false für Z-Achse!

            // Prüfen, ob der Ball im Toleranzbereich der ZIEL-Z-Position ist ***
            if (Mathf.Abs(ballNormalizedZ - episodeTriggerZ) < triggerTolerance)
            {
                //Debug.Log($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Ball reached target Z_norm ({ballNormalizedZ:F2} near {episodeTriggerZ:F2}). Triggering shot.");
                break; // Schleife verlassen und Schuss ausführen
            }

            timeElapsed += Time.deltaTime;
            yield return null; // Warten auf den nächsten Frame
        }

        // Timeout Check
        if (timeElapsed >= maxWaitTime)
        {
            Debug.LogWarning($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Timed out waiting for ball to reach Z_norm={episodeTriggerZ:F3}. Aborting shot.");
            delayedShotCoroutine = null;
            yield break;
        }

        // 4. Schuss ausführen
        if (Ball != null)
        {
            Rigidbody BallRB = Ball.GetComponent<Rigidbody>();
            if (BallRB != null && PlayArea != null && targetArea != null) // Zusätzliche Null-Checks
            {
                // --- Variante 1: Einfach zufällig in targetArea schießen ---
                /*
                float targetX = UnityEngine.Random.Range(targetArea.localPosition.x - targetArea.localScale.x / 2f, targetArea.localPosition.x + targetArea.localScale.x / 2f);
                float targetZ = UnityEngine.Random.Range(targetArea.localPosition.z - targetArea.localScale.z / 2f, targetArea.localPosition.z + targetArea.localScale.z / 2f);
                */

                // --- Variante 2: Nuttzt z_scale_factor_max für Streuung des finalen Schusses ---
                float targetX = UnityEngine.Random.Range(targetArea.localPosition.x - targetArea.localScale.x / 2f, targetArea.localPosition.x + targetArea.localScale.x / 2f);
                float centerZ = targetArea.localPosition.z;
                float worldHalfWidthZ = PlayArea.transform.localScale.z / 2f;
                // Nutzt den ANDEREN Curriculum-Parameter für die Streuung des finalen Schusses!
                float maxDeviationWorld = currentZScaleFactorMax * worldHalfWidthZ; // z_scale_factor_max hier!
                maxDeviationWorld = Mathf.Min(maxDeviationWorld, targetArea.localScale.z / 2f);
                float targetZ = UnityEngine.Random.Range(centerZ - maxDeviationWorld, centerZ + maxDeviationWorld);
                // --- Ende Variante 2 ---
                UnityEngine.Vector3 currentPos = Ball.transform.localPosition;
                UnityEngine.Vector3 direction = new UnityEngine.Vector3(targetX - currentPos.x, 0, targetZ - currentPos.z).normalized;

                float shotSpeed = UnityEngine.Random.Range(minBallSpeed, maxBallSpeed);
                BallRB.velocity = direction * shotSpeed;

                //Debug.Log($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Shot fired towards Z={targetZ:F2} (using final shot spread: {currentZScaleFactorMax:F2})");

            }
            else { Debug.LogError($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Ball RB, PlayArea or targetArea missing!"); }
        }
        else { Debug.LogError($"[{Time.frameCount}] DelayedShot Ep{episodeId}: Ball GameObject missing!"); }

        // 5. Coroutine-Referenz zurücksetzen
        delayedShotCoroutine = null;
    }

    protected override void OnDisable()
    {
        base.OnDisable(); // Wichtig: Basisklassen-Methode aufrufen
        // aufräumen
        if (delayedShotCoroutine != null)
        {
            StopCoroutine(delayedShotCoroutine);
            delayedShotCoroutine = null;
        }
    }

    /// <summary>
    /// Setzt die Ausgangsposition der Stangen zufällig
    /// </summary>
    private void RandomisePosition()
    {
        // Implementierung bei Bedarf hinzufügen
    }

    /// <summary>
    /// Berechnet eine Belohnung basierend auf einer modifizierten Sigmoid-Funktion
    /// </summary>
    public static float CalculateReward(float x, float a, float b, float c, float d)
    {
        return (a / (1 + Mathf.Exp(-b * (x - c)))) + d;
    }

    /// <summary>
    /// Verarbeitet Ereignisse im Zusammenhang mit dem Ball
    /// </summary>
    public void handleBallEvents(string status)
    {
        switch (status)
        {
            case "ET": // Eigentor des Torwarts
                AddReward(-0.8f);
                EndEpisodeWithReward();
                break;

            case "GT": // Gegentor
                AddReward(-1f);
                EndEpisodeWithReward();
                break;

            case "P": // Parade
                // Aktuelle Ballgeschwindigkeit und Richtung
                UnityEngine.Vector3 ballVelocityNormalized = ball_rb.velocity.normalized;
                UnityEngine.Vector3 ballVelocity = ball_rb.velocity;
                float speed = ballVelocity.magnitude;

                // Belohnungen für Geschwindigkeit und Winkel berechnen
                float speedReward = CalculateReward(speed, 1.5f, 0.6f, 2f, -0.5f);
                //Debug.Log("Speed: " + speed);
                //Debug.Log("Reward: " + speedReward);
                AddReward(speedReward);

                EndEpisodeWithReward();
                break;

            case "BO": // Bandenabpraller
                //AddReward(-0.2f);
                EndEpisodeWithReward();
                break;

            case "I": // Idle (Timeout)
                AddReward(-1f);
                EndEpisodeWithReward();
                break;
        }
    }

    // Methode zum Abrufen verzögerter Balldaten mit Jitter
    private (Vector3, Vector3) GetDelayedBallData()
    {
        if (!simulateLatency || ballPositionBuffer.Count == 0)
        {
            // Keine Latenz oder Puffer leer: aktuelle Daten zurückgeben
            return (Ball.transform.position, ball_rb.velocity);
        }

        // Berechne die zufällige Ziel-Verzögerungszeit innerhalb des Jitter-Bereichs
        float targetDelay = Random.Range(minSimulatedLatencySeconds, maxSimulatedLatencySeconds);
        float targetTimestamp = Time.time - targetDelay;

        // Finde den Puffer-Eintrag, der dem Ziel-Zeitstempel am nächsten kommt
        // (oder den ältesten, wenn alle neuer sind)
        BallDataFrame bestFrame = ballPositionBuffer.Peek(); // Starte mit dem ältesten Frame
        foreach (BallDataFrame frame in ballPositionBuffer)
        {
            // Wenn dieser Frame näher am Ziel-Zeitstempel ist als der bisher beste
            if (Mathf.Abs(frame.Timestamp - targetTimestamp) < Mathf.Abs(bestFrame.Timestamp - targetTimestamp))
            {
                bestFrame = frame;
            }
            // Da der Puffer nach Zeit sortiert ist (älteste zuerst),
            // können wir aufhören zu suchen, wenn die Frames zu neu werden (optional, für Effizienz)
            if (frame.Timestamp > targetTimestamp && bestFrame.Timestamp <= targetTimestamp)
            {
                // Wir haben den Punkt überschritten, der beste gefundene Frame ist gut genug
                break;
            }
        }

        // Gebe die Daten des ausgewählten Frames zurück
        return (bestFrame.Position, bestFrame.Velocity);
    }

    void FixedUpdate() // Physik-Updates hier
    {

        // Berechne die Vorhersage in jedem Physik-Schritt
        predictedCrossingZ = CalculatePredictedCrossingZ();

        // Optional: Debug-Ausgabe des vorhergesagten Z-Wertes
        // if (!float.IsNaN(predictedCrossingZ)) {
        //     Debug.Log($"Predicted Crossing Z: {predictedCrossingZ:F3}");
        // } else {
        //     Debug.Log("Prediction invalid.");
        // }
    }

    void Update()
    {
        // Manuelle Steuerung zum Testen
        if (Input.GetKeyDown(KeyCode.R))
        {
            SpawnBall();
        }
        // Puffer-Update für Latenz-Simulation
        if (simulateLatency)
        {
            // Sicherstellen, dass der Buffer nicht zu groß wird
            while (ballPositionBuffer.Count > maxBufferSize)
            {
                ballPositionBuffer.Dequeue(); // Entferne ältesten Eintrag, wenn Puffer voll ist
            }

            bufferUpdateTimer += Time.deltaTime;
            if (bufferUpdateTimer >= bufferUpdateInterval)
            {
                bufferUpdateTimer = 0f;

                // Aktuelle Ballposition und -geschwindigkeit in den Puffer legen
                BallDataFrame currentFrame = new BallDataFrame(
                    Ball.transform.position,
                    ball_rb.velocity,
                    Time.time
                );
                ballPositionBuffer.Enqueue(currentFrame);

                // Alte Einträge entfernen:
                // Verwende maxSimulatedLatencySeconds, um sicherzustellen,
                // dass wir Daten für den maximal möglichen Jitter behalten.
                while (ballPositionBuffer.Count > 0 &&
                       Time.time - ballPositionBuffer.Peek().Timestamp > maxSimulatedLatencySeconds)
                {
                    ballPositionBuffer.Dequeue(); // Entferne Frames, die älter als die maximale Latenz sind
                }
            }
        }

        // Debug-Navigation aktualisieren
        UpdateDebugNavigation();
    }
}