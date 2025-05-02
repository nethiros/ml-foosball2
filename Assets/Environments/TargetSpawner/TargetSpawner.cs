using UnityEngine;

public class TargetSpawner : MonoBehaviour
{
    public GameObject targetPrefab1; // Erstes Target-Objekt
    public GameObject targetPrefab2; // Zweites Target-Objekt
    public GameObject spawnArea; // Das GameObject, das den Spawn-Bereich definiert
    private GameObject currentTarget1;
    private GameObject currentTarget2;

    public Vector3 AddTarget()
    {
        // Falls bereits ein Target existiert, lösche es zuerst
        if (currentTarget1 != null || currentTarget2 != null)
        {
            DeleteTarget();
        }

        // Berechne zufällige Position innerhalb der SpawnArea
        Vector3 spawnPosition = GetRandomPositionInArea();

        // Erstes Target-Objekt erstellen und skalieren
        currentTarget1 = Instantiate(targetPrefab1, spawnPosition, Quaternion.identity);
        currentTarget1.transform.localScale *= 10;

        // Zweites Target-Objekt erstellen und skalieren
        currentTarget2 = Instantiate(targetPrefab2, spawnPosition, Quaternion.identity);
        currentTarget2.transform.localScale *= 10;

        // Rückgabe der Spawn-Position
        return spawnPosition;
    }

    public void DeleteTarget()
    {
        if (currentTarget1 != null)
        {
            Destroy(currentTarget1);
            currentTarget1 = null;
        }

        if (currentTarget2 != null)
        {
            Destroy(currentTarget2);
            currentTarget2 = null;
        }
    }

    private Vector3 GetRandomPositionInArea()
    {
        // Größe und Position der SpawnArea bestimmen
        Vector3 areaSize = spawnArea.transform.localScale * 10;
        Vector3 areaPosition = spawnArea.transform.position;

        // Zufällige Position innerhalb der SpawnArea berechnen
        float x = Random.Range(areaPosition.x - areaSize.x / 2, areaPosition.x + areaSize.x / 2);
        float z = Random.Range(areaPosition.z - areaSize.z / 2, areaPosition.z + areaSize.z / 2);
        float y = areaPosition.y; // Höhe beibehalten

        return new Vector3(x, y, z);
    }
}
