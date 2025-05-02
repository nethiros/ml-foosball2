using UnityEngine;
public class BallPredictionZone : MonoBehaviour
{
    [SerializeField] private AgentTorwart torwartAgent;

    private void Start()
    {
        if (torwartAgent == null)
        {
            Debug.LogError("BallPredictionZone: AgentTorwart nicht zugewiesen!");
        }
        // Stelle sicher, dass dieses GameObject einen Collider mit isTrigger=true hat
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogError("BallPredictionZone benötigt einen Collider!");
        }
        else if (!col.isTrigger)
        {
            Debug.LogWarning("BallPredictionZone Collider sollte als Trigger konfiguriert sein!");
            col.isTrigger = true;
        }
    }
}