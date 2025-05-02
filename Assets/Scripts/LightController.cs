using UnityEngine;
using System.IO.Ports;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Needed for Average()

// Anschalten von Lampe in ESP für Roundtriptime-Messung. Unwichtig für spätere Implementierung
public class LightController : MonoBehaviour
{
    [Header("Serial Port Configuration")]
    [SerializeField] private string portName = "COM3"; 
    [SerializeField] private int baudRate = 115200; 
    private SerialPort serialPort;

    // Target values (might be used elsewhere)
    private float targetPosition = 0f; 
    private float targetRotation = 0f; 
    private int lightVal = 0; 

    // Speeds
    private int translationSpeed = 8000; 
    private int rotationSpeed = 12000; 

    [Header("Measurement Sequence")]
    [SerializeField] private KeyCode startMeasurementKey = KeyCode.L; // Changed from M to L as per description
    [SerializeField] private int totalMeasurements = 10; // Number of measurements
    [SerializeField] private float timeoutSeconds = 2.0f; // Timeout for waiting for light detection
    [SerializeField] private float delayAfterLightOffMs = 300f; // Delay *after* light turns off before next measurement

    private List<float> latencyMeasurements = new List<float>(); //  Stores latency values in seconds
    private Coroutine measurementCoroutine = null; 
    private bool lightDetectedEventReceived = false; // Flag set by the event
    private float currentMeasurementStartTime = -1f;

    void Start()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open(); 
            Debug.Log($"Serial connection to {portName} opened."); 
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error opening port {portName}: {e.Message}"); 
        }
    }

    void OnEnable()
    {
        // Subscribe to the event from ReceiveCoordsLight
        ReceiveCoordsLight.OnLightTurnedOnDetected += HandleLightTurnedOnDetected;
    }

    void OnDisable()
    {
        // Unsubscribe when the object is disabled or destroyed
        ReceiveCoordsLight.OnLightTurnedOnDetected -= HandleLightTurnedOnDetected;
        // Stop coroutine if it's running
        if (measurementCoroutine != null)
        {
            StopCoroutine(measurementCoroutine);
            measurementCoroutine = null;
            Debug.Log("Measurement coroutine stopped on disable.");
            // Ensure light is off if disabled mid-sequence
            if (serialPort != null && serialPort.IsOpen && lightVal == 1)
            {
                lightVal = 0;
                SendCommand(false); // Send command without debug log spam
            }
        }
    }

    void Update()
    {
        // Start the measurement sequence when the key is pressed
        if (Input.GetKeyDown(startMeasurementKey))
        {
            if (measurementCoroutine == null)
            {
                measurementCoroutine = StartCoroutine(RunMeasurementSequence());
            }
            else
            {
                Debug.LogWarning("Measurement sequence already running.");
            }
        }

        // Manual override to turn light OFF (e.g., for testing)
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (measurementCoroutine != null)
            {
                Debug.LogWarning("Manual light OFF pressed during measurement sequence. Stopping sequence.");
                StopCoroutine(measurementCoroutine);
                measurementCoroutine = null;
            }
            lightVal = 0; 
            SendCommand(); 
            Debug.Log($"[{Time.time:F3}] K pressed. Light OFF command sent manually.");
        }

        // Optional: Speed change handling
        if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyUp(KeyCode.G))
        {
            SendCommand(false); // Resend command with potentially new speed, suppress log spam
        }
    }

    // This method is called by the event from ReceiveCoordsLight
    private void HandleLightTurnedOnDetected()
    {
        if (measurementCoroutine != null && currentMeasurementStartTime > 0) // Check if we are actually measuring
        {
            lightDetectedEventReceived = true; // Signal that the event was received
                                               // Latency calculation will happen within the coroutine after it resumes
            Debug.Log($"[{Time.time:F3}] Event 'OnLightTurnedOnDetected' received."); // adaptation
        }
        else
        {
            Debug.LogWarning($"[{Time.time:F3}] Event 'OnLightTurnedOnDetected' received, but not currently waiting for it.");
        }
    }

    // Coroutine for the measurement sequence
    private IEnumerator RunMeasurementSequence()
    {
        Debug.Log($"Starting measurement sequence: {totalMeasurements} iterations.");
        latencyMeasurements.Clear(); // Clear previous results

        // Initial state: Ensure light is off
        lightVal = 0;
        SendCommand(false); // Send command without log spam
        yield return new WaitForSeconds(0.1f); // Small delay to ensure command is processed

        for (int i = 0; i < totalMeasurements; i++)
        {
            Debug.Log($"--- Measurement Cycle {i + 1} / {totalMeasurements} ---");

            // 1. Reset flag and timer
            lightDetectedEventReceived = false; // Reset flag for this measurement
            currentMeasurementStartTime = -1f;
            float detectionTime = -1f;


            // 2. Turn Light ON
            lightVal = 1; // adaptation
            SendCommand(); // Send command WITH log this time
            currentMeasurementStartTime = Time.time; // Record start time *after* sending adaptation
            Debug.Log($"[{currentMeasurementStartTime:F3}] Light ON command sent.");


            // 3. Wait for light detection event OR timeout
            float waitStartTime = Time.time;
            while (!lightDetectedEventReceived && (Time.time - waitStartTime) < timeoutSeconds)
            {
                yield return null; // Wait for the next frame
            }

            // 4. Process result
            if (lightDetectedEventReceived)
            {
                detectionTime = Time.time;
                float latency = detectionTime - currentMeasurementStartTime;
                latencyMeasurements.Add(latency);
                Debug.Log($"[{detectionTime:F3}] Light ON detected by Receiver. Latency: {latency * 1000:F1} ms");
            }
            else
            {
                Debug.LogWarning($"[{Time.time:F3}] Timeout! Light detection event not received within {timeoutSeconds} seconds for measurement {i + 1}.");
                // Optionally add a placeholder or skip this measurement for average calculation
                // latencyMeasurements.Add(float.NaN); // Or handle as needed
            }

            // Reset start time marker
            currentMeasurementStartTime = -1f;

            // 5. Turn Light OFF
            lightVal = 0;
            SendCommand();
            Debug.Log($"[{Time.time:F3}] Light OFF command sent.");

            // 6. Wait specified delay before next measurement
            float delayMilliseconds = delayAfterLightOffMs; // Use the specified delay
            Debug.Log($"Waiting for {delayMilliseconds} ms...");
            yield return new WaitForSeconds(delayMilliseconds / 1000f); // Convert ms to seconds
        }

        // Sequence finished
        Debug.Log("--- Measurement Sequence Finished ---");
        if (latencyMeasurements.Count > 0)
        {
            // Filter out potential NaN values if you added them on timeout
            List<float> validMeasurements = latencyMeasurements.Where(t => !float.IsNaN(t)).ToList();
            if (validMeasurements.Count > 0)
            {
                float averageLatency = validMeasurements.Average(); // Calculate average
                float minLatency = validMeasurements.Min();
                float maxLatency = validMeasurements.Max();
                Debug.Log($"Results ({validMeasurements.Count} valid measurements):");
                Debug.Log($" - Average Latency: {averageLatency * 1000:F1} ms");
                Debug.Log($" - Minimum Latency: {minLatency * 1000:F1} ms");
                Debug.Log($" - Maximum Latency: {maxLatency * 1000:F1} ms");
            }
            else
            {
                Debug.Log("No valid latency measurements recorded.");
            }

        }
        else
        {
            Debug.Log("No latency measurements recorded (possibly all timed out).");
        }

        measurementCoroutine = null; // Allow starting a new sequence
    }


    // This method can remain if needed elsewhere
    public void SetTargetValues(float pos, float rot, int lightValue)
    {
        targetPosition = pos; 
        targetRotation = rot; 
        lightVal = lightValue; 
        SendCommand(); // Send command immediately
    }

    // Speed setters remain unchanged
    public void SetSpeedValuesTranslation(int transSpeed)
    {
        translationSpeed = transSpeed;
    }
    public void SetSpeedValuesRotation(int rotSpeed)
    {
        rotationSpeed = rotSpeed;
    }

    // SendCommand remains mostly the same, added optional logging suppression
    private void SendCommand(bool logSend = true) // Added optional parameter
    {
        if (serialPort == null || !serialPort.IsOpen) return;

        int mappedPosition = (int)Mathf.Lerp(-760, 760, (targetPosition + 1) / 2); 
        int mappedRotation = -(int)Mathf.Lerp(-200, 200, (targetRotation + 1) / 2);

        int currentTransSpeed = translationSpeed; 
        int currentRotSpeed = rotationSpeed; 
        int currentlightVal = this.lightVal; // Use class variable

        if (Input.GetKey(KeyCode.G)) // Speed reduction
        {
            currentTransSpeed = translationSpeed / 3;
            currentRotSpeed = rotationSpeed / 2;
        }

        string data = $"TL:{mappedPosition}:SL:{currentTransSpeed}:TR:{mappedRotation}:SR:{currentRotSpeed}:LED:{currentlightVal}:END\n";

        try
        {
            serialPort.Write(data);
            if (logSend)
            {
                // Debug.Log("Sent: " + data.TrimEnd()); // Optional: Log only when needed
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error sending data to Serial Port ({portName}): {e.Message}"); 
        }
    }

    void OnApplicationQuit()
    {
        if (measurementCoroutine != null)
        {
            StopCoroutine(measurementCoroutine);
            measurementCoroutine = null;
        }

        if (serialPort != null && serialPort.IsOpen)
        {
            // Ensure light is off on exit
            string endData = $"TL:0:SL:{translationSpeed}:TR:0:SR:{rotationSpeed}:LED:0:END\n"; 
            try { serialPort.Write(endData); } catch { /* Ignore errors on quit */ }

            serialPort.Close();
            Debug.Log("Serial connection closed."); 
        }
    }
}