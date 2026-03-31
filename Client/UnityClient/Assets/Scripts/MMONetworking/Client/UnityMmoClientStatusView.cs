using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoClientStatusView : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Vector2 offset = new Vector2(16f, 16f);
    private string _accountId = "player-local";
    private string _password = "changeme123";
    private string _newCharacterName = "New Adventurer";

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }

        if (client != null)
        {
            _accountId = client.AccountId;
            _password = client.Password;
        }
    }

    private void OnGUI()
    {
        if (client == null)
        {
            return;
        }

        var rect = new Rect(offset.x, offset.y, 460f, client.IsConnected ? 112f : 392f);
        GUI.Box(rect, "Unity MMO Client");
        GUI.Label(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 24f, 20f), client.StatusText);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 48f, rect.width - 24f, 20f), "Zone: " + client.CurrentZoneId + "  Player: " + client.PlayerId);
        var position = client.AuthoritativePosition;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 68f, rect.width - 24f, 20f), "Pos: " + position.x.ToString("F2") + ", " + position.y.ToString("F2") + ", " + position.z.ToString("F2"));

        if (client.IsConnected)
        {
            if (GUI.Button(new Rect(rect.x + rect.width - 108f, rect.y + 28f, 96f, 24f), "Disconnect"))
            {
                _ = client.DisconnectAsync();
            }

            return;
        }

        GUI.Label(new Rect(rect.x + 12f, rect.y + 96f, 88f, 20f), "Account");
        _accountId = GUI.TextField(new Rect(rect.x + 108f, rect.y + 94f, rect.width - 120f, 22f), _accountId);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 124f, 88f, 20f), "Password");
        _password = GUI.PasswordField(new Rect(rect.x + 108f, rect.y + 122f, rect.width - 120f, 22f), _password, '*');

        if (GUI.Button(new Rect(rect.x + 12f, rect.y + 158f, 112f, 28f), "Login"))
        {
            client.AccountId = _accountId;
            client.Password = _password;
            _ = client.LoginAsync();
        }

        if (GUI.Button(new Rect(rect.x + 132f, rect.y + 158f, 140f, 28f), "Register"))
        {
            client.AccountId = _accountId;
            client.Password = _password;
            _ = client.RegisterAsync();
        }

        if (GUI.Button(new Rect(rect.x + 280f, rect.y + 158f, 152f, 28f), "Load Characters"))
        {
            client.AccountId = _accountId;
            client.Password = _password;
            _ = client.RefreshCharactersAsync();
        }

        GUI.Label(new Rect(rect.x + 12f, rect.y + 202f, 180f, 20f), "Create Character");
        _newCharacterName = GUI.TextField(new Rect(rect.x + 12f, rect.y + 226f, 260f, 22f), _newCharacterName);
        if (GUI.Button(new Rect(rect.x + 280f, rect.y + 224f, 152f, 26f), "Create"))
        {
            client.AccountId = _accountId;
            client.Password = _password;
            _ = client.CreateCharacterAsync(_newCharacterName);
        }

        GUI.Label(new Rect(rect.x + 12f, rect.y + 262f, rect.width - 24f, 20f), "Characters");
        var y = rect.y + 286f;
        var characters = client.Characters;
        if (characters == null || characters.Length == 0)
        {
            GUI.Label(new Rect(rect.x + 12f, y, rect.width - 24f, 20f), "No characters loaded yet.");
            return;
        }

        for (var i = 0; i < characters.Length && i < 4; i++)
        {
            var character = characters[i];
            var isSelected = character.characterId == client.SelectedCharacterId;
            GUI.Label(new Rect(rect.x + 12f, y, 210f, 20f), character.characterName + " (" + character.characterId + ")" + (isSelected ? " [selected]" : string.Empty));
            if (GUI.Button(new Rect(rect.x + 228f, y - 2f, 92f, 24f), "Select"))
            {
                client.AccountId = _accountId;
                client.Password = _password;
                _ = client.SelectCharacterAsync(character.characterId);
            }

            if (GUI.Button(new Rect(rect.x + 328f, y - 2f, 104f, 24f), "Play"))
            {
                client.AccountId = _accountId;
                client.Password = _password;
                if (!isSelected)
                {
                    _ = SelectAndLoginAsync(character.characterId);
                }
                else
                {
                    _ = client.LoginAsync();
                }
            }

            y += 28f;
        }
    }

    private async System.Threading.Tasks.Task SelectAndLoginAsync(ulong characterId)
    {
        await client.SelectCharacterAsync(characterId);
        await client.LoginAsync();
    }
}
}
