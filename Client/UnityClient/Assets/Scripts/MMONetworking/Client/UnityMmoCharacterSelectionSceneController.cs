using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public sealed class UnityMmoCharacterSelectionSceneController : MonoBehaviour
{
    private UnityMmoClient _client;
    private bool _busy;
    private Vector2 _scroll;

    private async void Awake()
    {
        _client = FindObjectOfType<UnityMmoClient>();
        if (_client != null && _client.Characters.Length == 0)
        {
            await _client.TryRefreshCharactersAsync();
        }
    }

    private void OnGUI()
    {
        if (_client == null)
        {
            return;
        }

        var card = new Rect(40f, 40f, 620f, 420f);
        GUI.Box(card, "Character Selection");
        GUI.Label(new Rect(card.x + 20f, card.y + 34f, card.width - 40f, 22f), "Account: " + _client.AccountId);
        GUI.Label(new Rect(card.x + 20f, card.y + 58f, card.width - 40f, 22f), _busy ? "Working..." : _client.StatusText);

        GUI.enabled = !_busy;
        if (GUI.Button(new Rect(card.x + 20f, card.y + 90f, 160f, 28f), "Refresh Characters"))
        {
            _ = RefreshAsync();
        }

        if (GUI.Button(new Rect(card.x + 190f, card.y + 90f, 160f, 28f), "Create New Character"))
        {
            SceneManager.LoadScene(UnityMmoSceneNames.CharacterCreation);
        }

        if (GUI.Button(new Rect(card.x + 360f, card.y + 90f, 120f, 28f), "Back"))
        {
            SceneManager.LoadScene(UnityMmoSceneNames.Login);
        }

        GUI.enabled = true;

        var viewRect = new Rect(card.x + 20f, card.y + 132f, card.width - 40f, card.height - 152f);
        GUI.Box(viewRect, string.Empty);
        var characters = _client.Characters ?? new UnityMmoClient.CharacterOption[0];
        if (characters.Length == 0)
        {
            GUI.Label(new Rect(viewRect.x + 16f, viewRect.y + 16f, viewRect.width - 32f, 24f), "No characters found for this account.");
            return;
        }

        var contentHeight = 20f + characters.Length * 64f;
        _scroll = GUI.BeginScrollView(viewRect, _scroll, new Rect(0f, 0f, viewRect.width - 24f, contentHeight));
        for (var i = 0; i < characters.Length; i++)
        {
            var character = characters[i];
            var y = 16f + i * 64f;
            var selected = character.characterId == _client.SelectedCharacterId;
            GUI.Box(new Rect(12f, y, viewRect.width - 60f, 52f), string.Empty);
            GUI.Label(new Rect(24f, y + 8f, 280f, 22f), character.characterName);
            GUI.Label(new Rect(24f, y + 28f, 320f, 20f), "Id " + character.characterId + (selected ? " [selected]" : string.Empty));

            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(viewRect.width - 280f, y + 12f, 90f, 28f), "Select"))
            {
                _ = SelectAsync(character.characterId);
            }

            if (GUI.Button(new Rect(viewRect.width - 178f, y + 12f, 90f, 28f), "Play"))
            {
                _ = PlayAsync(character.characterId);
            }

            GUI.enabled = true;
        }

        GUI.EndScrollView();
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        try
        {
            await _client.TryRefreshCharactersAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SelectAsync(ulong characterId)
    {
        _busy = true;
        try
        {
            await _client.TrySelectCharacterAsync(characterId);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PlayAsync(ulong characterId)
    {
        _busy = true;
        try
        {
            if (await _client.TrySelectCharacterAndLoginAsync(characterId))
            {
                SceneManager.LoadScene(UnityMmoSceneNames.World);
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
}
