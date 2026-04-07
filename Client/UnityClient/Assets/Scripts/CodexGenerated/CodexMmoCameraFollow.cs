using UnityEngine;

public sealed class CodexMmoCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 2.25f, -4.5f);
    [SerializeField] private float followSharpness = 12f;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + target.TransformDirection(offset);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        transform.LookAt(target.position + Vector3.up * 1.35f);
    }
}
