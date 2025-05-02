using UnityEngine;
using System.IO.Ports;

public class MotorController : MonoBehaviour
{
    [SerializeField] private string portName = "COM3";  //USB Verbidnung Schnittstelle
    [SerializeField] private int baudRate = 115200; // Baudrate f�r senden von Daten
    private SerialPort serialPort;
    private float targetPosition = 0f; // Normiert zwischen -1 und 1
    private float targetRotation = 0f; // Normiert zwischen -1 und 1
    private int translationSpeed = 4000; // Standard-Translationsgeschwindigkeit
    private int rotationSpeed = 12000;   // Standard-Rotationsgeschwindigkeit

    void Start()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();
            Debug.Log("Serielle Verbindung zu " + portName + " ge�ffnet.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Fehler beim �ffnen des Ports: " + e.Message);
        }
    }

    public void SetTargetValues(float pos, float rot)
    {
        targetPosition = pos;
        targetRotation = rot;

        // Befehl sofort senden, wenn Werte gesetzt werden
        SendCommand();
    }

    // Neue Methode zum Setzen der Geschwindigkeiten
    public void SetSpeedValuesTranslation(int transSpeed)
    {
        translationSpeed = transSpeed;
    }
    // Neue Methode zum Setzen der Geschwindigkeiten
    public void SetSpeedValuesRotation(int rotSpeed)
    {
        rotationSpeed = rotSpeed;
    }

    // Neue Methode zum direkten Senden des Befehls
    private void SendCommand()
    {
        // Werte aus der Normierung zur�ckskalieren
        int mappedPosition = (int)Mathf.Lerp(-760, 760, (targetPosition + 1) / 2);
        int mappedRotation = -(int)Mathf.Lerp(-200, 200, (targetRotation + 1) / 2);

        // Geschwindigkeiten anwenden, mit M�glichkeit zur Verlangsamung per Taste
        int currentTransSpeed = translationSpeed;
        int currentRotSpeed = rotationSpeed;

        if (Input.GetKey(KeyCode.G))    // Durch dr�cken von G kann man die Geschwindigkeiten f�r Tests drosseln
        {
            currentTransSpeed = translationSpeed / 3; // Reduzierte Geschwindigkeit
            currentRotSpeed = rotationSpeed / 2;      // Reduzierte Geschwindigkeit
        }

        string data = $"TL:{mappedPosition}:SL:{currentTransSpeed}:TR:{mappedRotation}:SR:{currentRotSpeed}:END" + "\n";
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.WriteLine(data);
            //Debug.Log("Gesendet: " + data);
        }
    }

    void OnApplicationQuit()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
            Debug.Log("Serielle Verbindung geschlossen.");
        }
    }
}