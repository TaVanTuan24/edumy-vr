using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
/// <summary>
/// Simple fly camera controls for Play Mode testing in Unity Editor.
/// Hold Right Mouse to look around, use WASD + QE to move.
/// </summary>
public class EditorFlyCameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float fastMoveMultiplier = 3.0f;
    [SerializeField] private float lookSensitivity = 2.0f;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        Vector3 euler = transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse == null || keyboard == null) return;

        bool isLooking = mouse.rightButton.isPressed;
        if (isLooking)
        {
            Vector2 mouseDelta = mouse.delta.ReadValue() * 0.1f;
            float mouseX = mouseDelta.x * lookSensitivity;
            float mouseY = mouseDelta.y * lookSensitivity;

            yaw += mouseX;
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        float speed = moveSpeed;
        if (keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed)
        {
            speed *= fastMoveMultiplier;
        }

        Vector3 move = Vector3.zero;
        if (keyboard[Key.W].isPressed) move += transform.forward;
        if (keyboard[Key.S].isPressed) move -= transform.forward;
        if (keyboard[Key.D].isPressed) move += transform.right;
        if (keyboard[Key.A].isPressed) move -= transform.right;
        if (keyboard[Key.E].isPressed) move += Vector3.up;
        if (keyboard[Key.Q].isPressed) move -= Vector3.up;

        if (move.sqrMagnitude > 0.0001f)
        {
            transform.position += move.normalized * speed * Time.unscaledDeltaTime;
        }
    }
}
#endif
