using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading; // Optional für separaten Thread

// Latenzmessung. Nicht benötigt für späteren Aufbau

public class UDPEchoServer : MonoBehaviour
{
    public int listenPort = 4040; // Port, auf dem Unity lauscht

    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint; // Um den Absender zu speichern
    private bool isRunning = false;
    private Thread receiveThread; // Optional: Eigener Thread für Netzwerkoperationen

    void Start()
    {
        try
        {
            // UdpClient erstellen und an den Port binden
            udpClient = new UdpClient(listenPort);
            remoteEndPoint = new IPEndPoint(IPAddress.Any, 0); // Akzeptiert Pakete von jeder IP/Port
            isRunning = true;

            Debug.Log($"UDP Echo Server gestartet auf Port {listenPort}...");

            // --- Methode 1: Asynchroner Empfang (bevorzugt in Unity) ---
            udpClient.BeginReceive(ReceiveCallback, null);

            // --- Methode 2: Empfang in einem separaten Thread (alternativ) ---
            // receiveThread = new Thread(new ThreadStart(ReceiveData));
            // receiveThread.IsBackground = true; // Stellt sicher, dass der Thread mit Unity beendet wird
            // receiveThread.Start();

        }
        catch (Exception e)
        {
            Debug.LogError($"Fehler beim Starten des UDP Servers: {e.Message}");
            isRunning = false;
        }
    }

    // --- Methode 1: Callback für asynchronen Empfang ---
    private void ReceiveCallback(IAsyncResult ar)
    {
        if (!isRunning || udpClient == null) return; // Beenden, wenn nicht mehr läuft

        try
        {
            IPEndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] receivedBytes = udpClient.EndReceive(ar, ref receiveEndPoint);

            // Debug.Log($"UDP Paket empfangen von {receiveEndPoint}. Größe: {receivedBytes.Length} Bytes."); // Optional: Debug-Ausgabe

            // Empfangene Daten sofort zurück an den Absender senden
            if (receivedBytes.Length > 0)
            {
                udpClient.Send(receivedBytes, receivedBytes.Length, receiveEndPoint);
                // Debug.Log($"UDP Echo gesendet an {receiveEndPoint}."); // Optional: Debug-Ausgabe
            }

            // Erneut auf den nächsten Empfang lauschen
            if (isRunning)
            {
                udpClient.BeginReceive(ReceiveCallback, null);
            }
        }
        catch (ObjectDisposedException)
        {
            // Erwartete Ausnahme, wenn der Client geschlossen wird, während gewartet wird. Ignorieren.
        }
        catch (SocketException e)
        {
            // Nur loggen, wenn der Server noch laufen sollte
            if (isRunning) Debug.LogError($"Socket Fehler beim Empfangen/Senden: {e.Message} (ErrorCode: {e.SocketErrorCode})");
        }
        catch (Exception e)
        {
            if (isRunning) Debug.LogError($"Fehler im ReceiveCallback: {e.Message}\n{e.StackTrace}");
        }
    }

    // --- Methode 2: Empfangs-Loop in separatem Thread (alternativ zu Methode 1) ---
    /*
    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                // Blockierender Aufruf: Wartet hier auf Daten
                byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);

                // Debug.Log($"UDP Paket empfangen von {remoteEndPoint}. Größe: {receivedBytes.Length} Bytes."); // Optional

                // Daten sofort zurücksenden
                if (receivedBytes.Length > 0)
                {
                    udpClient.Send(receivedBytes, receivedBytes.Length, remoteEndPoint);
                    // Debug.Log($"UDP Echo gesendet an {remoteEndPoint}."); // Optional
                }
            }
            catch (SocketException e)
            {
                // Wenn der Socket geschlossen wird, während Receive() blockiert,
                // wird eine SocketException ausgelöst. Code 10004 ist typisch beim Schließen.
                if (e.SocketErrorCode == 10004 || !isRunning)
                {
                    Debug.Log("UDP Listener wird gestoppt.");
                    break; // Schleife verlassen
                }
                else
                {
                     // Nur loggen, wenn der Server noch laufen sollte
                     if(isRunning) Debug.LogError($"Socket Fehler beim Empfangen/Senden: {e.Message} (ErrorCode: {e.SocketErrorCode})");
                }
            }
            catch (Exception e)
            {
                if(isRunning) Debug.LogError($"Fehler in ReceiveData Loop: {e.Message}\n{e.StackTrace}");
                // Kurze Pause bei unerwartetem Fehler, um CPU-Spins zu vermeiden
                Thread.Sleep(10);
            }
        }
    }
    */


    void OnDestroy() // Wird aufgerufen, wenn das GameObject zerstört wird
    {
        StopServer();
    }

    void OnApplicationQuit() // Wird aufgerufen, wenn die Anwendung beendet wird
    {
        StopServer();
    }

    private void StopServer()
    {
        isRunning = false;

        if (udpClient != null)
        {
            try
            {
                // Wichtig: Schließen unterbricht auch blockierende oder asynchrone Aufrufe
                udpClient.Close();
                udpClient = null;
                Debug.Log("UDP Echo Server gestoppt.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Fehler beim Stoppen des UDP Servers: {e.Message}");
            }
        }

        // Optional: Wenn Thread-Methode verwendet wird, sicherstellen, dass er beendet ist
        // if (receiveThread != null && receiveThread.IsAlive)
        // {
        //     receiveThread.Join(500); // Kurz warten, bis der Thread beendet ist
        // }
    }
}