using System;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoWorldInteractionController : MonoBehaviour
{
    private enum SelectionMode
    {
        PreferNode,
        PreferMob
    }

    [SerializeField] private UnityMmoClient client;
    [SerializeField] private UnityMmoResourceNodeSystem resourceNodeSystem;
    [SerializeField] private UnityMmoMobSystem mobSystem;
    [SerializeField] private UnityMmoNpcSystem npcSystem;
    [SerializeField] private UnityMmoRemoteAvatarSystem remoteAvatarSystem;
    [SerializeField] private UnityMmoGameplayPanel gameplayPanel;
    [SerializeField] private UnityMmoSpellEffectSystem spellEffectSystem;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float interactionRadius = 9f;
    [SerializeField] private float labelDrawDistance = 26f;

    private ResourceNodeSnapshot? _selectedNode;
    private MobSnapshot? _selectedMob;
    private NpcSnapshot? _selectedNpc;
    private PlayerSnapshot? _selectedPlayer;
    private NpcSnapshot? _activeNpcDialog;
    private bool _busy;
    private SelectionMode _selectionMode;
    private bool _manualSelection;
    private string _selectionHint = string.Empty;

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }

        if (resourceNodeSystem == null)
        {
            resourceNodeSystem = FindObjectOfType<UnityMmoResourceNodeSystem>();
        }

        if (mobSystem == null)
        {
            mobSystem = FindObjectOfType<UnityMmoMobSystem>();
        }

        if (npcSystem == null)
        {
            npcSystem = FindObjectOfType<UnityMmoNpcSystem>();
        }

        if (remoteAvatarSystem == null)
        {
            remoteAvatarSystem = FindObjectOfType<UnityMmoRemoteAvatarSystem>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (gameplayPanel == null)
        {
            gameplayPanel = FindObjectOfType<UnityMmoGameplayPanel>();
        }

        if (spellEffectSystem == null)
        {
            spellEffectSystem = FindObjectOfType<UnityMmoSpellEffectSystem>();
        }
    }

    private void Update()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TrySelectFromPointer();
        }

        RefreshSelection();

        if (Input.GetKeyDown(KeyCode.E))
        {
            _ = InteractAsync();
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            gameplayPanel?.FocusInventory();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            gameplayPanel?.FocusCrafting();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            gameplayPanel?.FocusCombat();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            _ = AttackAsync();
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            _ = CastFireballAsync();
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            gameplayPanel?.FocusQuests();
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            gameplayPanel?.FocusProgression();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            gameplayPanel?.FocusReputation();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            gameplayPanel?.FocusEvents();
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            gameplayPanel?.FocusInvasions();
        }

        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            gameplayPanel?.ToggleCollapsed();
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CyclePriority();
        }

        if (Input.GetMouseButtonDown(1))
        {
            ClearSelection();
        }
    }

    private void OnGUI()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        DrawSelectionPanel();
        DrawNpcDialog();
        DrawWorldLabels();
        DrawActionBar();
    }

    private void DrawSelectionPanel()
    {
        var rect = new Rect(Screen.width - 320f, 16f, 296f, 120f);
        GUI.Box(rect, "Target");

        if (_selectedNode.HasValue)
        {
            var node = _selectedNode.Value;
            var distance = Vector3.Distance(client.AuthoritativePosition, new Vector3(node.Position.X, node.Position.Y, node.Position.Z));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 28f, rect.width - 32f, 20f), node.ResourceId + " [" + node.NodeId + "]");
            GUI.Label(new Rect(rect.x + 16f, rect.y + 50f, rect.width - 32f, 20f), "Remaining " + node.Remaining + " / " + node.MaxAmount + "  Dist " + distance.ToString("F1"));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 72f, rect.width - 32f, 20f), "Press E to gather" + (_manualSelection ? " | Right-click to clear" : string.Empty));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(rect.x + rect.width - 110f, rect.y + 44f, 92f, 32f), "Gather"))
            {
                _ = InteractAsync();
            }
            GUI.enabled = true;
            return;
        }

        if (_selectedPlayer.HasValue)
        {
            var player = _selectedPlayer.Value;
            var distance = Vector3.Distance(client.AuthoritativePosition, new Vector3(player.Position.X, player.Position.Y, player.Position.Z));
            var combatant = FindCombatant(player.PlayerId);
            var hpText = combatant != null
                ? "HP " + combatant.hitPoints + "/" + combatant.maxHitPoints + "  Stam " + combatant.stamina
                : "Combat profile not loaded";
            GUI.Label(new Rect(rect.x + 16f, rect.y + 28f, rect.width - 32f, 20f), "Player [" + player.PlayerId + "]");
            GUI.Label(new Rect(rect.x + 16f, rect.y + 50f, rect.width - 32f, 20f), hpText + "  Dist " + distance.ToString("F1"));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 72f, rect.width - 32f, 20f), "Press F to attack" + (_manualSelection ? " | Right-click to clear" : string.Empty));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(rect.x + rect.width - 110f, rect.y + 44f, 92f, 32f), "Attack"))
            {
                _ = AttackAsync();
            }
            GUI.enabled = true;
            return;
        }

        if (_selectedNpc.HasValue)
        {
            var npc = _selectedNpc.Value;
            var distance = Vector3.Distance(client.AuthoritativePosition, new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 28f, rect.width - 32f, 20f), npc.DisplayName + " [" + npc.NpcTypeId + "]");
            GUI.Label(new Rect(rect.x + 16f, rect.y + 50f, rect.width - 32f, 20f), "Role " + npc.PrimaryRole + "  Dist " + distance.ToString("F1"));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 72f, rect.width - 32f, 20f), "Press E to interact" + (_manualSelection ? " | Right-click to clear" : string.Empty));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(rect.x + rect.width - 110f, rect.y + 44f, 92f, 32f), "Talk"))
            {
                _ = InteractAsync();
            }
            GUI.enabled = true;
            return;
        }

        if (_selectedMob.HasValue)
        {
            var mob = _selectedMob.Value;
            var distance = Vector3.Distance(client.AuthoritativePosition, new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 28f, rect.width - 32f, 20f), mob.MobTypeId + " [" + mob.MobId + "]");
            GUI.Label(new Rect(rect.x + 16f, rect.y + 50f, rect.width - 32f, 20f), "HP " + mob.HitPoints + "/" + mob.MaxHitPoints + "  " + mob.State + "  Dist " + distance.ToString("F1"));
            GUI.Label(new Rect(rect.x + 16f, rect.y + 72f, rect.width - 32f, 20f), "Press F attack / Q fireball" + (_manualSelection ? " | Right-click to clear" : string.Empty));
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(rect.x + rect.width - 110f, rect.y + 44f, 92f, 32f), "Attack"))
            {
                _ = AttackAsync();
            }
            if (GUI.Button(new Rect(rect.x + rect.width - 110f, rect.y + 80f, 92f, 24f), "Fireball"))
            {
                _ = CastFireballAsync();
            }
            GUI.enabled = true;
            return;
        }

        GUI.Label(new Rect(rect.x + 16f, rect.y + 36f, rect.width - 32f, 20f), "No nearby target.");
        GUI.Label(new Rect(rect.x + 16f, rect.y + 60f, rect.width - 32f, 20f), string.IsNullOrWhiteSpace(_selectionHint) ? "Move closer to nodes or mobs." : _selectionHint);
    }

    private void DrawWorldLabels()
    {
        if (targetCamera == null)
        {
            return;
        }

        var playerPosition = client.AuthoritativePosition;
        foreach (var node in resourceNodeSystem != null ? resourceNodeSystem.GetVisibleNodes() : Array.Empty<ResourceNodeSnapshot>())
        {
            DrawLabel(
                new Vector3(node.Position.X, node.Position.Y + 1.6f, node.Position.Z),
                node.ResourceId,
                (new Vector3(node.Position.X, node.Position.Y, node.Position.Z) - playerPosition).sqrMagnitude,
                node.NodeId == (_selectedNode?.NodeId ?? string.Empty));
        }

        foreach (var mob in mobSystem != null ? mobSystem.GetVisibleMobs() : Array.Empty<MobSnapshot>())
        {
            DrawLabel(
                new Vector3(mob.Position.X, mob.Position.Y + 2f, mob.Position.Z),
                mob.MobTypeId,
                (new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z) - playerPosition).sqrMagnitude,
                mob.MobId == (_selectedMob?.MobId ?? string.Empty));
        }

        foreach (var npc in npcSystem != null ? npcSystem.GetVisibleNpcs() : Array.Empty<NpcSnapshot>())
        {
            DrawLabel(
                new Vector3(npc.Position.X, npc.Position.Y + 2.1f, npc.Position.Z),
                npc.DisplayName,
                (new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z) - playerPosition).sqrMagnitude,
                npc.NpcId == (_selectedNpc?.NpcId ?? string.Empty));
        }

        foreach (var player in remoteAvatarSystem != null ? remoteAvatarSystem.GetVisibleRemotePlayers() : Array.Empty<PlayerSnapshot>())
        {
            DrawLabel(
                new Vector3(player.Position.X, player.Position.Y + 2.1f, player.Position.Z),
                "P" + player.PlayerId,
                (new Vector3(player.Position.X, player.Position.Y, player.Position.Z) - playerPosition).sqrMagnitude,
                player.PlayerId == (_selectedPlayer?.PlayerId ?? 0UL));
        }
    }

    private void DrawLabel(Vector3 worldPosition, string text, float distanceSq, bool selected)
    {
        if (distanceSq > labelDrawDistance * labelDrawDistance)
        {
            return;
        }

        var screen = targetCamera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
        {
            return;
        }

        var rect = new Rect(screen.x - 60f, Screen.height - screen.y - 12f, 120f, 22f);
        var previousColor = GUI.color;
        GUI.color = selected ? new Color(1f, 0.87f, 0.45f, 0.95f) : new Color(0.08f, 0.09f, 0.12f, 0.78f);
        GUI.Box(rect, string.Empty);
        GUI.color = selected ? Color.black : Color.white;
        GUI.Label(rect, text);
        GUI.color = previousColor;
    }

    private async System.Threading.Tasks.Task InteractAsync()
    {
        if (_busy || client == null)
        {
            return;
        }

        if (_selectedNode.HasValue)
        {
            _busy = true;
            try
            {
                await client.ExecuteGatherAsync(_selectedNode.Value.NodeId);
                if (gameplayPanel != null)
                {
                    gameplayPanel.FocusInventory();
                }
            }
            finally
            {
                _busy = false;
            }
        }
        else if (_selectedNpc.HasValue)
        {
            InteractWithNpc(_selectedNpc.Value);
        }
    }

    private async System.Threading.Tasks.Task AttackAsync()
    {
        if (_busy || client == null)
        {
            return;
        }

        _busy = true;
        try
        {
            if (_selectedPlayer.HasValue)
            {
                await client.EnsureCombatAsync();
                await client.AttackCharacterAsync(_selectedPlayer.Value.PlayerId);
                gameplayPanel?.FocusCombat();
            }
            else if (_selectedMob.HasValue)
            {
                await client.ExecuteMobAttackAsync(_selectedMob.Value.MobId);
            }
            else
            {
                return;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async System.Threading.Tasks.Task CastFireballAsync()
    {
        if (_busy || client == null || !_selectedMob.HasValue)
        {
            return;
        }

        _busy = true;
        try
        {
            var mob = _selectedMob.Value;
            if (spellEffectSystem == null)
            {
                spellEffectSystem = FindObjectOfType<UnityMmoSpellEffectSystem>();
                if (spellEffectSystem == null)
                {
                    var spellEffectsRoot = new GameObject("Spell Effect System");
                    spellEffectSystem = spellEffectsRoot.AddComponent<UnityMmoSpellEffectSystem>();
                }
            }

            var origin = client.AuthoritativePosition + new Vector3(0f, 1.45f, 0f);
            var target = new Vector3(mob.Position.X, mob.Position.Y + 1.2f, mob.Position.Z);
            spellEffectSystem?.LaunchFireball(origin, target);
            await client.CastFireballAsync(mob.MobId);
        }
        finally
        {
            _busy = false;
        }
    }

    private void RefreshSelection()
    {
        if (_manualSelection)
        {
            if (_selectedNode.HasValue)
            {
                var nodePosition = new Vector3(_selectedNode.Value.Position.X, _selectedNode.Value.Position.Y, _selectedNode.Value.Position.Z);
                if ((nodePosition - client.AuthoritativePosition).sqrMagnitude <= interactionRadius * interactionRadius)
                {
                    return;
                }
            }

            if (_selectedMob.HasValue)
            {
                var mobPosition = new Vector3(_selectedMob.Value.Position.X, _selectedMob.Value.Position.Y, _selectedMob.Value.Position.Z);
                if ((mobPosition - client.AuthoritativePosition).sqrMagnitude <= interactionRadius * interactionRadius)
                {
                    return;
                }
            }

            if (_selectedNpc.HasValue)
            {
                var npcPosition = new Vector3(_selectedNpc.Value.Position.X, _selectedNpc.Value.Position.Y, _selectedNpc.Value.Position.Z);
                if ((npcPosition - client.AuthoritativePosition).sqrMagnitude <= interactionRadius * interactionRadius)
                {
                    return;
                }
            }

            if (_selectedPlayer.HasValue)
            {
                var selectedPlayerPosition = new Vector3(_selectedPlayer.Value.Position.X, _selectedPlayer.Value.Position.Y, _selectedPlayer.Value.Position.Z);
                if ((selectedPlayerPosition - client.AuthoritativePosition).sqrMagnitude <= labelDrawDistance * labelDrawDistance)
                {
                    return;
                }
            }

            _manualSelection = false;
        }

        var playerPosition = client.AuthoritativePosition;
        ResourceNodeSnapshot nearestNode = default;
        MobSnapshot nearestMob = default;
        NpcSnapshot nearestNpc = default;
        var hasNode = resourceNodeSystem != null && resourceNodeSystem.TryGetNearestNode(playerPosition, interactionRadius, out nearestNode);
        var hasMob = mobSystem != null && mobSystem.TryGetNearestMob(playerPosition, interactionRadius, out nearestMob);
        var hasNpc = npcSystem != null && npcSystem.TryGetNearestNpc(playerPosition, interactionRadius, out nearestNpc);

        if (hasNode)
        {
            _selectedNode = nearestNode;
        }
        else
        {
            _selectedNode = null;
        }

        if (hasMob)
        {
            _selectedMob = nearestMob;
        }
        else
        {
            _selectedMob = null;
        }

        if (hasNpc && !_selectedNode.HasValue && !_selectedMob.HasValue)
        {
            _selectedNpc = nearestNpc;
        }
        else
        {
            _selectedNpc = null;
        }

        if (!_manualSelection)
        {
            _selectedPlayer = null;
        }

        if (_selectedNode.HasValue && _selectedMob.HasValue)
        {
            if (_selectionMode == SelectionMode.PreferMob)
            {
                _selectedNode = null;
            }
            else
            {
                _selectedMob = null;
            }
        }
    }

    private void CyclePriority()
    {
        _selectionMode = _selectionMode == SelectionMode.PreferNode
            ? SelectionMode.PreferMob
            : SelectionMode.PreferNode;
        _manualSelection = false;
        _selectionHint = _selectionMode == SelectionMode.PreferMob
            ? "Target mode: mobs first."
            : "Target mode: nodes first.";
        RefreshSelection();
    }

    private void TrySelectFromPointer()
    {
        if (targetCamera == null)
        {
            return;
        }

        var ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        if (resourceNodeSystem != null && resourceNodeSystem.TrySelectNodeFromRay(ray, out var node))
        {
            _selectedNode = node;
            _selectedMob = null;
            _selectedNpc = null;
            _selectedPlayer = null;
            _manualSelection = true;
            _selectionHint = "Selected " + node.ResourceId + ".";
            return;
        }

        if (mobSystem != null && mobSystem.TrySelectMobFromRay(ray, out var mob))
        {
            _selectedMob = mob;
            _selectedNode = null;
            _selectedNpc = null;
            _selectedPlayer = null;
            _manualSelection = true;
            _selectionHint = "Selected " + mob.MobTypeId + ".";
            return;
        }

        if (npcSystem != null && npcSystem.TrySelectNpcFromRay(ray, out var npc))
        {
            _selectedNpc = npc;
            _selectedNode = null;
            _selectedMob = null;
            _selectedPlayer = null;
            _manualSelection = true;
            _selectionHint = "Selected " + npc.DisplayName + ".";
            return;
        }

        if (remoteAvatarSystem != null && remoteAvatarSystem.TrySelectPlayerFromRay(ray, out var player))
        {
            _selectedPlayer = player;
            _selectedNode = null;
            _selectedMob = null;
            _selectedNpc = null;
            _manualSelection = true;
            _selectionHint = "Selected player " + player.PlayerId + ".";
            return;
        }

        _selectedNode = null;
        _selectedMob = null;
        _selectedNpc = null;
        _selectedPlayer = null;
        _manualSelection = false;
        _selectionHint = "No target under cursor.";
    }

    private void DrawActionBar()
    {
        var rect = new Rect((Screen.width * 0.5f) - 290f, Screen.height - 86f, 580f, 64f);
        GUI.Box(rect, "Actions");

        DrawActionButton(rect.x + 10f, rect.y + 28f, 68f, "Bag", () => gameplayPanel?.FocusInventory());
        DrawActionButton(rect.x + 82f, rect.y + 28f, 68f, "Craft", () => gameplayPanel?.FocusCrafting());
        DrawActionButton(rect.x + 154f, rect.y + 28f, 68f, "Skills", () => gameplayPanel?.FocusProgression());
        DrawActionButton(rect.x + 226f, rect.y + 28f, 68f, "Quests", () => gameplayPanel?.FocusQuests());
        DrawActionButton(rect.x + 298f, rect.y + 28f, 68f, "Rep", () => gameplayPanel?.FocusReputation());
        DrawActionButton(rect.x + 370f, rect.y + 28f, 68f, "Events", () => gameplayPanel?.FocusEvents());
        DrawActionButton(rect.x + 442f, rect.y + 28f, 68f, "Invade", () => gameplayPanel?.FocusInvasions());
        DrawActionButton(rect.x + 514f, rect.y + 28f, 56f, "Fight", () => gameplayPanel?.FocusCombat());
    }

    private void DrawActionButton(float x, float y, float width, string label, Action action)
    {
        if (GUI.Button(new Rect(x, y, width, 28f), label))
        {
            action();
        }
    }

    private void ClearSelection()
    {
        _selectedNode = null;
        _selectedMob = null;
        _selectedNpc = null;
        _selectedPlayer = null;
        _activeNpcDialog = null;
        _manualSelection = false;
        _selectionHint = "Selection cleared.";
    }

    private CombatantData FindCombatant(ulong characterId)
    {
        var combat = client.Combat;
        if (combat == null || combat.combatants == null)
        {
            return null;
        }

        for (var i = 0; i < combat.combatants.Length; i++)
        {
            var combatant = combat.combatants[i];
            if (combatant != null && combatant.characterId == characterId)
            {
                return combatant;
            }
        }

        return null;
    }

    private void InteractWithNpc(NpcSnapshot npc)
    {
        if (gameplayPanel != null && HasNpcAction(npc, "shop"))
        {
            gameplayPanel.PrimeShopForNpc(npc.NpcId);
        }

        if (client != null)
        {
            _ = client.RefreshNpcContextAsync(npc.NpcId);
        }

        _activeNpcDialog = npc;
        _selectionHint = "Talking to " + npc.DisplayName + ".";
    }

    private void DrawNpcDialog()
    {
        if (!_activeNpcDialog.HasValue)
        {
            return;
        }

        var npc = _activeNpcDialog.Value;
        var rect = new Rect(Screen.width - 420f, 148f, 396f, 286f);
        GUI.Box(rect, npc.DisplayName);

        var context = client != null &&
                      client.NpcContext != null &&
                      string.Equals(client.NpcContext.npcId, npc.NpcId, StringComparison.OrdinalIgnoreCase)
            ? client.NpcContext
            : null;

        var greeting = context != null && !string.IsNullOrWhiteSpace(context.greetingText)
            ? context.greetingText
            : string.IsNullOrWhiteSpace(npc.GreetingText)
            ? "The NPC watches you, waiting to see what you need."
            : npc.GreetingText;
        GUI.Label(new Rect(rect.x + 16f, rect.y + 30f, rect.width - 32f, 54f), greeting);
        var servicesText = context != null && context.services != null && context.services.Length > 0
            ? string.Join(", ", context.services)
            : FormatServices(npc);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 86f, rect.width - 32f, 20f), "Role: " + npc.PrimaryRole + "  Services: " + servicesText);
        var serviceSummary = BuildNpcServiceSummary(npc, context);
        if (!string.IsNullOrWhiteSpace(serviceSummary))
        {
            GUI.Label(new Rect(rect.x + 16f, rect.y + 104f, rect.width - 32f, 20f), serviceSummary);
        }

        var previewY = rect.y + 136f;
        if (context != null)
        {
            var previewLine = previewY;
            if (context.shopPreviewLines != null && context.shopPreviewLines.Length > 0)
            {
                GUI.Label(new Rect(rect.x + 16f, previewLine, rect.width - 32f, 18f), "Stock: " + context.shopPreviewLines[0]);
                previewLine += 18f;
            }

            if (context.questPreviewTitles != null && context.questPreviewTitles.Length > 0)
            {
                GUI.Label(new Rect(rect.x + 16f, previewLine, rect.width - 32f, 18f), "Quest: " + context.questPreviewTitles[0]);
                previewLine += 18f;
            }
        }

        var buttonY = rect.y + 188f;
        var buttonX = rect.x + 16f;
        var options = context != null && context.options != null && context.options.Length > 0
            ? context.options
            : BuildFallbackNpcOptions(npc);

        for (var i = 0; i < options.Length; i++)
        {
            var option = options[i];
            if (GUI.Button(new Rect(buttonX, buttonY, 96f, 30f), option.label))
            {
                ExecuteNpcOption(npc, option.actionId);
            }
            buttonX += 104f;
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 94f, rect.y + rect.height - 42f, 78f, 26f), "Close"))
        {
            _activeNpcDialog = null;
        }
    }

    private void OpenNpcShop(NpcSnapshot npc)
    {
        if (gameplayPanel == null)
        {
            return;
        }

        gameplayPanel.OpenShopForNpc(npc.NpcId, npc.DisplayName);
        _selectionHint = "Opened merchant services with " + npc.DisplayName + ".";
    }

    private void OpenNpcQuests(NpcSnapshot npc)
    {
        if (gameplayPanel == null)
        {
            return;
        }

        gameplayPanel.OpenQuestsForNpc(npc.NpcId, npc.DisplayName);
        _selectionHint = "Opened quest board with " + npc.DisplayName + ".";
    }

    private void OpenNpcTraining(NpcSnapshot npc)
    {
        if (gameplayPanel == null)
        {
            return;
        }

        gameplayPanel.FocusProgression();
        _selectionHint = "Viewed training with " + npc.DisplayName + ".";
    }

    private void ExecuteNpcOption(NpcSnapshot npc, string actionId)
    {
        switch ((actionId ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "shop":
                OpenNpcShop(npc);
                break;
            case "quests":
                OpenNpcQuests(npc);
                break;
            case "training":
                OpenNpcTraining(npc);
                break;
            default:
                _selectionHint = "Spoke with " + npc.DisplayName + ".";
                break;
        }
    }

    private static string FormatServices(NpcSnapshot npc)
        => npc.Services == null || npc.Services.Length == 0
            ? "none"
            : string.Join(", ", npc.Services);

    private static NpcServiceOptionData[] BuildFallbackNpcOptions(NpcSnapshot npc)
    {
        var options = new System.Collections.Generic.List<NpcServiceOptionData>();
        if (npc.ServiceOptions != null && npc.ServiceOptions.Length > 0)
        {
            for (var index = 0; index < npc.ServiceOptions.Length; index++)
            {
                var option = npc.ServiceOptions[index];
                if (string.IsNullOrWhiteSpace(option.ActionId))
                {
                    continue;
                }

                options.Add(new NpcServiceOptionData
                {
                    actionId = option.ActionId,
                    label = string.IsNullOrWhiteSpace(option.Label) ? option.ActionId : option.Label,
                    uiHint = option.UiHint ?? string.Empty
                });
            }
        }

        if (options.Count == 0 && (HasNpcService(npc, "shop") || HasNpcService(npc, "crafting") || ContainsWord(npc.PrimaryRole, "shop")))
        {
            options.Add(new NpcServiceOptionData { actionId = "shop", label = "Shop", uiHint = string.Empty });
        }

        if (options.Count == 0 && (HasNpcService(npc, "quests") || ContainsWord(npc.PrimaryRole, "quest")))
        {
            options.Add(new NpcServiceOptionData { actionId = "quests", label = "Quests", uiHint = string.Empty });
        }

        if (options.Count == 0 && (HasNpcService(npc, "training") || ContainsWord(npc.PrimaryRole, "trainer")))
        {
            options.Add(new NpcServiceOptionData { actionId = "training", label = "Training", uiHint = string.Empty });
        }

        if (options.Count == 0)
        {
            options.Add(new NpcServiceOptionData { actionId = "talk", label = "Talk", uiHint = string.Empty });
        }

        return options.ToArray();
    }

    private string BuildNpcServiceSummary(NpcSnapshot npc, NpcContextData context)
    {
        if (context != null)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (context.options != null)
            {
                for (var i = 0; i < context.options.Length; i++)
                {
                    var option = context.options[i];
                    if (!string.IsNullOrWhiteSpace(option?.uiHint))
                    {
                        parts.Add(option.uiHint);
                    }
                }
            }

            return parts.Count == 0 ? string.Empty : string.Join(" | ", parts.ToArray());
        }

        if (gameplayPanel == null)
        {
            return string.Empty;
        }

        var summaryParts = new System.Collections.Generic.List<string>();
        if (HasNpcAction(npc, "shop"))
        {
            summaryParts.Add("offers " + gameplayPanel.GetShopOfferCountForNpc(npc.NpcId));
        }

        if (HasNpcAction(npc, "quests"))
        {
            summaryParts.Add("quests " + gameplayPanel.GetQuestCountForNpc(npc.NpcId));
        }

        if (HasNpcAction(npc, "training"))
        {
            summaryParts.Add("training available");
        }

        return summaryParts.Count == 0 ? string.Empty : string.Join(" | ", summaryParts.ToArray());
    }

    private static bool HasNpcService(NpcSnapshot npc, string service)
    {
        if (HasNpcAction(npc, service))
        {
            return true;
        }

        if (npc.Services == null)
        {
            return false;
        }

        for (var i = 0; i < npc.Services.Length; i++)
        {
            if (string.Equals(npc.Services[i], service, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNpcAction(NpcSnapshot npc, string actionId)
    {
        if (npc.ServiceOptions == null)
        {
            return false;
        }

        for (var i = 0; i < npc.ServiceOptions.Length; i++)
        {
            if (string.Equals(npc.ServiceOptions[i].ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWord(string value, string fragment)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
}
