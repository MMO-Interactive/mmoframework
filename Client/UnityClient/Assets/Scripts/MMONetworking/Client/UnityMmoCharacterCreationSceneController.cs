using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public sealed class UnityMmoCharacterCreationSceneController : MonoBehaviour
{
    private UnityMmoClient _client;
    private string _characterName = "New Adventurer";
    private bool _busy;

    private void Awake()
    {
        _client = FindObjectOfType<UnityMmoClient>();
    }

    private void OnGUI()
    {
        if (_client == null)
        {
            return;
        }

        var card = new Rect(40f, 40f, 520f, 256f);
        GUI.Box(card, "Character Creation");
        GUI.Label(new Rect(card.x + 20f, card.y + 34f, card.width - 40f, 24f), "Create a new skill-based character for Rise of Heroes.");
        GUI.Label(new Rect(card.x + 20f, card.y + 72f, 120f, 22f), "Character name");
        _characterName = GUI.TextField(new Rect(card.x + 20f, card.y + 98f, card.width - 40f, 24f), _characterName ?? string.Empty);

        GUI.enabled = !_busy;
        if (GUI.Button(new Rect(card.x + 20f, card.y + 142f, 180f, 30f), "Create Character"))
        {
            _ = CreateAsync();
        }

        if (GUI.Button(new Rect(card.x + 212f, card.y + 142f, 120f, 30f), "Back"))
        {
            SceneManager.LoadScene(UnityMmoSceneNames.CharacterSelection);
        }

        GUI.enabled = true;
        GUI.Label(new Rect(card.x + 20f, card.y + 190f, card.width - 40f, 36f), _busy ? "Working..." : _client.StatusText);
    }

    private async Task CreateAsync()
    {
        _busy = true;
        try
        {
            if (await _client.TryCreateCharacterAsync(_characterName))
            {
                SceneManager.LoadScene(UnityMmoSceneNames.CharacterSelection);
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
}
