using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoClient : MonoBehaviour
{
    private static readonly TimeSpan GameplayServiceTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AccountServiceTimeout = TimeSpan.FromSeconds(5);
    [Header("Gateway")]
    [SerializeField] private string gatewayHost = "127.0.0.1";
    [SerializeField] private int gatewayTcpPort = 7000;
    [SerializeField] private string accountId = "player-local";
    [SerializeField] private string password = "changeme123";
    [SerializeField] private AccountAuthMode authMode = AccountAuthMode.Login;
    [SerializeField] private int requestedZoneId = 1;
    [SerializeField] private bool connectOnStart = false;
    [SerializeField] private UnityMmoAssetBundleService assetBundleService;

    public Guid SessionId { get; private set; }
    public ulong PlayerId { get; private set; }
    public ulong SelectedCharacterId { get; private set; }
    public int CurrentZoneId { get; private set; }
    public Vector3 AuthoritativePosition { get; private set; }
    public string StatusText { get; private set; }
    public double LastHeartbeatRttMs { get; private set; }
    public double HeartbeatJitterMs { get; private set; }
    public string AssetBundleStatus => assetBundleService != null ? assetBundleService.StatusText : string.Empty;
    public CharacterOption[] Characters { get; private set; } = Array.Empty<CharacterOption>();
    public InventorySnapshotData Inventory { get; private set; }
    public ShopCatalogData ShopCatalog { get; private set; }
    public NpcContextData NpcContext { get; private set; }
    public CraftingRecipeData[] CraftingRecipes { get; private set; } = Array.Empty<CraftingRecipeData>();
    public CharacterProgressionData Progression { get; private set; }
    public CombatSnapshotData Combat { get; private set; }
    public ReputationProfileData Reputation { get; private set; }
    public QuestBoardData QuestBoard { get; private set; }
    public WorldEventBoardData WorldEvents { get; private set; }
    public InvasionBoardData Invasions { get; private set; }
    public string AccountId
    {
        get => accountId;
        set => accountId = value ?? string.Empty;
    }

    public string Password
    {
        get => password;
        set => password = value ?? string.Empty;
    }

    public bool IsConnected => _activeConnection != null;
    public ulong CurrentCharacterId => SelectedCharacterId != 0 ? SelectedCharacterId : PlayerId;
    public event Action<WorldSnapshotMessage> SnapshotReceived;
    public event Action<GameplayResultMessage> GameplayResultReceived;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private readonly SemaphoreSlim _connectionSwapLock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<GameplayServiceResponseMessage>> _pendingGameplayServiceRequests = new ConcurrentDictionary<uint, TaskCompletionSource<GameplayServiceResponseMessage>>();
    private CancellationTokenSource _sessionCancellation;
    private ZoneConnection _activeConnection;
    private uint _inputSequence;
    private uint _gameplayCommandSequence;
    private uint _gameplayServiceRequestSequence;
    private uint _accountServiceRequestSequence;

    private async void Start()
    {
        EnsureAssetBundleService();

        StatusText = "Idle";
        if (connectOnStart)
        {
            await ConnectAsync(authMode);
        }
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.G))
        {
            _ = SendGatherCommandAsync("tree-1");
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.H))
        {
            _ = SendGatherCommandAsync("ore-1");
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.I))
        {
            _ = SendInspectInventoryAsync();
        }
    }

    private void FixedUpdate()
    {
        if (_activeConnection == null)
        {
            return;
        }

        var horizontal = 0f;
        var vertical = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            horizontal -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            horizontal += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            vertical -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            vertical += 1f;
        }

        var move = new Vector3(horizontal, 0f, vertical);
        _ = SendInputAsync(move, Time.fixedDeltaTime);
    }

    private async void OnDestroy()
    {
        await DisconnectAsync();
    }

    public Task ConnectAsync()
        => ConnectAsync(authMode);

    public Task LoginAsync()
        => ConnectAsync(AccountAuthMode.Login);

    public Task RegisterAsync()
        => ConnectAsync(AccountAuthMode.Register);

    public async Task<bool> TryRefreshCharactersAsync()
    {
        await RefreshCharactersAsync();
        return Characters.Length > 0;
    }

    public async Task<bool> TryCreateCharacterAsync(string characterName)
    {
        var beforeCount = Characters.Length;
        await CreateCharacterAsync(characterName);
        return Characters.Length > beforeCount || SelectedCharacterId != 0;
    }

    public async Task<bool> TrySelectCharacterAsync(ulong characterId)
    {
        await SelectCharacterAsync(characterId);
        return SelectedCharacterId == characterId;
    }

    public async Task<bool> TryLoginAndConnectAsync()
    {
        await LoginAsync();
        return IsConnected;
    }

    public async Task<bool> TryRegisterAndPrepareAccountAsync()
    {
        await RegisterAsync();
        if (!IsConnected)
        {
            return false;
        }

        await DisconnectAsync();
        await RefreshCharactersAsync();
        return Characters.Length > 0;
    }

    public async Task<bool> TrySelectCharacterAndLoginAsync(ulong characterId)
    {
        var selected = await TrySelectCharacterAsync(characterId);
        if (!selected)
        {
            return false;
        }

        return await TryLoginAndConnectAsync();
    }

    public async Task ConnectAsync(AccountAuthMode mode)
    {
        try
        {
            EnsureAssetBundleService();
            await DisconnectAsync();
            _sessionCancellation = new CancellationTokenSource();
            StatusText = mode == AccountAuthMode.Register
                ? "Registering account"
                : "Logging in";

            using (var gatewayClient = new TcpClient())
            {
                await gatewayClient.ConnectAsync(gatewayHost, gatewayTcpPort);
                using (var stream = gatewayClient.GetStream())
                {
                    await WireProtocol.WriteTcpMessageAsync(
                        stream,
                        new ClientHelloMessage(WireProtocol.CurrentProtocolVersion, accountId.Trim(), password, mode, requestedZoneId),
                        _sessionCancellation.Token).ConfigureAwait(false);

                    var response = await WireProtocol.ReadTcpMessageAsync(stream, _sessionCancellation.Token).ConfigureAwait(false);
                    if (response is ErrorMessage error)
                    {
                        throw new InvalidOperationException(error.Text);
                    }

                    var accepted = response as HelloAcceptedMessage;
                    if (accepted == null)
                    {
                        throw new InvalidOperationException("Gateway did not return HelloAccepted.");
                    }

                    accountId = accountId.Trim().ToLowerInvariant();
                    authMode = mode;
                    SessionId = accepted.SessionId;
                    PlayerId = accepted.PlayerId;
                    SelectedCharacterId = accepted.PlayerId;
                    await ReplaceZoneConnectionAsync(
                        accepted.ZoneHost,
                        accepted.ZoneTcpPort,
                        accepted.ZoneUdpPort,
                        accepted.TransferToken,
                        accepted.ZoneBundleName,
                        0,
                        accepted.ZoneId,
                        _sessionCancellation.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public Task DisconnectAsync()
    {
        if (_sessionCancellation != null)
        {
            _sessionCancellation.Cancel();
            _sessionCancellation.Dispose();
            _sessionCancellation = null;
        }

        var previous = Interlocked.Exchange(ref _activeConnection, null);
        if (previous != null)
        {
            previous.Dispose();
        }

        CancelPendingGameplayRequests("Disconnected.");

        StatusText = "Disconnected";
        return Task.CompletedTask;
    }

    public async Task RefreshCharactersAsync()
    {
        try
        {
            var response = await SendAccountServiceRequestAsync<EmptyRequest, AccountCharacterListResponse>(
                AccountServiceKind.CharacterList,
                EmptyRequest.Instance).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Loaded " + Characters.Length + " characters.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task CreateCharacterAsync(string characterName)
    {
        try
        {
            var response = await SendAccountServiceRequestAsync<CharacterCreateRequest, AccountCharacterListResponse>(
                AccountServiceKind.CharacterCreate,
                new CharacterCreateRequest { characterName = characterName }).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Created character " + characterName + ".";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task SelectCharacterAsync(ulong characterId)
    {
        try
        {
            var response = await SendAccountServiceRequestAsync<CharacterSelectRequest, AccountCharacterListResponse>(
                AccountServiceKind.CharacterSelect,
                new CharacterSelectRequest { characterId = characterId }).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Selected character " + characterId + ".";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshInventoryAsync()
    {
        try
        {
            Inventory = await RequestGameplayServiceAsync<EmptyRequest, InventorySnapshotData>(GameplayServiceKind.Inventory, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded inventory.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshCraftingRecipesAsync()
    {
        try
        {
            CraftingRecipes = await RequestGameplayServiceArrayAsync<EmptyRequest, CraftingRecipeData>(GameplayServiceKind.CraftingRecipes, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded " + CraftingRecipes.Length + " recipes.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshShopCatalogAsync(string npcId)
    {
        try
        {
            ShopCatalog = await RequestGameplayServiceAsync<ShopCatalogRequest, ShopCatalogData>(
                GameplayServiceKind.ShopCatalog,
                new ShopCatalogRequest { npcId = npcId }).ConfigureAwait(false);
            StatusText = "Loaded merchant stock.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshNpcContextAsync(string npcId)
    {
        try
        {
            NpcContext = await RequestGameplayServiceAsync<NpcContextRequest, NpcContextData>(
                GameplayServiceKind.NpcContext,
                new NpcContextRequest { npcId = npcId }).ConfigureAwait(false);
            StatusText = "Loaded NPC context.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task PurchaseShopOfferAsync(string offerId)
    {
        try
        {
            var result = await RequestGameplayServiceAsync<ShopPurchaseRequest, ShopPurchaseData>(
                GameplayServiceKind.ShopPurchase,
                new ShopPurchaseRequest { offerId = offerId }).ConfigureAwait(false);
            Inventory = result.inventory;
            StatusText = result.message;
            if (ShopCatalog != null && !string.IsNullOrWhiteSpace(ShopCatalog.npcId))
            {
                await RefreshShopCatalogAsync(ShopCatalog.npcId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ExecuteRecipeAsync(string recipeId)
    {
        try
        {
            var result = await RequestGameplayServiceAsync<CraftingExecuteRequest, CraftingResultData>(
                GameplayServiceKind.CraftRecipe,
                new CraftingExecuteRequest { recipeId = recipeId }).ConfigureAwait(false);
            Inventory = result.inventory;
            StatusText = !string.IsNullOrWhiteSpace(result.message)
                ? result.message
                : (result.success ? "Craft complete." : "Craft failed.");
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshProgressionAsync()
    {
        try
        {
            Progression = await RequestGameplayServiceAsync<EmptyRequest, CharacterProgressionData>(GameplayServiceKind.Progression, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded progression.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshCombatAsync()
    {
        try
        {
            Combat = await RequestGameplayServiceAsync<EmptyRequest, CombatSnapshotData>(GameplayServiceKind.CombatSnapshot, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded combat state.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task EnsureCombatAsync()
    {
        try
        {
            Combat = await RequestGameplayServiceAsync<CombatEnsureRequest, CombatSnapshotData>(
                GameplayServiceKind.CombatEnsure,
                new CombatEnsureRequest()).ConfigureAwait(false);
            StatusText = "Combat profile ready.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task AttackCharacterAsync(ulong targetCharacterId, int baseDamage = 10)
    {
        try
        {
            Combat = await RequestGameplayServiceAsync<CombatAttackRequest, CombatSnapshotData>(
                GameplayServiceKind.CombatAttack,
                new CombatAttackRequest
                {
                    targetCharacterId = targetCharacterId,
                    baseDamage = baseDamage <= 0 ? 10 : baseDamage
                }).ConfigureAwait(false);
            StatusText = "Attack resolved.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshReputationAsync()
    {
        try
        {
            Reputation = await RequestGameplayServiceAsync<EmptyRequest, ReputationProfileData>(GameplayServiceKind.Reputation, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded reputation.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshQuestBoardAsync()
    {
        try
        {
            QuestBoard = await RequestGameplayServiceAsync<EmptyRequest, QuestBoardData>(GameplayServiceKind.QuestBoard, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded quests.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ExecuteQuestActionAsync(string actionId)
    {
        try
        {
            QuestBoard = await RequestGameplayServiceAsync<QuestActionRequest, QuestBoardData>(
                GameplayServiceKind.QuestAction,
                new QuestActionRequest { actionId = actionId, amount = 1 }).ConfigureAwait(false);
            StatusText = "Quest action recorded.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ClaimQuestAsync(string questId)
    {
        try
        {
            var result = await RequestGameplayServiceAsync<QuestClaimRequest, QuestClaimData>(
                GameplayServiceKind.QuestClaim,
                new QuestClaimRequest { questId = questId }).ConfigureAwait(false);
            StatusText = result.message;
            await RefreshAllGameplayAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshWorldEventsAsync()
    {
        try
        {
            WorldEvents = await RequestGameplayServiceAsync<EmptyRequest, WorldEventBoardData>(GameplayServiceKind.WorldEvents, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded world events.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ContributeWorldEventAsync(string eventId)
    {
        try
        {
            WorldEvents = await RequestGameplayServiceAsync<WorldEventContributionRequest, WorldEventBoardData>(
                GameplayServiceKind.WorldEventContribute,
                new WorldEventContributionRequest { eventId = eventId, amount = 1 }).ConfigureAwait(false);
            StatusText = "Contributed to event.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ClaimWorldEventAsync(string eventId)
    {
        try
        {
            var result = await RequestGameplayServiceAsync<WorldEventClaimRequest, WorldEventClaimData>(
                GameplayServiceKind.WorldEventClaim,
                new WorldEventClaimRequest { eventId = eventId }).ConfigureAwait(false);
            StatusText = result.message;
            await RefreshAllGameplayAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshInvasionsAsync()
    {
        try
        {
            Invasions = await RequestGameplayServiceAsync<EmptyRequest, InvasionBoardData>(GameplayServiceKind.Invasions, EmptyRequest.Instance).ConfigureAwait(false);
            StatusText = "Loaded invasions.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task RefreshAllGameplayAsync()
    {
        try
        {
            var bundle = await RequestGameplayServiceAsync<EmptyRequest, GameplayBundleData>(
                GameplayServiceKind.FullState,
                EmptyRequest.Instance).ConfigureAwait(false);
            ApplyGameplayBundle(bundle);
            StatusText = "Loaded gameplay state.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public Task ExecuteGatherAsync(string nodeId)
        => SendGatherCommandAsync(nodeId);

    public Task ExecuteMobAttackAsync(string mobId)
        => SendAttackCommandAsync(mobId);

    public Task CastFireballAsync(string mobId)
        => SendCastSpellCommandAsync("fireball:" + mobId);

    public Task InspectInventoryAsync()
        => SendInspectInventoryAsync();

    public async Task RecordInvasionKillAsync(string invasionId)
    {
        try
        {
            Invasions = await RequestGameplayServiceAsync<InvasionRecordKillRequest, InvasionBoardData>(
                GameplayServiceKind.InvasionRecordKill,
                new InvasionRecordKillRequest { invasionId = invasionId, kills = 1 }).ConfigureAwait(false);
            StatusText = "Recorded invasion kill.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task ClaimInvasionAsync(string invasionId)
    {
        try
        {
            var result = await RequestGameplayServiceAsync<InvasionClaimRequest, InvasionClaimData>(
                GameplayServiceKind.InvasionClaim,
                new InvasionClaimRequest { invasionId = invasionId }).ConfigureAwait(false);
            StatusText = result.message;
            await RefreshAllGameplayAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    private async Task SendInputAsync(Vector3 move, float deltaTime)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        var payload = WireProtocol.SerializeUdpMessage(
            new ClientInputMessage(SessionId, ++_inputSequence, ToNetworkVector(move.normalized), deltaTime));

        try
        {
            await connection.UdpClient.SendAsync(payload, payload.Length, connection.ZoneHost, connection.ZoneUdpPort).ConfigureAwait(false);
            if (_inputSequence % 20 == 0)
            {
                Debug.Log("Sent input seq " + _inputSequence + " to zone " + connection.ZoneUdpPort + ".");
            }
        }
        catch
        {
        }
    }

    private async Task ReplaceZoneConnectionAsync(string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, string zoneBundleName, int previousZoneId, int newZoneId, CancellationToken cancellationToken)
    {
        await _connectionSwapLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessionToken = _sessionCancellation != null ? _sessionCancellation.Token : cancellationToken;
            var next = await CreateZoneConnectionAsync(zoneHost, zoneTcpPort, zoneUdpPort, transferToken, sessionToken).ConfigureAwait(false);
            var prior = Interlocked.Exchange(ref _activeConnection, next);
            StartConnectionLoops(next);
            if (prior != null)
            {
                CancelPendingGameplayRequests("Zone connection replaced.");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000, sessionToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        prior.Dispose();
                    }
                }, CancellationToken.None);
            }

            CurrentZoneId = newZoneId;
            AuthoritativePosition = next.SpawnPosition;
            StatusText = "Connected to zone " + newZoneId + " (G tree / H ore / I inventory)";
            EnsureAssetBundleService();
            if (assetBundleService != null)
            {
                var zoneIdToLoad = newZoneId;
                var bundleNameToLoad = zoneBundleName;
                Debug.Log("Queueing zone bundle load for zone " + zoneIdToLoad + " bundle '" + (string.IsNullOrWhiteSpace(bundleNameToLoad) ? "<empty>" : bundleNameToLoad) + "'.");
                _mainThreadActions.Enqueue(() => assetBundleService.BeginEnsureZoneBundle(zoneIdToLoad, bundleNameToLoad));
            }
            else
            {
                Debug.LogWarning("UnityMmoAssetBundleService was not found when attaching to zone " + newZoneId + ".");
            }

            if (previousZoneId != 0 && previousZoneId != newZoneId)
            {
                Debug.Log("Transferred from zone " + previousZoneId + " to zone " + newZoneId + ".");
            }
        }
        finally
        {
            _connectionSwapLock.Release();
        }
    }

    private async Task<ZoneConnection> CreateZoneConnectionAsync(string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(zoneHost, zoneTcpPort);
        var stream = tcpClient.GetStream();

        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new AttachToZoneMessage(SessionId, PlayerId, transferToken),
            cancellationToken).ConfigureAwait(false);

        var response = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
        if (response is ErrorMessage error)
        {
            throw new InvalidOperationException(error.Text);
        }

        var attached = response as AttachAcceptedMessage;
        if (attached == null)
        {
            throw new InvalidOperationException("Zone did not return AttachAccepted.");
        }

        Debug.Log("TCP attached to zone " + attached.ZoneId + " at " + zoneHost + ":" + zoneUdpPort + ".");

        var udpClient = new UdpClient(0);
        var probePayload = WireProtocol.SerializeUdpMessage(new TransferProbeMessage(SessionId, transferToken));
        _ = SendTransferProbeAsync(udpClient, zoneHost, zoneUdpPort, probePayload);

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new ZoneConnection(tcpClient, stream, udpClient, linkedCancellation, ToUnityVector(attached.SpawnPosition), probePayload, zoneHost, zoneUdpPort);
    }

    private async Task SendTransferProbeAsync(UdpClient udpClient, string zoneHost, int zoneUdpPort, byte[] probePayload)
    {
        try
        {
            await udpClient.SendAsync(probePayload, probePayload.Length, zoneHost, zoneUdpPort).ConfigureAwait(false);
            Debug.Log("Sent transfer probe to " + zoneHost + ":" + zoneUdpPort + ".");
        }
        catch
        {
        }
    }

    private void StartConnectionLoops(ZoneConnection connection)
    {
        _ = Task.Run(() => ControlLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => UdpLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => ProbeLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => HeartbeatLoopAsync(connection), connection.Cancellation.Token);
    }

    private async Task SendGatherCommandAsync(string targetId)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.Gather, targetId);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Gathering from " + targetId + "...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task SendInspectInventoryAsync()
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.InspectInventory, string.Empty);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Requesting inventory...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task ControlLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var message = await WireProtocol.ReadTcpMessageAsync(connection.Stream, connection.Cancellation.Token).ConfigureAwait(false);
                if (message is ZoneTransferPrepareMessage prepare && prepare.SessionId == SessionId)
                {
                    _mainThreadActions.Enqueue(() => StatusText = "Transferring to zone " + prepare.ToZoneId);
                    await ReplaceZoneConnectionAsync(
                        prepare.ZoneHost,
                        prepare.ZoneTcpPort,
                        prepare.ZoneUdpPort,
                        prepare.TransferToken,
                        prepare.ZoneBundleName,
                        prepare.FromZoneId,
                        prepare.ToZoneId,
                        _sessionCancellation != null ? _sessionCancellation.Token : connection.Cancellation.Token).ConfigureAwait(false);
                    return;
                }

                if (message is HeartbeatMessage heartbeat)
                {
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var rttMs = Math.Max(0, nowMs - heartbeat.ServerTicks);
                    var previousRtt = LastHeartbeatRttMs;
                    _mainThreadActions.Enqueue(() =>
                    {
                        LastHeartbeatRttMs = rttMs;
                        if (previousRtt > 0)
                        {
                            HeartbeatJitterMs = Math.Abs(rttMs - previousRtt);
                        }
                    });
                    continue;
                }

                if (message is ErrorMessage error)
                {
                    _mainThreadActions.Enqueue(() => StatusText = error.Text);
                    continue;
                }

                if (message is DisconnectNoticeMessage disconnect)
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        StatusText = disconnect.Reason + (disconnect.CanReconnect ? " Reconnect grace: " + disconnect.GraceSeconds + "s." : string.Empty);
                    });
                    continue;
                }

                var gameplayServiceResponse = message as GameplayServiceResponseMessage;
                if (gameplayServiceResponse != null)
                {
                    if (_pendingGameplayServiceRequests.TryRemove(gameplayServiceResponse.RequestId, out var pendingRequest))
                    {
                        pendingRequest.TrySetResult(gameplayServiceResponse);
                    }
                    continue;
                }

                var gameplayStatePush = message as GameplayStatePushMessage;
                if (gameplayStatePush != null && gameplayStatePush.SessionId == SessionId)
                {
                    try
                    {
                        var bundle = JsonUtility.FromJson<GameplayBundleData>(gameplayStatePush.PayloadJson);
                        _mainThreadActions.Enqueue(() => ApplyGameplayBundle(bundle));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    continue;
                }

                var gameplayResult = message as GameplayResultMessage;
                if (gameplayResult != null)
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        StatusText = gameplayResult.Text + " | " + gameplayResult.ItemId + ": " + gameplayResult.ItemCount + " | Gathering " + gameplayResult.SkillValue;
                        GameplayResultReceived?.Invoke(gameplayResult);
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            CancelPendingGameplayRequests("Control connection canceled.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            CancelPendingGameplayRequests("Control connection error.");
            _mainThreadActions.Enqueue(() => StatusText = "Control connection error");
        }
    }

    private async Task UdpLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var result = await connection.UdpClient.ReceiveAsync().ConfigureAwait(false);
                var message = WireProtocol.DeserializeUdpMessage(result.Buffer);
                connection.HasSeenUdpTraffic = true;
                if (message is TransferReadyMessage ready)
                {
                    Debug.Log("Received TransferReady for zone " + ready.ZoneId + ".");
                }

                if (message is WorldSnapshotMessage snapshot && snapshot.ZoneId == CurrentZoneId)
                {
                    if (snapshot.Tick % 20 == 0)
                    {
                        Debug.Log("Received snapshot tick " + snapshot.Tick + " for zone " + snapshot.ZoneId + " with " + snapshot.ResourceNodes.Length + " resource nodes.");
                    }
                    for (var i = 0; i < snapshot.Players.Length; i++)
                    {
                        var player = snapshot.Players[i];
                        if (player.PlayerId == PlayerId)
                        {
                            var nextPosition = ToUnityVector(player.Position);
                            _mainThreadActions.Enqueue(() => AuthoritativePosition = nextPosition);
                            break;
                        }
                    }

                    var deliveredSnapshot = snapshot;
                    _mainThreadActions.Enqueue(() =>
                    {
                        var handlers = SnapshotReceived;
                        if (handlers != null)
                        {
                            var invocationList = handlers.GetInvocationList();
                            for (var i = 0; i < invocationList.Length; i++)
                            {
                                try
                                {
                                    ((Action<WorldSnapshotMessage>)invocationList[i])(deliveredSnapshot);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogException(ex);
                                }
                            }
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _mainThreadActions.Enqueue(() => StatusText = "UDP connection error");
        }
    }

    private async Task ProbeLoopAsync(ZoneConnection connection)
    {
        try
        {
            for (var attempt = 0; attempt < 15 && !connection.Cancellation.IsCancellationRequested; attempt++)
            {
                if (connection.HasSeenUdpTraffic)
                {
                    return;
                }

                await SendTransferProbeAsync(connection.UdpClient, connection.ZoneHost, connection.ZoneUdpPort, connection.ProbePayload).ConfigureAwait(false);
                await Task.Delay(200, connection.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static NetworkVector3 ToNetworkVector(Vector3 vector)
        => new NetworkVector3(vector.x, vector.y, vector.z);

    private static Vector3 ToUnityVector(NetworkVector3 vector)
        => new Vector3(vector.X, vector.Y, vector.Z);

    private void ApplyCharacterResponse(AccountCharacterListResponse response)
    {
        SelectedCharacterId = response.selectedCharacterId;
        Characters = response.characters ?? Array.Empty<CharacterOption>();
    }

    private void ApplyGameplayBundle(GameplayBundleData bundle)
    {
        if (bundle == null)
        {
            return;
        }

        Inventory = bundle.inventory;
        CraftingRecipes = bundle.craftingRecipes ?? Array.Empty<CraftingRecipeData>();
        Progression = bundle.progression;
        Combat = bundle.combat;
        Reputation = bundle.reputation;
        QuestBoard = bundle.questBoard;
        WorldEvents = bundle.worldEvents;
        Invasions = bundle.invasions;
    }

    private void EnsureAssetBundleService()
    {
        if (assetBundleService == null)
        {
            assetBundleService = FindObjectOfType<UnityMmoAssetBundleService>();
        }
    }

    private void CancelPendingGameplayRequests(string reason)
    {
        foreach (var pair in _pendingGameplayServiceRequests.ToArray())
        {
            if (_pendingGameplayServiceRequests.TryRemove(pair.Key, out var pending))
            {
                pending.TrySetException(new InvalidOperationException(reason));
            }
        }
    }

    private async Task<TResponse> RequestGameplayServiceAsync<TRequest, TResponse>(GameplayServiceKind serviceKind, TRequest payload)
        where TResponse : class
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            throw new InvalidOperationException("Not connected to a zone.");
        }

        var requestId = ++_gameplayServiceRequestSequence;
        var pendingRequest = new TaskCompletionSource<GameplayServiceResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingGameplayServiceRequests.TryAdd(requestId, pendingRequest))
        {
            throw new InvalidOperationException("Failed to allocate gameplay service request.");
        }

        try
        {
            await connection.SendControlMessageAsync(
                new GameplayServiceRequestMessage(
                    SessionId,
                    requestId,
                    serviceKind,
                    payload != null ? JsonUtility.ToJson(payload) : "{}")).ConfigureAwait(false);

            var completedTask = await Task.WhenAny(
                pendingRequest.Task,
                Task.Delay(GameplayServiceTimeout, connection.Cancellation.Token)).ConfigureAwait(false);
            if (completedTask != pendingRequest.Task)
            {
                throw new TimeoutException("Gameplay request timed out.");
            }

            var response = await pendingRequest.Task.ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorText) ? "Gameplay request failed." : response.ErrorText);
            }

            var parsed = JsonUtility.FromJson<TResponse>(response.PayloadJson);
            if (parsed == null)
            {
                throw new InvalidOperationException("Gameplay service returned an empty response.");
            }

            return parsed;
        }
        finally
        {
            _pendingGameplayServiceRequests.TryRemove(requestId, out _);
        }
    }

    private async Task<T[]> RequestGameplayServiceArrayAsync<TRequest, T>(GameplayServiceKind serviceKind, TRequest payload)
    {
        var wrapper = await RequestGameplayServiceAsync<TRequest, ArrayWrapper<T>>(serviceKind, payload).ConfigureAwait(false);
        return wrapper != null && wrapper.items != null ? wrapper.items : Array.Empty<T>();
    }

    private async Task<TResponse> SendAccountServiceRequestAsync<TRequest, TResponse>(AccountServiceKind serviceKind, TRequest payload)
        where TResponse : class
    {
        using (var gatewayClient = new TcpClient())
        {
            var connectTask = gatewayClient.ConnectAsync(gatewayHost, gatewayTcpPort);
            var completedConnect = await Task.WhenAny(connectTask, Task.Delay(AccountServiceTimeout)).ConfigureAwait(false);
            if (completedConnect != connectTask)
            {
                throw new TimeoutException("Gateway account request timed out.");
            }

            using (var stream = gatewayClient.GetStream())
            {
                using var timeout = new CancellationTokenSource(AccountServiceTimeout);
                var request = new AccountServiceRequestMessage(
                    ++_accountServiceRequestSequence,
                    serviceKind,
                    accountId.Trim(),
                    password,
                    payload != null ? JsonUtility.ToJson(payload) : "{}");
                await WireProtocol.WriteTcpMessageAsync(stream, request, timeout.Token).ConfigureAwait(false);

                var response = await WireProtocol.ReadTcpMessageAsync(stream, timeout.Token).ConfigureAwait(false);
                if (response is ErrorMessage error)
                {
                    throw new InvalidOperationException(error.Text);
                }

                if (response is not AccountServiceResponseMessage accountServiceResponse)
                {
                    throw new InvalidOperationException("Unexpected gateway account response.");
                }

                if (!accountServiceResponse.Success)
                {
                    throw new InvalidOperationException(accountServiceResponse.ErrorText);
                }

                var parsed = JsonUtility.FromJson<TResponse>(accountServiceResponse.PayloadJson);
                if (parsed == null)
                {
                    throw new InvalidOperationException("Gateway account service returned an empty response.");
                }

                return parsed;
            }
        }
    }

    private async Task SendAttackCommandAsync(string targetId)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.Attack, targetId);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Attacking " + targetId + "...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task SendCastSpellCommandAsync(string targetId)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.CastSpell, targetId);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Casting " + targetId + "...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task HeartbeatLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), connection.Cancellation.Token).ConfigureAwait(false);
                if (connection.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                await connection.SendControlMessageAsync(new HeartbeatMessage(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private sealed class ZoneConnection : IDisposable
    {
        public ZoneConnection(TcpClient tcpClient, NetworkStream stream, UdpClient udpClient, CancellationTokenSource cancellation, Vector3 spawnPosition, byte[] probePayload, string zoneHost, int zoneUdpPort)
        {
            TcpClient = tcpClient;
            Stream = stream;
            UdpClient = udpClient;
            Cancellation = cancellation;
            SpawnPosition = spawnPosition;
            ProbePayload = probePayload;
            ZoneHost = zoneHost;
            ZoneUdpPort = zoneUdpPort;
        }

        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }
        public UdpClient UdpClient { get; }
        public CancellationTokenSource Cancellation { get; }
        public Vector3 SpawnPosition { get; }
        public byte[] ProbePayload { get; }
        public string ZoneHost { get; }
        public int ZoneUdpPort { get; }
        public bool HasSeenUdpTraffic { get; set; }

        public async Task SendControlMessageAsync(TcpMessage message)
        {
            await _controlWriteLock.WaitAsync(Cancellation.Token).ConfigureAwait(false);
            try
            {
                await WireProtocol.WriteTcpMessageAsync(Stream, message, Cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _controlWriteLock.Release();
            }
        }

        public void Dispose()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
            UdpClient.Dispose();
            Stream.Dispose();
            TcpClient.Dispose();
        }

        private readonly SemaphoreSlim _controlWriteLock = new SemaphoreSlim(1, 1);
    }

    [Serializable]
    private sealed class CharacterCreateRequest
    {
        public string characterName;
    }

    [Serializable]
    private sealed class CharacterSelectRequest
    {
        public ulong characterId;
    }

    [Serializable]
    private sealed class EmptyRequest
    {
        public static readonly EmptyRequest Instance = new EmptyRequest();
    }

    [Serializable]
    public sealed class CharacterOption
    {
        public ulong characterId;
        public string characterName;
        public string createdAtUtc;
        public string lastSelectedAtUtc;
    }

    [Serializable]
    private sealed class AccountCharacterListResponse
    {
        public string accountId;
        public string accountName;
        public ulong selectedCharacterId;
        public CharacterOption[] characters;
    }

    [Serializable]
    private sealed class CraftingExecuteRequest
    {
        public ulong characterId;
        public string recipeId;
        public int outputMaxStack = 200;
        public int capacity = 40;
    }

    [Serializable]
    private sealed class ShopCatalogRequest
    {
        public string npcId;
    }

    [Serializable]
    private sealed class ShopPurchaseRequest
    {
        public string offerId;
        public int outputMaxStack = 200;
        public int capacity = 40;
    }

    [Serializable]
    private sealed class NpcContextRequest
    {
        public string npcId;
    }

    [Serializable]
    private sealed class CombatEnsureRequest
    {
        public ulong characterId;
    }

    [Serializable]
    private sealed class CombatAttackRequest
    {
        public ulong attackerCharacterId;
        public ulong targetCharacterId;
        public int baseDamage;
    }

    [Serializable]
    private sealed class QuestActionRequest
    {
        public ulong characterId;
        public string actionId;
        public int amount;
    }

    [Serializable]
    private sealed class QuestClaimRequest
    {
        public ulong characterId;
        public string questId;
    }

    [Serializable]
    private sealed class WorldEventContributionRequest
    {
        public string eventId;
        public ulong characterId;
        public int amount;
    }

    [Serializable]
    private sealed class WorldEventClaimRequest
    {
        public string eventId;
        public ulong characterId;
    }

    [Serializable]
    private sealed class InvasionRecordKillRequest
    {
        public string invasionId;
        public ulong characterId;
        public int kills;
    }

    [Serializable]
    private sealed class InvasionClaimRequest
    {
        public string invasionId;
        public ulong characterId;
    }

    [Serializable]
    private sealed class MessageEnvelope
    {
        public string message;
    }
}
}
