using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class CodexMmoPlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float sprintMultiplier = 1.65f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float jumpHeight = 1.25f;
    [SerializeField] private Transform cameraAnchor;

    private CharacterController controller;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(horizontal, 0f, vertical);
        input = Vector3.ClampMagnitude(input, 1f);

        Transform basis = cameraAnchor != null ? cameraAnchor : transform;
        Vector3 forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(basis.right, Vector3.up).normalized;
        Vector3 movement = (right * input.x) + (forward * input.z);

        float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * sprintMultiplier : moveSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (controller.isGrounded && Input.GetButtonDown("Jump"))
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = (movement * speed) + (Vector3.up * verticalVelocity);
        controller.Move(velocity * Time.deltaTime);

        if (movement.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 540f * Time.deltaTime);
        }
    }
}
