using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Simpler Algorithmisch gelöster Controller. Aktuell noch zu zittrig
public class PredictiveBallFollowController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MoveRodTorwart moveRod;
    [SerializeField] private GameObject ball;
    [SerializeField] private Collider shotZoneCollider;

    [Header("Movement Settings")]
    [SerializeField] private float rodTravelLimit = 0.23f;
    [SerializeField] private float movementSpeed = 10f;
    [SerializeField] private float shotCooldown = 1.0f;

    [Header("Prediction Settings")]
    [SerializeField] private float predictionFactor = 0.25f;  // Stärke der Vorhersage
    [SerializeField] private float maxPredictionOffset = 0.15f;  // Maximaler Offset in Metern
    [SerializeField] private float velocityThreshold = 0.1f;  // Min. Geschwindigkeit für Vorhersage
    [SerializeField] private bool usePrediction = true;  // Vorhersage ein-/ausschalten
    [SerializeField] private bool debug = false;

    // Cache für Berechnungen
    private float halfRodTravelLimit;
    private float targetPosNormalized;
    private float lastShotTime = -10f;

    private void Start()
    {
        // MoveRodTorwart Komponente finden, falls nicht zugewiesen
        if (moveRod == null)
        {
            moveRod = GetComponent<MoveRodTorwart>();
            if (moveRod == null)
            {
                Debug.LogError("MoveRodTorwart component not found!");
                enabled = false;
                return;
            }
        }

        // Ball finden, falls nicht zugewiesen
        if (ball == null)
        {
            ball = GameObject.FindGameObjectWithTag("Ball");
            if (ball == null)
            {
                Debug.LogError("Ball not found! Make sure it has a 'Ball' tag or assign it directly.");
                enabled = false;
                return;
            }
        }

        // Überprüfe, ob die Schusszone zugewiesen wurde
        if (shotZoneCollider == null)
        {
            Debug.LogWarning("Shot zone collider not assigned. Automatic shooting will be disabled.");
        }
        else
        {
            // Sicherstellen, dass der Collider als Trigger markiert ist
            if (!shotZoneCollider.isTrigger)
            {
                Debug.LogWarning("Shot zone collider should be marked as a trigger! Please check the collider settings.");
            }
        }

        halfRodTravelLimit = rodTravelLimit / 2f;

        if (debug)
        {
            Debug.Log("PredictiveBallFollowController initialized successfully.");
        }
    }

    private void Update()
    {
        // Check ball position directly in update loop
        CheckBallInShotZone();
    }

    private void FixedUpdate()
    {
        if (ball != null && moveRod != null)
        {
            // Ball-Position auf Z-Achse ermitteln
            float ballPosZ = ball.transform.position.z;

            // Prädiktive Position berechnen, wenn aktiviert
            if (usePrediction && BallPositionReceiver.IsBallVisible)
            {
                // Hole die Z-Geschwindigkeit des Balls aus dem BallPositionReceiver
                float velocityZ = BallPositionReceiver.RawVelocityZ;

                // Nur vorhersagen, wenn Geschwindigkeit über Schwellwert
                if (Mathf.Abs(velocityZ) > velocityThreshold)
                {
                    // Berechne den Offset basierend auf der Geschwindigkeit
                    float predictionOffset = velocityZ * predictionFactor;

                    // Begrenze den Offset
                    predictionOffset = Mathf.Clamp(predictionOffset, -maxPredictionOffset, maxPredictionOffset);

                    // Wende den Offset auf die aktuelle Position an
                    ballPosZ += predictionOffset;

                    if (debug)
                    {
                        Debug.Log($"Ball velocity: {velocityZ:F3} m/s, Prediction offset: {predictionOffset:F3}m");
                    }
                }
            }

            // Für MoveRodTorwart begrenzen wir die Ball-Position auf den Bewegungsbereich des Torwarts
            float clampedBallPosZ = Mathf.Clamp(ballPosZ, -halfRodTravelLimit, halfRodTravelLimit);

            // Begrenzte Position auf den Bereich -1 bis 1 normalisieren für MoveRodTorwart
            targetPosNormalized = clampedBallPosZ / halfRodTravelLimit;

            // Position und Rotationsgeschwindigkeit an MoveRod übergeben
            moveRod.SetTargetsAndSpeeds(movementSpeed, targetPosNormalized, 0f, 0f, 1f);

            if (debug && Time.frameCount % 60 == 0)
            {
                var (currentPos, currentRot) = moveRod.GetPosAndRot();
                Debug.Log($"Ball Position: {ballPosZ:F3}m, Clamped: {clampedBallPosZ:F3}m, " +
                          $"Normalized: {targetPosNormalized:F3}, Current Torwart Position: {currentPos:F3}");
            }
        }
    }

    // Direkte Prüfung, ob der Ball in der Zone ist
    private void CheckBallInShotZone()
    {
        if (ball == null || shotZoneCollider == null) return;

        // Verwende Physics.OverlapBox statt Trigger-Events
        bool ballInZone = shotZoneCollider.bounds.Contains(ball.transform.position);

        if (ballInZone && CanShoot())
        {
            if (debug)
            {
                Debug.Log($"Ball is in shot zone! Triggering shot at position {ball.transform.position}");
            }

            // Schuss direkt auslösen statt über Signal
            TriggerShot();
        }
    }

    // Überprüft, ob genug Zeit seit dem letzten Schuss vergangen ist
    private bool CanShoot()
    {
        // Prüft, ob der Cooldown abgelaufen ist
        bool cooldownExpired = (Time.time - lastShotTime) >= shotCooldown;

        // Prüft, ob der Torwart aktuell nicht bereits schießt
        bool notCurrentlyShooting = moveRod.GetShotState() == 0;

        return cooldownExpired && notCurrentlyShooting;
    }

    // Methode zum Auslösen eines Schusses
    public void TriggerShot()
    {
        if (moveRod != null && CanShoot())
        {
            moveRod.StartShot();
            lastShotTime = Time.time;

            if (debug)
            {
                Debug.Log("Shot triggered!");
            }
        }
    }
}