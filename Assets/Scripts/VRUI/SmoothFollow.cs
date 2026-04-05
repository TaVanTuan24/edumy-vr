using UnityEngine;

public class SmoothFollow : MonoBehaviour
{
    [SerializeField] private Transform target; // Camera.main.transform
    [SerializeField] private float distance = 1.8f;
    [SerializeField] private float smoothSpeed = 2.0f;
    [SerializeField] private Vector3 offsetAngle = new Vector3(15, 0, 0); // Tilt down 15 degrees

    private void Start()
    {
        if (target == null) target = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Position directly in front of camera
        Vector3 targetPosition = target.position + target.forward * distance;
        
        // Slerp to target position for smoothness
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);

        // Face the camera but keep tilted
        Quaternion targetRotation = Quaternion.LookRotation(transform.position - target.position);
        targetRotation *= Quaternion.Euler(offsetAngle);
        
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smoothSpeed);
    }
}
