using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class BallBehaviourAbwehr : MonoBehaviour
{
    [SerializeField] AgentAbwehr AgentScript;
    [SerializeField] Material BallMaterial;
    [SerializeField] Material TeamMaterial;
    private Renderer Renderer;
    private Rigidbody RB;
    private char hitPlayer = 'N'; //N = None, A = Abwehr, T = Torwart

    private float idleTime = 0f;  // Timer für die Ruhezeit
    private bool isBallIdle = false;  // Überwacht, ob der Ball stillsteht
    private bool ballReadyForReward = false;




    void Start(){
        Renderer = GetComponent<Renderer>();
        RB = GetComponent<Rigidbody>();
    }

    private void OnTriggerEnter(Collider other){
        if(other.gameObject.name=="Backboard"){
            AgentScript.handleBallEvents("BB");
        }
        if (other.gameObject.name == "ImpactArea")
        {
            ballReadyForReward = true;
        }
        //Falls Tor faellt
        if(other.gameObject.name=="Torcollider"){
            ballReadyForReward = false;
            //Debug.Log("Tor");
            if (hitPlayer=='T'){
                AgentScript.handleBallEvents("ET"); //Eigentor bzw. abgefaelscht durch Torwart
            }else if(hitPlayer=='A'){
                AgentScript.handleBallEvents("EA"); //Eigentor bzw. abgefaelscht durch ABwehr
            }else{
                AgentScript.handleBallEvents("GT"); //Tor ohne Eigentkontakt
            }
        }
        else if(other.gameObject.name=="EndArea"){
            AgentScript.trackingBall = false;
        }
    }
    private void OnTriggerExit(Collider other){
        if(other.gameObject.name=="ImpactArea" && (ballReadyForReward || AgentScript.spawnedAt))
        {
            //Debug.Log("Area verlassen");
            if(hitPlayer!='N'){
                AgentScript.handleBallEvents("P"); //Parade bzw. erfolgreich geblockt
            }else{
                if(AgentScript.spawnedAt) {
                    AgentScript.handleBallEvents("P"); //Ball kommt von Spawnort 2, da ist es ok wenn der Ball nicht geblockt wird
                } else {
                    AgentScript.handleBallEvents("BO"); //Ball wurde nicht geblockt
                }
                
                //AgentScript.handleBallEvents("BO"); //Einfach von der Bande abgeprallt und zurueckgerollt
            }
        }
    }
    private void OnCollisionEnter(Collision other){
        //Falls Spieler von Team rot getroffen: Abspeichern und Farbe wechseln
        if(other.gameObject.CompareTag("Torwart_Rot")){
            hitPlayer = 'T';
            Renderer.material = TeamMaterial;
        }else if(other.gameObject.CompareTag("Abwehr_Rot")){
            hitPlayer = 'A';
            Renderer.material = TeamMaterial;
        }
    }
    

    public void resetBall(){
        hitPlayer = 'N';
        //Farbe auf Ausgangsfarbe setzen
        Renderer.material = BallMaterial;
        
    }

    void Update(){
        if (Mathf.Abs(RB.velocity.x) < 0.05f & Mathf.Abs(RB.velocity.z) < 0.05f) // Wenn die Geschwindigkeit des Balls sehr gering ist
        {
            // Wenn der Ball länger stillsteht, erhöhe den Timer
            if (!isBallIdle)
            {
                idleTime += Time.deltaTime;  // Zeit hinzufügen, die seit dem Stillstand vergangen ist
                isBallIdle = true;
            }else{
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
