using UnityEngine;

public class BallBehaviourTorwart : MonoBehaviour
{
    [SerializeField] AgentTorwart AgentScript;
    [SerializeField] Material BallMaterial;
    [SerializeField] Material TeamMaterial;
    private Renderer Renderer;
    private Rigidbody RB;
    private char hitPlayer = 'N'; //N = None, A = Abwehr, T = Torwart
    private float idleTime = 0f;  // Timer für die Ruhezeit
    private bool isBallIdle = false;  // Überwacht, ob der Ball stillsteht

    // Variablen für die Zonenpräsenz (1 = in Zone, 0 = nicht in Zone)
    private int ballInShotzone = 0;
    private int ballInBehindGoalkeeper = 0;
    private int ballInImpactArea = 0;
    private int currentFrontGoalZoneID = 0; // 0 = keine, 1 = Zone1, 2 = Zone2, 3 = Zone3

    void Start()
    {
        Renderer = GetComponent<Renderer>();
        RB = GetComponent<Rigidbody>();
        RB.maxAngularVelocity = 100f;
        RB.maxLinearVelocity = 100f;
    }

    private void OnTriggerEnter(Collider other)
    {
        //Falls Tor faellt
        if (other.gameObject.name == "TorAI")
        {
            if (hitPlayer == 'T')
            {
                AgentScript.handleBallEvents("ET"); //Eigentor bzw. abgefaelscht durch Torwart
            }
            else
            {
                AgentScript.handleBallEvents("GT"); //Tor ohne Eigentkontakt
            }
        }
        else if (other.gameObject.name == "EndArea")
        {
            AgentScript.trackingBall = false;
        }

        // Zonenerkennung
        if (other.gameObject.name == "Shotzone")
        {
            ballInShotzone = 1;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "BehindGoalkeeper")
        {
            ballInBehindGoalkeeper = 1;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "ImpactArea")
        {
            AgentScript.busyRespawning = false; //Ab hier ist der Respawn definitiv abgeschlossen. Wenn man die Variable bereits nach dem Funktionsaufruf (SpawnBall) setzt, funktioniert dies nicht...
            ballInImpactArea = 1;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal1")
        {
            currentFrontGoalZoneID = 1;
            UpdateZoneInfo(); // Agenten informieren
        }
        else if (other.gameObject.name == "inFrontOfGoal2")
        {
            currentFrontGoalZoneID = 2;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal3")
        {
            currentFrontGoalZoneID = 3;
            UpdateZoneInfo();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.name == "ImpactArea" && (AgentScript.busyRespawning == false || AgentScript.useRealBallData == true)) //Wenn der echte Ball genutzt wird kann dieser ja nicht "respawnen". Daher das OR
        {
            ballInImpactArea = 0;
            UpdateZoneInfo();

            if (hitPlayer == 'T')
            {
                AgentScript.handleBallEvents("P"); //Parade bzw. erfolgreich geblockt
            }
            else
            {
                AgentScript.handleBallEvents("BO"); //Einfach von der Bande abgeprallt und zurueckgerollt
            }
        }

        // Zonen-Exit-Erkennung
        if (other.gameObject.name == "Shotzone")
        {
            ballInShotzone = 0;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "BehindGoalkeeper")
        {
            ballInBehindGoalkeeper = 0;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal1")
        {
            if (currentFrontGoalZoneID == 1) currentFrontGoalZoneID = 0; // Nur zurücksetzen, wenn es diese Zone war
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal2")
        {
            if (currentFrontGoalZoneID == 2) currentFrontGoalZoneID = 0;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal3")
        {
            if (currentFrontGoalZoneID == 3) currentFrontGoalZoneID = 0;
            UpdateZoneInfo();
        }
    }

    // Erweitere UpdateZoneInfo
    private void UpdateZoneInfo()
    {
        // Übergib die neue Zonen-ID an den Agenten
        AgentScript.UpdateZoneStatus(ballInShotzone, ballInBehindGoalkeeper, ballInImpactArea, currentFrontGoalZoneID);
    }

    private void OnCollisionEnter(Collision other)
    {
        //Falls Spieler von Team rot getroffen: Abspeichern und Farbe wechseln
        if (other.gameObject.CompareTag("Torwart_Rot"))
        {
            hitPlayer = 'T';
            Renderer.material = TeamMaterial;
        }
    }

    public void resetBall()
    {
        hitPlayer = 'N';
        //Farbe auf Ausgangsfarbe setzen
        Renderer.material = BallMaterial;

        // Zurücksetzen der Zonenvariablen
        ballInShotzone = 0;
        ballInBehindGoalkeeper = 0;
        ballInImpactArea = 0;
        UpdateZoneInfo();
    }

    void Update()
    {
        if (Mathf.Abs(RB.velocity.x) < 0.05f & Mathf.Abs(RB.velocity.z) < 0.05f) // Wenn die Geschwindigkeit des Balls sehr gering ist
        {
            // Wenn der Ball länger stillsteht, erhöhe den Timer
            if (!isBallIdle)
            {
                idleTime += Time.deltaTime;  // Zeit hinzufügen, die seit dem Stillstand vergangen ist
                isBallIdle = true;
            }
            else
            {
                idleTime += Time.deltaTime;
            }
            // Aktion ausführen, wenn der Ball länger als 1 Sekunde stillsteht
            if (idleTime >= 2f)
            {
                idleTime = 0f;  // Timer zurücksetzen, wenn die Aktion ausgeführt wurde
                isBallIdle = false;  // Ball wird wieder als nicht idle betrachtet
                AgentScript.handleBallEvents("I");
            }
        }
        else
        {
            // Wenn der Ball sich wieder bewegt, den Timer zurücksetzen
            idleTime = 0f;
            isBallIdle = false;
        }
    }
}