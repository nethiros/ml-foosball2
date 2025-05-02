using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Steuert die Bewegung und Rotation der Torwartstange in einem Tischkicker-Spiel.
/// Erm�glicht sowohl manuelle Steuerung als auch automatisierte Schussbewegungen.
/// </summary>
public class MoveRodTorwart : MonoBehaviour
{
    private Rigidbody rb;
    private ConfigurableJoint joint;

    [SerializeField] private MotorController motorController; // Controller zum Steuern der Schrittmotoren �ber den ESP
    [SerializeField] private float[] zeroPos = new float[2];  // Minimale und maximale Position [min, max]
    [SerializeField] private float[] zeroRot = new float[2];  // Minimale und maximale Rotation [min, max]

    // Positions-Variablen
    private float posNormalized;        // Aktuelle Position, normalisiert (-1 bis 1)
    private float posUnits;             // Aktuelle Position in Unity-Einheiten
    private float posTargetNormalized;  // Zielposition, normalisiert
    private float posSpeedUnits;        // Bewegungsgeschwindigkeit in Unity-Einheiten/Sekunde
    private float normToUnits;          // Umrechnungsfaktor von normalisierter Position zu Unity-Einheiten
    private float minStepUnits;         // Minimale Schrittweite f�r die Bewegung pro FixedUpdate

    // Rotations-Variablen
    private float rotNormalized;        // Aktuelle Rotation, normalisiert (-1 bis 1)
    private float rotDegree;            // Aktuelle Rotation in Grad
    private float rotTargetNormalized;  // Zielrotation, normalisiert
    private float rotSpeedRadian;       // Rotationsgeschwindigkeit in Radiant/Sekunde
    private float normToRadian;         // Umrechnungsfaktor von normalisierter Rotation zu Radiant
    private float minStepRadian;        // Minimale Schrittweite f�r die Rotation pro FixedUpdate

    private float scale;                // Skalierungsfaktor aus der �bergeordneten Transformation

    [SerializeField] private int teamMultiplier = 1;  // 1 oder -1, je nach Team - bestimmt Richtungskonvention
    [SerializeField] private bool debug = false;      // Debug-Modus aktivieren/deaktivieren

    // Schuss-Parameter
    [SerializeField] private float backSwingAngle = 40f;     // Winkel f�r den R�ckschwung (Ausholbewegung)
    [SerializeField] private float forwardSwingAngle = 90f;  // Winkel f�r den Vorschwung (Schussbewegung)
    [SerializeField] private float shotSpeedRad = 22f;       // Schussgeschwindigkeit in Radiant/s
    [SerializeField] private float extremePositionDelay = 0.5f; // Verz�gerung an Extrempositionen in Sekunden
    [SerializeField] private GameObject goalkeeperPosition;  // Visuelle Referenz f�r die Torwartposition

    // Zustandsmaschine f�r die Schussbewegung
    private enum ShotState
    {
        Idle,             // Ruhezustand
        BackSwing,        // Ausholbewegung 
        BackSwingHold,    // Kurze Pause nach dem Ausholen
        ForwardSwing,     // Vorw�rtsbewegung (eigentlicher Schuss)
        ForwardSwingHold, // Kurze Pause nach dem Schuss
        ReturnToHome      // R�ckkehr in die Ausgangsposition
    }
    private ShotState currentShotState = ShotState.Idle;
    private float targetRotation = 0f;  // Zielrotation f�r die aktuelle Schussphase in Grad
    private float homeRotation = 0f;    // Neutrale Rotationsposition (Ausgangslage)
    private float stateStartTime = 0f;  // Zeit des Zustandsbeginns f�r Timeout-Berechnung
    private float stateTimeout = 0.3f;  // Maximale Zeit pro Zustand (Sekunden)
    private float angleThreshold = 5f;  // Schwellenwert f�r Winkelgenauigkeit (gr��er f�r mehr Toleranz)

    /// <summary>
    /// Initialisierung der Komponente
    /// </summary>
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        scale = transform.parent.localScale.z;  // Skalierungsfaktor aus dem �bergeordneten Objekt

        // Umrechnungsfaktoren berechnen
        normToUnits = Mathf.Abs(zeroPos[1] - zeroPos[0]) * scale / 2f;  // Umrechnung von normalisiert zu Einheiten
        normToRadian = Mathf.Abs(zeroRot[1] - zeroRot[0]) * Mathf.Deg2Rad / 2f;  // Umrechnung von normalisiert zu Radiant

        joint = GetComponent<ConfigurableJoint>();

        // Lineares Bewegungslimit f�r den Joint konfigurieren
        SoftJointLimit newLinearLimit = joint.linearLimit;
        newLinearLimit.limit = Mathf.Abs(zeroPos[1] - zeroPos[0]) * scale / 2f;
        joint.linearLimit = newLinearLimit;

        // Winkel-Limit f�r den Joint konfigurieren
        SoftJointLimit newAngularLimit = joint.angularZLimit;
        newAngularLimit.limit = Mathf.Abs(zeroRot[1] - zeroRot[0]) / 2f;
        joint.angularZLimit = newAngularLimit;

        // Initialisiere Position und Rotation
        CalcPosAndRot();
        homeRotation = rotDegree; // Startrotation als Ausgangsbasis speichern

        // Maximalgeschwindigkeit f�r schnelle Rotationen setzen
        rb.maxAngularVelocity = shotSpeedRad * 1.5f;

        // Initiale Position an den MotorController senden
        if (motorController != null)
        {
            motorController.SetTargetValues(posNormalized, -rotNormalized);
        }
    }

    /// <summary>
    /// Setzt Ziel-Positionen und -Geschwindigkeiten f�r Bewegung und Rotation
    /// </summary>
    /// <param name="posSpeed">Bewegungsgeschwindigkeit</param>
    /// <param name="posTarget">Zielposition (-1 bis 1)</param>
    /// <param name="rotSpeed">Rotationsgeschwindigkeit</param>
    /// <param name="rotTarget">Zielrotation (-1 bis 1)</param>
    /// <param name="shotSignal">Schusssignal (>0.5 l�st Schuss aus)</param>
    public void SetTargetsAndSpeeds(float posSpeed, float posTarget, float rotSpeed, float rotTarget, float shotSignal)
    {
        // Position aktualisieren
        posTargetNormalized = posTarget;
        posSpeedUnits = posSpeed;
        minStepUnits = posSpeedUnits * Time.fixedDeltaTime;  // Mindestschritt f�r einen Frame
        rb.maxLinearVelocity = posSpeedUnits + 0.5f;  // Maximale Geschwindigkeit leicht erh�hen

        // Rotation aktualisieren, wenn direkte Steuerung verwendet wird (rotSpeed > 0)
        if (rotSpeed > 0)
        {
            rotTargetNormalized = rotTarget;
            rotSpeedRadian = rotSpeed;
            minStepRadian = rotSpeedRadian * Time.fixedDeltaTime;  // Mindestschritt f�r einen Frame

            // Schusszustand zur�cksetzen bei direkter Rotationssteuerung
            currentShotState = ShotState.Idle;
        }

        // Position an den MotorController senden
        if (motorController != null)
        {
            motorController.SetTargetValues(posTargetNormalized, -rotNormalized);
        }

        // Schuss starten, wenn Signal empfangen und im Ruhezustand
        if (shotSignal > 0.5f && currentShotState == ShotState.Idle)
        {
            StartShot();
        }
    }

    /// <summary>
    /// Startet die Schussbewegung, wenn sich die Stange im Ruhezustand befindet
    /// </summary>
    public void StartShot()
    {
        if (currentShotState == ShotState.Idle)
        {
            // Aktuelle Position neu berechnen
            CalcPosAndRot();

            // Wechsel in den R�ckschwung-Zustand
            currentShotState = ShotState.BackSwing;
            stateStartTime = Time.time;

            // R�ckschwung-Ziel berechnen (Ausholbewegung)
            targetRotation = homeRotation + (backSwingAngle * teamMultiplier);
            if (debug) Debug.Log($"Shot started: BackSwing phase, Current: {rotDegree}, Target: {targetRotation}");
        }
    }

    /// <summary>
    /// Gibt aktuelle normalisierte Position und Rotation zur�ck
    /// </summary>
    public (float, float) GetPosAndRot()
    {
        return (posNormalized, rotNormalized);
    }

    /// <summary>
    /// Gibt den aktuellen Schusszustand zur�ck (0=Idle, 1=Schuss aktiv)
    /// </summary>
    public int GetShotState()
    {
        // 0 f�r Ruhezustand, 1 f�r aktiven Schuss
        return currentShotState == ShotState.Idle ? 0 : 1;
    }

    /// <summary>
    /// Berechnet die aktuelle Position und Rotation und normalisiert sie
    /// </summary>
    private void CalcPosAndRot()
    {
        // Aktuelle Position und Rotation in Unity-Einheiten/Grad
        posUnits = transform.localPosition.z;
        rotDegree = transform.localEulerAngles.z;

        // Korrektur f�r Winkel �ber 180� (Unity's Euler-Winkel sind 0-360�)
        if (rotDegree > 180f)
        {
            rotDegree -= 360f;
        }

        // Normalisierung auf den Bereich -1 bis 1
        posNormalized = Mathf.Clamp(teamMultiplier * (Mathf.InverseLerp(zeroPos[0], zeroPos[1], posUnits) * 2f - 1f), -1f, 1f);
        rotNormalized = Mathf.Clamp(-teamMultiplier * (Mathf.InverseLerp(zeroRot[0], zeroRot[1], rotDegree) * 2f - 1f), -1f, 1f);
    }

    /// <summary>
    /// Setzt zuf�llige Position und Rotation (f�r Tests)
    /// </summary>
    public void SetRandomPosAndRot()
    {
        transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y,
                                              Mathf.Lerp(zeroPos[0], zeroPos[1], UnityEngine.Random.Range(0f, 1f)));
        transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y,
                                                Mathf.Lerp(zeroRot[0], zeroRot[1], UnityEngine.Random.Range(0f, 1f)));
    }

    /// <summary>
    /// Wird in festen Zeitintervallen aufgerufen f�r Physik-basierte Berechnungen
    /// </summary>
    void FixedUpdate()
    {
        // Position und Rotation aktualisieren
        CalcPosAndRot();

        // Visuelle Position des Torwart-Indikators aktualisieren
        if (goalkeeperPosition != null)
        {
            Vector3 newPosition = goalkeeperPosition.transform.localPosition;
            newPosition.z = transform.localPosition.z + 0.53f;  // Offset f�r korrekte Positionierung
            goalkeeperPosition.transform.localPosition = newPosition;
        }

        // Bewegung der Stange (Translation) verarbeiten
        if (!Mathf.Approximately(posNormalized, posTargetNormalized))
        {
            if (Mathf.Abs(posTargetNormalized - posNormalized) * normToUnits < minStepUnits)
            {
                // Kleiner Schritt: direkt zum Ziel bewegen
                rb.velocity = new Vector3(0, 0, teamMultiplier * (posTargetNormalized - posNormalized) * normToUnits / Time.fixedDeltaTime);
            }
            else
            {
                // Gro�er Schritt: mit konstanter Geschwindigkeit bewegen
                rb.velocity = new Vector3(0, 0, teamMultiplier * Mathf.Sign(posTargetNormalized - posNormalized) * posSpeedUnits);
            }
        }
        else
        {
            // Ziel erreicht: Geschwindigkeit auf 0 setzen
            rb.velocity = new Vector3(0, 0, 0);
        }

        // Rotation der Stange verarbeiten - entweder direkte Steuerung oder Schuss-Zustandsmaschine
        if (!Mathf.Approximately(rotNormalized, rotTargetNormalized))
        {
            if (Mathf.Abs(rotTargetNormalized - rotNormalized) * normToRadian < minStepRadian)
            {
                // Kleiner Schritt: direkt zum Ziel drehen
                rb.angularVelocity = new Vector3(0, 0, -teamMultiplier * (rotTargetNormalized - rotNormalized) * normToRadian / Time.fixedDeltaTime);
            }
            else
            {
                // Gro�er Schritt: mit konstanter Geschwindigkeit drehen
                rb.angularVelocity = new Vector3(0, 0, -teamMultiplier * Mathf.Sign(rotTargetNormalized - rotNormalized) * rotSpeedRadian);
            }
        }
        else
        {
            // Ziel erreicht: Drehgeschwindigkeit auf 0 setzen
            rb.angularVelocity = new Vector3(0, 0, 0);
        }

        // Debug-Ausgabe
        if (debug)
        {
            //Debug.Log($"Direct Rotation: Target={targetRotDegree}, Current={rotDegree}, Diff={rotDiff}");
        }

        // Schuss-Zustandsmaschine verarbeiten, wenn aktiv
        if (currentShotState != ShotState.Idle)
        {
            ProcessShotState();
        }

        // Aktualisierte Werte an den MotorController senden
        if (motorController != null)
        {
            motorController.SetTargetValues(posNormalized, -rotNormalized);
        }
    }

    /// <summary>
    /// Verarbeitet die Schuss-Zustandsmaschine und steuert die Rotationsbewegung
    /// </summary>
    private void ProcessShotState()
    {
        // Zeit im aktuellen Zustand berechnen
        float timeInState = Time.time - stateStartTime;

        switch (currentShotState)
        {
            case ShotState.Idle:
                // Keine Rotation im Ruhezustand
                rb.angularVelocity = Vector3.zero;
                break;

            case ShotState.BackSwing:
                // R�ckschwung (Ausholbewegung)
                // Distanz zum Zielwinkel berechnen
                float backSwingDiff = GetShortestAngleDifference(rotDegree, targetRotation);

                // Ziel erreicht oder Timeout?
                if (Mathf.Abs(backSwingDiff) < angleThreshold || timeInState > stateTimeout)
                {
                    // Rotation stoppen und in Haltezustand wechseln
                    rb.angularVelocity = Vector3.zero;
                    currentShotState = ShotState.BackSwingHold;
                    stateStartTime = Time.time;

                    if (debug) Debug.Log($"Shot phase: BackSwingHold, position: {rotDegree}, waiting {extremePositionDelay}s");
                }
                else
                {
                    // R�ckschwungbewegung fortsetzen
                    ApplyRotation(targetRotation);
                }
                break;

            case ShotState.BackSwingHold:
                // Haltezeit an Extremposition (Ausholposition)
                rb.angularVelocity = Vector3.zero;  // Keine Bewegung w�hrend des Haltens

                // Lange genug gewartet?
                if (timeInState >= extremePositionDelay)
                {
                    // In Vorschwung-Phase wechseln (eigentliche Schussbewegung)
                    currentShotState = ShotState.ForwardSwing;
                    stateStartTime = Time.time;

                    // Zielwinkel f�r Vorschwung berechnen
                    targetRotation = rotDegree - (forwardSwingAngle * teamMultiplier);

                    if (debug) Debug.Log($"Shot phase: ForwardSwing, from: {rotDegree}, target: {targetRotation}");
                }
                break;

            case ShotState.ForwardSwing:
                // Vorschwung (Schussbewegung)
                // Distanz zum Zielwinkel berechnen
                float forwardSwingDiff = GetShortestAngleDifference(rotDegree, targetRotation);

                // Ziel erreicht oder Timeout?
                if (Mathf.Abs(forwardSwingDiff) < angleThreshold || timeInState > stateTimeout)
                {
                    // Rotation stoppen und in Haltezustand wechseln
                    rb.angularVelocity = Vector3.zero;
                    currentShotState = ShotState.ForwardSwingHold;
                    stateStartTime = Time.time;

                    if (debug) Debug.Log($"Shot phase: ForwardSwingHold, position: {rotDegree}, waiting {extremePositionDelay}s");
                }
                else
                {
                    // Vorschwungbewegung fortsetzen
                    ApplyRotation(targetRotation);
                }
                break;

            case ShotState.ForwardSwingHold:
                // Haltezeit an Extremposition (Schussposition)
                rb.angularVelocity = Vector3.zero;  // Keine Bewegung w�hrend des Haltens

                // Lange genug gewartet?
                if (timeInState >= extremePositionDelay)
                {
                    // In R�ckkehr-Phase wechseln
                    currentShotState = ShotState.ReturnToHome;
                    stateStartTime = Time.time;

                    // Zur�ck zur Ausgangsposition
                    targetRotation = homeRotation;

                    if (debug) Debug.Log($"Shot phase: ReturnToHome, from: {rotDegree}, target: {homeRotation}");
                }
                break;

            case ShotState.ReturnToHome:
                // R�ckkehr zur Ausgangsposition
                // Distanz zum Zielwinkel berechnen
                float homePosDiff = GetShortestAngleDifference(rotDegree, targetRotation);

                // Ziel erreicht oder Timeout?
                if (Mathf.Abs(homePosDiff) < angleThreshold || timeInState > stateTimeout * 1.5f) // L�ngeres Timeout f�r R�ckkehr
                {
                    // Schusssequenz abgeschlossen
                    currentShotState = ShotState.Idle;
                    rb.angularVelocity = Vector3.zero;

                    if (debug) Debug.Log($"Shot complete: Back to Idle, final pos: {rotDegree}");
                }
                else
                {
                    // Bewegung zur Ausgangsposition fortsetzen, etwas langsamer
                    ApplyRotation(targetRotation, 0.7f);
                }
                break;
        }
    }

    /// <summary>
    /// Berechnet den k�rzesten Winkelunterschied unter Ber�cksichtigung des 360� �bergangs
    /// </summary>
    private float GetShortestAngleDifference(float currentAngle, float targetAngle)
    {
        float diff = targetAngle - currentAngle;

        // Normalisieren auf -180� bis 180�
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;

        return diff;
    }

    /// <summary>
    /// Wendet eine Rotation in Richtung des Zielwinkels mit anpassbarer Geschwindigkeit an
    /// </summary>
    /// <param name="targetAngle">Zielwinkel in Grad</param>
    /// <param name="speedMultiplier">Geschwindigkeitsmultiplikator (1.0 = normale Geschwindigkeit)</param>
    private void ApplyRotation(float targetAngle, float speedMultiplier = 1.0f)
    {
        // K�rzesten Weg zum Ziel berechnen
        float angleDiff = GetShortestAngleDifference(rotDegree, targetAngle);
        float direction = Mathf.Sign(angleDiff);  // Richtung bestimmen (+1 oder -1)

        // Basis-Rotationsgeschwindigkeit berechnen
        float baseSpeed = shotSpeedRad * speedMultiplier;

        // PID-�hnliche Geschwindigkeitsanpassung: schneller starten, langsamer zum Ziel
        float absAngleDiff = Mathf.Abs(angleDiff);
        float speedFactor = 1.0f;

        if (absAngleDiff < 30f)
        {
            // Geschwindigkeit reduzieren, wenn nahe am Ziel (proportional zur Entfernung)
            speedFactor = Mathf.Max(0.2f, absAngleDiff / 30f);
        }

        // Endg�ltige Rotationsgeschwindigkeit berechnen
        float finalSpeed = direction * baseSpeed * speedFactor;

        // Rotationsgeschwindigkeit anwenden
        rb.angularVelocity = new Vector3(0, 0, finalSpeed);

        if (debug && Time.frameCount % 10 == 0) // Weniger h�ufige Debug-Ausgaben
        {
            Debug.Log($"Angle diff: {angleDiff}, Speed: {finalSpeed}, Factor: {speedFactor}");
        }
    }

    /// <summary>
    /// Update wird jeden Frame aufgerufen
    /// </summary>
    void Update()
    {
        if (debug && Time.frameCount % 20 == 0) // Reduzierte Debug-H�ufigkeit
        {
            //Debug.Log($"State: {currentShotState}, RotDeg: {rotDegree:F3}, Target: {targetRotation:F3}, Diff: {GetShortestAngleDifference(rotDegree, targetRotation):F3}");
            Debug.Log($"Normalized: Pos: {posNormalized} | Rot: {rotNormalized} | Target-Pos: {posTargetNormalized} | Target-Rot: {rotTargetNormalized}");
        }
    }
}