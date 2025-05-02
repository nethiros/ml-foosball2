using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class MoveRodAbwehr : MonoBehaviour
{
    private Rigidbody Rigidbody;
    private float posSpeed = 4f;
    private float posTarget = 4f;
    private float rotSpeed = 4f;
    private float rotTarget = 4f;
    private int posDirection = 1;
    public float maxAngularVelocity = 12f;
    public float maxLinearVelocity = 8f;
    float factorLinear;
    float factorAngle;
    float stepLinear;
    float stepAngle;

    private float fixedUpdateStep;
    private float scale = 10f;

    private float pos = 0f;
    private float rot = 0f;

    [SerializeField] private float[] zeroPos = new float[2];
    [SerializeField] private float[] zeroRot = new float[2];

    void Start(){

        Rigidbody = GetComponent<Rigidbody>();
        Rigidbody.maxAngularVelocity = maxAngularVelocity;
        Rigidbody.maxLinearVelocity = maxLinearVelocity;
        fixedUpdateStep = Time.fixedDeltaTime;
        CalcPosAndRot();
    }

    public void SetTargetsAndSpeeds(float posSpeed, float posTarget, float rotSpeed, float rotTarget){
        CalcPosAndRot();
        //Uebergebene Geschwindigkeiten und Ziele abspeichern. 
        //Die Targets sind zwischen -1 und 1 normiert
        //Die Geschwindigkeiten diskret zwischen ... und ... in Standardgeschwindigkeitseinheiten fuer velocity und angularVelocity
        this.posSpeed = posSpeed;
        this.posTarget = posTarget;
        this.rotSpeed = rotSpeed;
        this.rotTarget = rotTarget;

        factorLinear = (zeroPos[1]-zeroPos[0])*scale/2f; //Umrechung von kompletter Bewegegung (-1,1) zu Units
        stepLinear = posSpeed*fixedUpdateStep; //Minimale Schrittweite in einem FixedUpdate Schritt in Units

        factorAngle = (zeroRot[1]-zeroRot[0])*Mathf.Deg2Rad/2f; //Umrechung von kompletter Bewegung (-1,1) zu Radian
        stepAngle = rotSpeed*fixedUpdateStep; //Minimale Schrittweite in einem FixedUpdate Schritt in Radians
        
        //Position
        if(posTarget > pos && Mathf.Abs(posTarget-pos)*factorLinear>stepLinear){
            Rigidbody.velocity = new Vector3(0, 0, this.posSpeed);
            posDirection = 1;
        }else if(posTarget < pos && Mathf.Abs(posTarget-pos)*factorLinear>stepLinear){
            Rigidbody.velocity = new Vector3(0, 0, -this.posSpeed);
            posDirection = -1;
        }else{
            Rigidbody.velocity = new Vector3(0, 0, 0);
        }
        
        //Rotation
        if(!Mathf.Approximately(rot, rotTarget)){
            if(Mathf.Abs(rotTarget - rot) * factorAngle  < stepAngle){
                Rigidbody.angularVelocity = new Vector3(0, 0, 1 * (rotTarget - rot) * factorAngle  / Time.fixedDeltaTime);
            }else{
                Rigidbody.angularVelocity = new Vector3(0, 0, 1 * Mathf.Sign(rotTarget-rot) * rotSpeed);
            }
        }else{
            Rigidbody.angularVelocity = new Vector3(0,0,0);
        }
    }

    public (float, float) GetPosAndRot(){
        //Aktuelle Position und Rotation normiert zurueckgeben
        return (pos, rot);
    }

    private void CalcPosAndRot(){
        float posUnits = transform.localPosition.z;
        float rotUnits = transform.localEulerAngles.z;

        //string debugString1 = String.Format("{0} | {1}", posUnits, rotUnits);
        //Debug.Log(debugString1);

        if(rotUnits > 180f){
            rotUnits -= 360f;
        }

        pos = Mathf.Lerp(-1, 1, Mathf.InverseLerp(zeroPos[0], zeroPos[1], posUnits));
        rot = Mathf.Lerp(-1, 1, Mathf.InverseLerp(zeroRot[0], zeroRot[1], rotUnits));
    }

    public void SetPosAndRot(float pos, float rot){
        Vector3 newPosition = transform.localPosition;
        newPosition.z = Mathf.Lerp(zeroPos[0], zeroPos[1], pos);
        transform.localPosition = newPosition;

        Vector3 newRotation = transform.localEulerAngles;
        newRotation.z = Mathf.Lerp(zeroRot[0], zeroRot[1], rot);
        transform.localEulerAngles = newRotation;
        //transform.rotation = Quaternion.Euler(newRotation);

    }

    void FixedUpdate(){
        CalcPosAndRot();
        //Pruefen ob Ziel erreicht wurde, dann stoppen (Achtung Overshoot)
        //Falls ja, dann Geschwindigkeit auf 0 setzen
        if(posDirection == 1){
            if(pos>posTarget){
                Rigidbody.velocity = new Vector3(0, 0, 0);
            }
        }else if(posDirection == -1){
            if(pos<posTarget){
                Rigidbody.velocity = new Vector3(0, 0, 0);
            }
        }
/*  ???????????????????????????????????????????????????
        if(rotDirection == 1){
            if(rot>rotTarget){
                Rigidbody.angularVelocity = new Vector3(0, 0, 0);
            }
        }else if(rotDirection == -1){
            if(rot<rotTarget){
                Rigidbody.angularVelocity = new Vector3(0, 0, 0);
            }
        }*/
    }
}
