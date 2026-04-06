using UnityEngine;
using UnityEngine.SceneManagement;

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

        var rect = new Rect(offset.x, offset.y, 420f, 158f);
        GUI.Box(rect, "Rise of Heroes");
        GUI.Label(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 20f), client.StatusText);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 20f), "Zone: " + client.CurrentZoneId + "  Player: " + client.PlayerId);
        var position = client.AuthoritativePosition;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 68f, rect.width - 24f, 20f), "Pos: " + position.x.ToString("F2") + ", " + position.y.ToString("F2") + ", " + position.z.ToString("F2"));
        GUI.Label(new Rect(rect.x + 12f, rect.y + 88f, rect.width - 140f, 20f), "RTT: " + client.LastHeartbeatRttMs.ToString("F0") + " ms  Jitter: " + client.HeartbeatJitterMs.ToString("F0") + " ms");
        GUI.Label(new Rect(rect.x + 12f, rect.y + 108f, rect.width - 24f, 20f), "Assets: " + (string.IsNullOrWhiteSpace(client.AssetBundleStatus) ? "idle" : client.AssetBundleStatus));
        GUI.Label(new Rect(rect.x + 12f, rect.y + 128f, rect.width - 24f, 20f), "E gather | F attack | Q fireball | Tab target mode | Right-click clear | B/C/P/J/R/V/N/K panels | ` toggle");
        if (GUI.Button(new Rect(rect.x + rect.width - 108f, rect.y + 28f, 96f, 24f), "Disconnect"))
        {
            _ = DisconnectAndReturnToLoginAsync();
        }
    }

    private async System.Threading.Tasks.Task DisconnectAndReturnToLoginAsync()
    {
        await client.DisconnectAsync();
        SceneManager.LoadScene(UnityMmoSceneNames.Login);
    }
}
}
