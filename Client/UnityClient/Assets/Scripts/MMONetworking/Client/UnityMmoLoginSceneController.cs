using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public sealed class UnityMmoLoginSceneController : MonoBehaviour
{
    private UnityMmoClient _client;
    private string _accountId;
    private string _password;
    private bool _busy;

    private void Awake()
    {
        _client = FindObjectOfType<UnityMmoClient>();
        if (_client != null)
        {
            _accountId = _client.AccountId;
            _password = _client.Password;
        }
    }

    private void OnGUI()
    {
        if (_client == null)
        {
            return;
        }

        var card = new Rect(40f, 40f, 440f, 284f);
        GUI.Box(card, "Rise of Heroes");
        GUI.Label(new Rect(card.x + 20f, card.y + 34f, card.width - 40f, 24f), "Login");
        GUI.Label(new Rect(card.x + 20f, card.y + 62f, card.width - 40f, 44f), "Sign in to your sandbox account or register a new one before selecting a character.");
        GUI.Label(new Rect(card.x + 20f, card.y + 112f, 96f, 22f), "Account");
        _accountId = GUI.TextField(new Rect(card.x + 120f, card.y + 110f, 280f, 24f), _accountId ?? string.Empty);
        GUI.Label(new Rect(card.x + 20f, card.y + 146f, 96f, 22f), "Password");
        _password = GUI.PasswordField(new Rect(card.x + 120f, card.y + 144f, 280f, 24f), _password ?? string.Empty, '*');

        GUI.enabled = !_busy;
        if (GUI.Button(new Rect(card.x + 20f, card.y + 188f, 180f, 30f), "Continue To Characters"))
        {
            _ = ContinueToCharacterSelectionAsync();
        }

        if (GUI.Button(new Rect(card.x + 220f, card.y + 188f, 180f, 30f), "Register Account"))
        {
            _ = RegisterAccountAsync();
        }

        GUI.enabled = true;
        GUI.Label(new Rect(card.x + 20f, card.y + 232f, card.width - 40f, 40f), _busy ? "Working..." : _client.StatusText);
    }

    private async Task ContinueToCharacterSelectionAsync()
    {
        _busy = true;
        try
        {
            _client.AccountId = _accountId;
            _client.Password = _password;
            var hasCharacters = await _client.TryRefreshCharactersAsync();
            if (!hasCharacters && !string.IsNullOrWhiteSpace(_client.StatusText) && _client.StatusText.IndexOf("Loaded 0 characters.", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            if (_client.Characters.Length == 0)
            {
                SceneManager.LoadScene(UnityMmoSceneNames.CharacterCreation);
                return;
            }

            SceneManager.LoadScene(UnityMmoSceneNames.CharacterSelection);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RegisterAccountAsync()
    {
        _busy = true;
        try
        {
            _client.AccountId = _accountId;
            _client.Password = _password;
            var prepared = await _client.TryRegisterAndPrepareAccountAsync();
            if (!prepared)
            {
                return;
            }

            if (_client.Characters.Length == 0)
            {
                SceneManager.LoadScene(UnityMmoSceneNames.CharacterCreation);
                return;
            }

            SceneManager.LoadScene(UnityMmoSceneNames.CharacterSelection);
        }
        finally
        {
            _busy = false;
        }
    }
}
}
