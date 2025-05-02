using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraControl : MonoBehaviour
{
    [SerializeField] private Camera[] Cameras;
    [SerializeField] private Image fadeImg;
    [SerializeField] private float fadeDuration = 0.1f;
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float slowSpeed = 2f;
    [SerializeField] private float normalSpeed = 5f;
    [SerializeField] private float sprintSpeed = 10f;

    private int active = 0;
    private bool isFading = false;
    private bool moveCamera = false;
    private float currentSpeed;

    void Start()
    {
        SetActiveCamera(active);
    }

    void Update()
    {
        // Kamera wechseln mit 'C'
        if (Input.GetKeyDown(KeyCode.C) && !isFading)
        {
            active++;
            if (active >= Cameras.Length) active = 0;
            SwitchCamera(active);
        }

        // Kamera-Steuerung umschalten mit 'M'
        if (Input.GetKeyDown(KeyCode.M))
        {
            moveCamera = !moveCamera;
            Cursor.visible = !moveCamera;
            Cursor.lockState = moveCamera ? CursorLockMode.Locked : CursorLockMode.None;
        }

        // Falls MoveCamera aktiv ist, Bewegung und Rotation ermöglichen
        if (moveCamera)
        {
            Movement();
            Rotation();
        }
    }

    void SwitchCamera(int a)
    {
        if (!isFading)
        {
            StartCoroutine(FadeAndSwitch(a));
        }
    }

    private IEnumerator FadeAndSwitch(int a)
    {
        isFading = true;
        yield return StartCoroutine(Fade(1f));
        SetActiveCamera(a);
        yield return StartCoroutine(Fade(0f));
        isFading = false;
    }

    private IEnumerator Fade(float targetAlpha)
    {
        float alpha = fadeImg.color.a;
        float time = 0;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            float newAlpha = Mathf.Lerp(alpha, targetAlpha, time / fadeDuration);
            fadeImg.color = new Color(0, 0, 0, newAlpha);
            yield return null;
        }
    }

    void SetActiveCamera(int a)
    {
        for (int c = 0; c < Cameras.Length; c++)
        {
            Cameras[c].enabled = (c == a);
        }
    }

    // Kamera-Rotation mit Mausbewegung
    void Rotation()
    {
        Vector3 mouseInput = new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0);
        Cameras[active].transform.Rotate(mouseInput * sensitivity);

        Vector3 eulerRotation = Cameras[active].transform.rotation.eulerAngles;
        Cameras[active].transform.rotation = Quaternion.Euler(eulerRotation.x, eulerRotation.y, 0);
    }

    // Kamera-Bewegung mit WASD, Space und Strg
    void Movement()
    {
        Vector3 input = new Vector3(Input.GetAxis("Horizontal"), Input.GetAxis("Jump"), Input.GetAxis("Vertical"));

        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed = sprintSpeed;
        }
        else if (Input.GetKey(KeyCode.LeftAlt))
        {
            currentSpeed = slowSpeed;
        }
        else
        {
            currentSpeed = normalSpeed;
        }

        Cameras[active].transform.Translate(input * currentSpeed * Time.deltaTime);
    }
}
