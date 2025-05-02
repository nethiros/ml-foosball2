using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Numerics;
using TMPro; // Für TextMeshPro

public class AgentAbwehr : Agent
{

    [SerializeField] private GameObject SpawnArea1;
    [SerializeField] private GameObject SpawnArea2;
    [SerializeField] private GameObject[] TargetAreas;
    private GameObject TargetArea;
    private int areaCount = 0;
    [SerializeField] private GameObject Ball;
    //[SerializeField] private GameObject Torwart;
    [SerializeField] private GameObject Abwehr;
    [SerializeField] private TargetSpawner targetSpawner;
    [SerializeField] private float minBallSpeed = 5f;
    [SerializeField] private float maxBallSpeed = 30f;
    [SerializeField] private float[] maxRodSpeeds = new float[]{8f, 8f}; //Fuer jede Stange abwechelnds linear und rot speed


    [Header("Ball Position Tracking")]
    [SerializeField] private bool useRealBallData = false; // Checkbox für Datenquelle

    [Header("Ball Information Displays")]
    [SerializeField] private TextMeshProUGUI posXText;
    [SerializeField] private TextMeshProUGUI posZText;
    [SerializeField] private TextMeshProUGUI velocityXText;
    [SerializeField] private TextMeshProUGUI velocityZText;

    public float mainSpawnProbability = 1f; // 80 % Wahrscheinlichkeit für Haupt-Spawn
    private UnityEngine.Vector3 lastTargetPosition;
    public bool spawnedAt;
    public bool trackingBall;
    public float cumulative = 0f;
    private MoveRodAbwehr moveRodAbwehr;
    //[SerializeField] private bool rewardForSpeed = true;
    private Rigidbody ball_rb;

    void Start(){
        moveRodAbwehr = Abwehr.GetComponent<MoveRodAbwehr>();
        areaCount = TargetAreas.Length;
        ball_rb = Ball.GetComponent<Rigidbody>();
    }
    public override void OnEpisodeBegin()
    {   
        
        cumulative = 0f;
        int randAreaIndex = UnityEngine.Random.Range(0,areaCount);
        TargetArea = TargetAreas[randAreaIndex];  //Zufaelligen Zielbereich festlegen
        lastTargetPosition = targetSpawner.AddTarget();
        SpawnBall();
        RandomisePosition();
        //Debug.Log($"Neue Episode | Time since previous Epsiode: {Time.time - lastTime}");
        //lastTime = Time.time;
        
    }

    private float randomizeNumber(float input){
        float output = Mathf.Clamp(input + Random.Range(-0.01f, 0.01f), -1 , 1);
        return output;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float ballPosX, ballPosZ, ballVelX, ballVelZ;

        if (useRealBallData)
        {
            // Normierte Daten vom BallPositionReceiver
            ballPosX = BallPositionReceiver.NormalizedX;
            ballPosZ = BallPositionReceiver.NormalizedZ;
            ballVelX = BallPositionReceiver.ApproximatedVelocityX;
            ballVelZ = BallPositionReceiver.ApproximatedVelocityZ;
        }
        else
        {
            // Simulationsdaten
            ballPosX = randomizeNumber(Ball.transform.localPosition.x);
            ballPosZ = randomizeNumber(Ball.transform.localPosition.z);
            ballVelX = randomizeNumber(ball_rb.velocity.x);
            ballVelZ = randomizeNumber(ball_rb.velocity.z);
        }

        // Text-Displays aktualisieren
        UpdateBallInfoTexts(ballPosX, ballPosZ, ballVelX, ballVelZ);

        sensor.AddObservation(lastTargetPosition[0]);
        sensor.AddObservation(lastTargetPosition[2]);
        sensor.AddObservation(ballPosX);
        sensor.AddObservation(ballPosZ);
        sensor.AddObservation(ballVelX);
        sensor.AddObservation(ballVelZ);

        float abwehrPos;
        float abwehrRot;
        (abwehrPos, abwehrRot) = moveRodAbwehr.GetPosAndRot();
        sensor.AddObservation(randomizeNumber(abwehrPos));
        sensor.AddObservation(randomizeNumber(abwehrRot));
    }

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
        AddReward(-0.001f);
        float moveInputAbwehr = actions.ContinuousActions[0];
        float rotateInputAbwehr = -actions.ContinuousActions[1];
        
        //Debug.Log(actions.DiscreteActions[0]);
        //Debug.Log(actions.ContinuousActions[2]);
        //string debugLog = actions.ContinuousActions[0].ToString() + " | " + actions.ContinuousActions[1].ToString() + actions.ContinuousActions[2].ToString() + " | " + actions.ContinuousActions[3].ToString();
        //Debug.Log(debugLog);

        //Uebersetzen der Speeds von diskreten Werten 0, 1, 2, ... in reale Werte
        float[] speeds = new float[4];
        int i = 0;
        foreach(int action in actions.DiscreteActions){
            switch(action){
                case 0: //Geschwindigkeitsstufe 0 - Langsam
                    speeds[i] = 0.1f*maxRodSpeeds[i];
                    break;
                case 1:
                    speeds[i] = 0.5f*maxRodSpeeds[i]; //Geschwindigkeitsstufe 1 - Mittelschnell
                    break;
                case 2:
                    speeds[i] = 1f*maxRodSpeeds[i]; //Geschwindigkeitsstufe 2 - Schnell
                    break;
            }
            i++;
        }

        //moveRodTorwart.SetTargetsAndSpeeds(speeds[0], moveInputTorwart, speeds[1], rotateInputTorwart);
        moveRodAbwehr.SetTargetsAndSpeeds(speeds[0], moveInputAbwehr, speeds[1], rotateInputAbwehr);
        /*if (startRewards == true) {
            AddReward(-ball_rb.velocity.x/200);
        }*/
    }


    private void RespawnTarget()
    {
        targetSpawner.AddTarget();
    }
    
    public override void Heuristic(in ActionBuffers actionsOut){
        ActionSegment<float> continousActions = actionsOut.ContinuousActions;
        continousActions[0] = Input.GetAxisRaw("Horizontal");
        continousActions[1] = Input.GetAxisRaw("Vertical");
        //continousActions[2] = Input.GetAxisRaw("Horizontal");
        //continousActions[3] = Input.GetAxisRaw("Vertical");
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 2;
        discreteActions[1] = 2;
        //discreteActions[2] = 2;
        //discreteActions[3] = 2;
    }

private void SpawnBall()
{
    bool useMainSpawn = UnityEngine.Random.value < 0.7f; // 70% für SpawnArea 1, 30% für SpawnArea 2
    spawnedAt = !useMainSpawn; // false für SpawnArea 1, true für SpawnArea 2

    // Wähle das passende Spawn- und Zielgebiet
    Transform selectedSpawnArea = useMainSpawn ? SpawnArea1.transform : SpawnArea2.transform;
    Transform selectedTargetArea = useMainSpawn ? TargetAreas[0].transform : TargetAreas[1].transform;

    // Zufällige Position im Spawnbereich bestimmen
    float spawnX = UnityEngine.Random.Range(selectedSpawnArea.localPosition.x - selectedSpawnArea.localScale.x / 2,
                                            selectedSpawnArea.localPosition.x + selectedSpawnArea.localScale.x / 2);
    float spawnZ = UnityEngine.Random.Range(selectedSpawnArea.localPosition.z - selectedSpawnArea.localScale.z / 2,
                                            selectedSpawnArea.localPosition.z + selectedSpawnArea.localScale.z / 2);
    float spawnY = selectedSpawnArea.localPosition.y;

    UnityEngine.Vector3 spawnPosition = new UnityEngine.Vector3(spawnX, spawnY, spawnZ);
    Ball.transform.localPosition = spawnPosition;

    Rigidbody BallRB = Ball.GetComponent<Rigidbody>();

    // Zufällige Zielposition bestimmen
    float targetX = UnityEngine.Random.Range(selectedTargetArea.localPosition.x - selectedTargetArea.localScale.x / 2,
                                             selectedTargetArea.localPosition.x + selectedTargetArea.localScale.x / 2);
    float targetZ = UnityEngine.Random.Range(selectedTargetArea.localPosition.z - selectedTargetArea.localScale.z / 2,
                                             selectedTargetArea.localPosition.z + selectedTargetArea.localScale.z / 2);
    float targetY = selectedTargetArea.localPosition.y;

    // Geschwindigkeit berechnen
    float velX = targetX - spawnX;
    float velZ = targetZ - spawnZ;
    BallRB.velocity = new UnityEngine.Vector3(velX, 0, velZ).normalized * UnityEngine.Random.Range(minBallSpeed, maxBallSpeed);

    // Ball zurücksetzen
    Ball.GetComponent<BallBehaviourAbwehr>().resetBall();
}


    private void RandomisePosition(){
        //Ausgangsposition der Stangen randomisieren
        moveRodAbwehr.SetPosAndRot(UnityEngine.Random.value, UnityEngine.Random.value);
    }


    public static float CalculateReward(float x, float a, float b, float c, float d)
    {
            return (a / (1 + Mathf.Exp(-b * (x - c)))) + d;
    }
    


    public void handleBallEvents(string status){
        switch(status){
            case "EA": //Eigentor des Torwarts
                AddReward(-0.7f);
                EndEpisode();
                break;

            case "ET": //Eigentor der Abwehr
                AddReward(-0.7f);
                EndEpisode();
                break;

            case "GT": //Gegentor
                AddReward(-1f);
                EndEpisode();
                break;
            
            case "P": // Parade
                // Aktuelle Ballgeschwindigkeit und Richtung
                UnityEngine.Vector3 ballVelocityNormalized = ball_rb.velocity.normalized;
                UnityEngine.Vector3 ballVelocity = ball_rb.velocity;  // Geschwindigkeit des Balls
                float speed = ballVelocity.magnitude;     // Geschwindigkeit als Skalar (Betrag des Vektors)
                
                // Richtung vom Ball zur Target-Position berechnen
                UnityEngine.Vector3 targetDirection = (lastTargetPosition - Ball.transform.position).normalized;

                // Winkel zwischen beiden Vektoren berechnen
                float angleDifference = Mathf.Abs(UnityEngine.Vector3.Angle(ballVelocityNormalized, targetDirection));

                float speedReward = CalculateReward(speed, 1.5f, 0.6f, 4f, -0.5f);
                float angleReward = CalculateReward(angleDifference, 1.5f, -0.1f, 20f, -0.5f);
                AddReward(speedReward);
                AddReward(0.2f*angleReward);
                // Ausgabe des Winkels
                //Debug.Log("Winkel-Differenz: " + angleDifference + "° Reward: " + angleReward);
                //Debug.Log($"Geschwindigkeit: {speed} Reward: {speedReward}");
                // Warte eine Sekunde vor dem Episodenende
                //StartCoroutine(TrackBallDistance());
                EndEpisode();
                break;


            case "BO": //Bandenabpraller
                AddReward(-0.6f);
                EndEpisode();
                break;
            
            case "BB": //Hintere Bande getroffen
                AddReward(-0.0f);
                break;

            case "I": //Idle
                AddReward(-1f);
                EndEpisode();
                break;
        }
    }

    private IEnumerator TrackBallDistance()
    {
        trackingBall = true;
        //float closestDistance = float.MaxValue;
        float elapsedTime = 0f;
        //float velocity = 0f;
        //float reward = 0;
        //UnityEngine.Vector3 ballPosition;
        
        while (elapsedTime < 1f && trackingBall)
        {
            /*ballPosition = Ball.transform.position;
            float distance = UnityEngine.Vector3.Distance(ballPosition, lastTargetPosition);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                float velX = -ball_rb.velocity.x;
                    //Geschwindigkeit mittels modifizierter Sigmoidfunktion in reward umrechnen
                    float a = 1f; //Max Reward
                    float b = 1f;
                    float c = 0.4f; //Stauchungsfakor
                    float d = 5f; //Geschwindigkeit fuer mittleren Reward
                    reward = a / (b + Mathf.Exp(-c * (velX - d)));
            }*/
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        //Skaliere die Belohnung zwischen 0.5 und 5
        //float normalizedReward = Mathf.Clamp(5f - (closestDistance * 1.5f), 0.5f, 5f);
        //AddReward(normalizedReward);
        //AddReward(reward);
        
        EndEpisode();
    }

    void Update(){

        if (Input.GetKeyDown(KeyCode.T))
        {
            RespawnTarget();
        }
        
        if (Input.GetKeyDown(KeyCode.R)){
            SpawnBall();
        }
    }

}
