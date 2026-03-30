using UnityEngine;

namespace MMONetworking.ZoneServer
{
public sealed class UnityZoneServerStatusView : MonoBehaviour
{
    [SerializeField] private UnityZoneServerBootstrap bootstrap;
    [SerializeField] private Vector2 offset = new Vector2(16f, 16f);

    private void OnGUI()
    {
        if (bootstrap == null)
        {
            return;
        }

        var rect = new Rect(offset.x, offset.y, 420f, 72f);
        GUI.Box(rect, "Unity Zone Server");
        GUI.Label(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 20f), bootstrap.Status);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 20f), "Dev-mode zone runtime. Hook token validation to the external control plane next.");
    }
}
}
