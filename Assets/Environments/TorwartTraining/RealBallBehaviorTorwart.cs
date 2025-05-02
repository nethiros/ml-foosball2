using UnityEngine;

public class RealBallBehaviorTorwart : MonoBehaviour
{
    [SerializeField] AgentTorwart AgentScript;
    [SerializeField] Material BallMaterial;
    [SerializeField] Material TeamMaterial;
    private Renderer Renderer;
    private Rigidbody RB;

    // Variablen f�r die Zonenpr�senz (1 = in Zone, 0 = nicht in Zone)
    private int ballInShotzone = 0;
    private int ballInBehindGoalkeeper = 0;
    private int ballInImpactArea = 0;
    private int ballInFrontOfGoal = 0;
    void Start()
    {
        Renderer = GetComponent<Renderer>();
        RB = GetComponent<Rigidbody>();
        RB.maxAngularVelocity = 100f;
        RB.maxLinearVelocity = 100f;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Zonenerkennung beim Betreten - ist besser als OnTriggerStay f�r initiale Erkennung
        UpdateZoneStatus(other, 1);
    }

    private void OnTriggerExit(Collider other)
    {
        // Zonenerkennung beim Verlassen - setzt Status zur�ck auf 0
        UpdateZoneStatus(other, 0);
    }

    // OnTriggerStay behalten wir f�r den Fall, dass ein Objekt erst nach dem Start im Trigger ist
    private void OnTriggerStay(Collider other)
    {
        // Zonenerkennung
        UpdateZoneStatus(other, 1);
    }

    // Hilfsmethode, um Zone zu identifizieren und Status zu setzen (1 oder 0)
    private void UpdateZoneStatus(Collider other, int status)
    {
        if (other.gameObject.name == "Shotzone")
        {
            ballInShotzone = status;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "BehindGoalkeeper")
        {
            ballInBehindGoalkeeper = status;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "ImpactArea")
        {
            ballInImpactArea = status;
            UpdateZoneInfo();
        }
        else if (other.gameObject.name == "inFrontOfGoal")
        {
            ballInFrontOfGoal = status;
            UpdateZoneInfo();
        }
    }

    // Erweiterte UpdateZoneInfo
    private void UpdateZoneInfo()
    {
        // Sende aktuelle Zonenstatus an Agent
        AgentScript.UpdateZoneStatus(ballInShotzone, ballInBehindGoalkeeper, ballInImpactArea, ballInFrontOfGoal);
    }

    // Optional: Methode zum Zur�cksetzen aller Zonen
    public void ResetZones()
    {
        ballInShotzone = 0;
        ballInBehindGoalkeeper = 0;
        ballInImpactArea = 0;
        ballInFrontOfGoal = 0;
        UpdateZoneInfo();
    }
}