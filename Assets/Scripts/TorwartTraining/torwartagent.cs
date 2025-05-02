using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

//Outdated Torwartagent
public class torwartagent : Agent
{   
    [SerializeField] private GameObject ball_obj;

    private Rigidbody ball_rb;

    [SerializeField] private float maxLinearVelocity = 1.0f;
    [SerializeField] private float maxAngularVelocity = 30f;


    public override void OnEpisodeBegin()
    {

    }

    public override void CollectObservations(VectorSensor sensor)
    {
        
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float vel = 0f;
        switch(actions.DiscreteActions[0]){
            case 0:
                vel = 0.1f * maxLinearVelocity;
                break;
            case 1:
                vel = 0.5f * maxLinearVelocity;
                break;
            case 2:
                vel = 1.0f * maxLinearVelocity;
                break;
        }

        float angularVel = 0f;
        switch(actions.DiscreteActions[1]){
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
        switch(actions.DiscreteActions[2]){
            case 0:
                angularPos = 0f;
                break;
            case 1:
                angularPos = -0.3f;
                break;
            case 2:
                angularPos = 0.3f;
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

    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continousActions = actionsOut.ContinuousActions;
        continousActions[0] = Input.GetAxisRaw("Horizontal");
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 0;
        discreteActions[1] = 0;
        discreteActions[2] = 0;

        if(Input.GetKey(KeyCode.X)) discreteActions[0] = 0;
        else if(Input.GetKey(KeyCode.C)) discreteActions[0] = 1;
        else if(Input.GetKey(KeyCode.V)) discreteActions[0] = 2;

        if(Input.GetKey(KeyCode.G)) discreteActions[1] = 0;
        else if(Input.GetKey(KeyCode.H)) discreteActions[1] = 1;
        else if(Input.GetKey(KeyCode.J)) discreteActions[1] = 2;

        if(Input.GetKey(KeyCode.Keypad0)) discreteActions[2] = 0;
        else if(Input.GetKey(KeyCode.Keypad1)) discreteActions[2] = 1;
        else if(Input.GetKey(KeyCode.Keypad2)) discreteActions[2] = 2;
        else if(Input.GetKey(KeyCode.Keypad3)) discreteActions[2] = 3;
        else if(Input.GetKey(KeyCode.Keypad4)) discreteActions[2] = 4;
        else if(Input.GetKey(KeyCode.Keypad5)) discreteActions[2] = 5;
        else if(Input.GetKey(KeyCode.Keypad6)) discreteActions[2] = 6;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)){
            EndEpisode();
        }
    }


    private float ModifiedSigmoid(float vel, float a, float b, float c, float d){
        float res = a / (1f + Mathf.Exp(b * (vel + c))) + d;
        
        return res;
    }


}
