using UnityEngine;

public sealed class VoxelDemoFlyCamera : MonoBehaviour
{
    private float _yaw = 135f;
    private float _pitch = 18f;

    private void Start()
    {
        ApplyRotation();
    }

    private void Update()
    {
        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * 3f;
            _pitch -= Input.GetAxis("Mouse Y") * 3f;
            _pitch = Mathf.Clamp(_pitch, -80f, 80f);
            ApplyRotation();
        }

        var speed = Input.GetKey(KeyCode.LeftShift) ? 18f : 8f;
        var vertical = 0f;
        if (Input.GetKey(KeyCode.E))
        {
            vertical += 1f;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            vertical -= 1f;
        }

        var move = new Vector3(
            Input.GetAxisRaw("Horizontal"),
            vertical,
            Input.GetAxisRaw("Vertical"));

        if (move.sqrMagnitude > 0f)
        {
            transform.position += transform.TransformDirection(move.normalized) * (speed * Time.deltaTime);
        }
    }

    private void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }
}
