using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoClientAvatar : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private float smoothTime = 0.08f;

    private Vector3 _velocity;

    public void Bind(UnityMmoClient targetClient)
    {
        client = targetClient;
        if (client != null)
        {
            transform.position = client.AuthoritativePosition;
        }
    }

    private void LateUpdate()
    {
        if (client == null)
        {
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, client.AuthoritativePosition, ref _velocity, smoothTime);
    }
}
}
