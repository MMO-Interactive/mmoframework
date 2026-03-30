using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoClientStatusView : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Vector2 offset = new Vector2(16f, 16f);

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }
    }

    private void OnGUI()
    {
        if (client == null)
        {
            return;
        }

        var rect = new Rect(offset.x, offset.y, 420f, 96f);
        GUI.Box(rect, "Unity MMO Client");
        GUI.Label(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 20f), client.StatusText);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 20f), "Zone: " + client.CurrentZoneId + "  Player: " + client.PlayerId);
        var position = client.AuthoritativePosition;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 68f, rect.width - 24f, 20f), "Pos: " + position.x.ToString("F2") + ", " + position.y.ToString("F2") + ", " + position.z.ToString("F2"));
    }
}
}
