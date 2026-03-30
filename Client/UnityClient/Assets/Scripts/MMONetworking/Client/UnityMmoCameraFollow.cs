using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 9f, -11f);
    [SerializeField] private float smooth = 6f;

    public void Bind(Transform followTarget)
    {
        target = followTarget;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        var desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smooth);
        transform.LookAt(target.position + new Vector3(0f, 1.25f, 0f));
    }
}
}
