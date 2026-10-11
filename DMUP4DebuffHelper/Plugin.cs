using DMUP4DebuffHelper.Windows;
using Dalamud.Game.DutyState;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using P3 = DMUP3BlackholeHelper;

namespace DMUP4DebuffHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string ConfigCommandName = "/dmu";
    private const string ShortConfigCommandName = "/dmuh";
    private const string ConfigCommandAliasName = "/dmuhelper";
    private const uint DmuTerritoryId = 1363;
    private const uint BossTellStatusId = 2056;
    private const uint CursedShriekStatusId = 5543;
    private const int QueuedChatDelayMs = 200;
    private const float StatusTimerAnchorRefreshThreshold = 0.05f;
    private static readonly TimeSpan BossTellFreshness = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FloodWoundMemory = TimeSpan.FromSeconds(90);

    internal static readonly IReadOnlyDictionary<uint, WatchedStatus> WatchedStatuses =
        new Dictionary<uint, WatchedStatus>
        {
            [5545] = new(5545, "Compressed Water", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 10),
            [5544] = new(5544, "Forked Lightning", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 20),
            [5543] = new(5543, "Cursed Shriek", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 30),
            [5546] = new(5546, "Acceleration Bomb", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 40),
            [5548] = new(5548, "Dynamic Fluid", P4Boss.Chaos, P4MechanicGroup.Chaos, 50),
            [5547] = new(5547, "Entropy", P4Boss.Chaos, P4MechanicGroup.Chaos, 60),
            [P4Flood.WhiteWound1StatusId] = new(P4Flood.WhiteWound1StatusId, "White Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 70),
            [P4Flood.WhiteWound2StatusId] = new(P4Flood.WhiteWound2StatusId, "White Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 71),
            [P4Flood.BlackWound1StatusId] = new(P4Flood.BlackWound1StatusId, "Black Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 80),
            [P4Flood.BlackWound2StatusId] = new(P4Flood.BlackWound2StatusId, "Black Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 81),
            [P4Flood.AllaganFieldStatusId] = new(P4Flood.AllaganFieldStatusId, "Allagan Field", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 90),
            [P4Flood.BeyondDeath1StatusId] = new(P4Flood.BeyondDeath1StatusId, "Beyond Death", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 100),
            [P4Flood.BeyondDeath2StatusId] = new(P4Flood.BeyondDeath2StatusId, "Beyond Death", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 101),
        };

    private static readonly IReadOnlyDictionary<ushort, RealityState> BossTellRealities =
        new Dictionary<ushort, RealityState>
        {
            [1119] = RealityState.Fake,
            [1120] = RealityState.Real,
            [1121] = RealityState.Fake,
            [1122] = RealityState.Real,
        };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DMUHelper");
    private readonly ConfigWindow configWindow;
    private readonly HelperWindow helperWindow;
    private readonly P3BlackHoleTracker p3BlackHoleTracker;
    private readonly P3LimitCutTracker p3LimitCutTracker;
    private readonly GameEventHooks gameEventHooks;
    private readonly DiscoveryLog discoveryLog;
    private readonly P1Tracker p1Tracker;
    private readonly List<PartyStatusEntry> currentEntries = [];
    private readonly List<PartyMemberSnapshot> currentMembers = [];
    private readonly List<P4DebuffAssignment> currentAssignments = [];
    private readonly List<P4DebuffAssignment> localAssignments = [];
    private readonly List<P4DebuffAssignment> currentGazeAssignments = [];
    private readonly List<P4DebuffAssignment> currentPartyCallAssignments = [];
    private readonly List<BossTellSnapshot> currentBossTells = [];
    private readonly List<BossTellSnapshot> currentPullBossTells = [];
    private readonly List<P4DebuffRecord> currentPullDebuffRecords = [];
    private readonly List<P4PullSnapshot> pullSnapshots = [];
    private readonly Dictionary<uint, string> statusNameCache = new();
    private readonly Dictionary<uint, uint> statusIconCache = new();
    private readonly Dictionary<string, BossTellSnapshot> latestBossTells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedDebuffState> capturedDebuffStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedFloodWoundState> capturedFloodWounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatusTimerAnchor> statusTimerAnchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> activeDebuffRecordIndexes = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeDebuffKeysLastFrame = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeBossTellKeysLastFrame = new(StringComparer.Ordinal);
    private readonly Dictionary<P4Boss, string> lastLoggedTellParams = new();
    private readonly HashSet<string> registeredCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> queuedPartyChatMessages = [];
    private Hook<ActionEffectHandler.Delegates.Receive>? actionEffectHook;
    private DateTime? pullStartedAtUtc;
    private DateTime nextQueuedPartyChatMessageAtUtc = DateTime.MinValue;
    private float lastKnownPullElapsedSeconds;
    private bool p4SeenThisPull;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyStatusEntry> CurrentEntries => currentEntries;

    public IReadOnlyList<P4DebuffAssignment> CurrentAssignments => currentAssignments;

    public IReadOnlyList<P4DebuffAssignment> LocalAssignments => localAssignments;

    public IReadOnlyList<P4DebuffAssignment> CurrentGazeAssignments => currentGazeAssignments;

    public IReadOnlyList<P4DebuffAssignment> CurrentPartyCallAssignments => currentPartyCallAssignments;

    public IReadOnlyList<BossTellSnapshot> CurrentBossTells => currentBossTells;

    public IReadOnlyList<P4DebuffRecord> CurrentPullDebuffRecords => currentPullDebuffRecords;

    public IReadOnlyList<BossTellSnapshot> CurrentPullBossTells => currentPullBossTells;

    public IReadOnlyList<P4PullSnapshot> PullSnapshots => pullSnapshots;

    public P3BlackHoleTracker P3BlackHole => p3BlackHoleTracker;

    public P3LimitCutTracker P3LimitCut => p3LimitCutTracker;

    public P1Tracker P1 => p1Tracker;

    public P4CallSetHistory CallSetHistory { get; } = new();

    public DmuHelperDisplayMode DisplayMode { get; private set; } = DmuHelperDisplayMode.Empty;

    public float CurrentPullElapsedSeconds => pullStartedAtUtc is not null
        ? (float)(DateTime.UtcNow - pullStartedAtUtc.Value).TotalSeconds
        : lastKnownPullElapsedSeconds;

    public bool IsInDmu { get; private set; }

    private bool ShouldShowHelperWindow => ShouldShowHelperWindowInTerritory(ClientState.TerritoryType);

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        MigrateConfiguration();
        Configuration.SelectedBlackHoleStrategy = DMUP3BlackholeHelper.BlackHoleStrategy.Normalize(Configuration.SelectedBlackHoleStrategy);
        IsInDmu = ClientState.IsLoggedIn && ClientState.TerritoryType == DmuTerritoryId;

        p3BlackHoleTracker = new P3BlackHoleTracker(this);
        p3LimitCutTracker = new P3LimitCutTracker();
        gameEventHooks = new GameEventHooks();
        discoveryLog = new DiscoveryLog(this, gameEventHooks);
        p1Tracker = new P1Tracker(this, gameEventHooks);
        configWindow = new ConfigWindow(this);
        helperWindow = new HelperWindow(this)
        {
            IsOpen = ShouldShowHelperWindow,
        };

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(helperWindow);

        AddConfigCommand(ConfigCommandName);
        AddConfigCommand(ShortConfigCommandName);
        AddConfigCommand(ConfigCommandAliasName);

        unsafe
        {
            actionEffectHook = GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                ActionEffectHandler.MemberFunctionPointers.Receive,
                OnReceiveActionEffect);
            actionEffectHook.Enable();
        }

        Framework.Update += OnFrameworkUpdate;
        ClientState.Login += OnClientLogin;
        ClientState.Logout += OnClientLogout;
        ClientState.TerritoryChanged += OnTerritoryChanged;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleHelperUi;
        DutyState.DutyStarted += OnDutyReset;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
    }

    public void Dispose()
    {
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyReset;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleHelperUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Logout -= OnClientLogout;
        ClientState.Login -= OnClientLogin;
        Framework.Update -= OnFrameworkUpdate;
        RemoveCommands();
        actionEffectHook?.Dispose();
        p1Tracker.Dispose();
        discoveryLog.Dispose();
        gameEventHooks.Dispose();

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        helperWindow.Dispose();
    }

    private void AddConfigCommand(string command)
    {
        try
        {
            CommandManager.AddHandler(command, new CommandInfo(OnConfigCommand)
            {
                HelpMessage = "Open the DMU Helper settings window.",
            });
            registeredCommands.Add(command);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not register DMU Helper command {Command}.", command);
        }
    }

    private void RemoveCommands()
    {
        foreach (var command in registeredCommands.ToList())
        {
            try
            {
                CommandManager.RemoveHandler(command);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not remove DMU Helper command {Command}.", command);
            }
            finally
            {
                registeredCommands.Remove(command);
            }
        }
    }

    public void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public void OpenConfigUi()
    {
        configWindow.IsOpen = true;
    }

    public void ToggleHelperUi()
    {
        if (helperWindow.IsOpen)
        {
            helperWindow.IsOpen = false;
            return;
        }

        RefreshHelperWindowVisibility();
    }

    public void OpenHelperUi()
    {
        RefreshHelperWindowVisibility();
    }

    public void SetOnlyShowInInstance(bool enabled)
    {
        Configuration.OnlyShowInInstance = enabled;
        RefreshHelperWindowVisibility();
        SaveConfiguration();
    }

    public void SetPreviewWhenInactive(bool enabled)
    {
        Configuration.PreviewWhenInactive = enabled;
        UpdateDisplayMode();
        RefreshHelperWindowVisibility();
        SaveConfiguration();
    }

    public void SetHelperCollapsed(bool collapsed)
    {
        Configuration.HelperCollapsed = collapsed;
        SaveConfiguration();
    }

    public void SetHelperFontScale(float scale)
    {
        Configuration.HelperFontScale = Math.Clamp(scale, 0.75f, 2.0f);
        SaveConfiguration();
    }

    public void SetHelperIconScale(float scale)
    {
        Configuration.HelperIconScale = Math.Clamp(scale, 0.75f, 3.0f);
        SaveConfiguration();
    }

    public void SetHelperBackgroundOpacity(float opacity)
    {
        Configuration.HelperBackgroundOpacity = Math.Clamp(opacity, 0.15f, 1.0f);
        SaveConfiguration();
    }

    public void SetSelectedBlackHoleStrategy(DMUP3BlackholeHelper.BlackHoleStrategyKind strategy)
    {
        Configuration.SelectedBlackHoleStrategy = DMUP3BlackholeHelper.BlackHoleStrategy.Normalize(strategy);
        SaveConfiguration();
    }

    public void PostP3AssignmentOrderToChat(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments)
    {
        foreach (var line in BuildP3AssignmentOrderLines(assignments))
        {
            queuedPartyChatMessages.Enqueue(line);
        }
    }

    public void SaveConfiguration()
    {
        Configuration.Save();
    }

    private void MigrateConfiguration()
    {
        if (Configuration.Version >= 4)
        {
            return;
        }

        if (!Configuration.ShowHelper)
        {
            Configuration.OnlyShowInInstance = true;
            Configuration.PreviewWhenInactive = false;
        }

        Configuration.Version = 4;
        SaveConfiguration();
    }

    private void OnConfigCommand(string command, string args)
    {
        OpenConfigUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        FlushQueuedPartyChatMessages(DateTime.UtcNow);
        RefreshStatusSnapshot();
        if (IsInDmu && Configuration.DiscoveryLogging)
        {
            discoveryLog.Update();
        }
    }

    private void OnClientLogin()
    {
        IsInDmu = ClientState.TerritoryType == DmuTerritoryId;
        RefreshHelperWindowVisibility();
    }

    private void OnClientLogout(int type, int code)
    {
        helperWindow.IsOpen = false;
        IsInDmu = false;
        ResetHelperState("Logged out");
    }

    private void OnTerritoryChanged(uint territory)
    {
        IsInDmu = ClientState.IsLoggedIn && territory == DmuTerritoryId;
        if (!IsInDmu)
        {
            ResetHelperState("Left DMU");
        }

        helperWindow.IsOpen = ShouldShowHelperWindowInTerritory(territory);
    }

    private void RefreshHelperWindowVisibility()
    {
        helperWindow.IsOpen = ShouldShowHelperWindow;
    }

    private bool ShouldShowHelperWindowInTerritory(uint territory)
    {
        return ClientState.IsLoggedIn &&
            (Configuration.PreviewWhenInactive ||
                !Configuration.OnlyShowInInstance ||
                territory == DmuTerritoryId);
    }

    private void FlushQueuedPartyChatMessages(DateTime now)
    {
        if (queuedPartyChatMessages.Count == 0 || nextQueuedPartyChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedPartyChatMessages.Dequeue();
        SendPartyChatMessage(nextMessage);
        nextQueuedPartyChatMessageAtUtc = now.AddMilliseconds(QueuedChatDelayMs);
    }

    private unsafe void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        if (!p4SeenThisPull && ClientState.TerritoryType == DmuTerritoryId)
        {
            p3LimitCutTracker.ProcessActionEffect(casterEntityId, header);
            p3BlackHoleTracker.ProcessActionEffect(casterEntityId, header, targetEntityIds);
            if (p3BlackHoleTracker.HasLiveSignal)
            {
                p3LimitCutTracker.Reset();
            }
        }

        if (header != null && ClientState.TerritoryType == DmuTerritoryId)
        {
            p1Tracker.OnActionEffect(header->ActionId);
        }

        if (Configuration.DiscoveryLogging && header != null && ClientState.TerritoryType == DmuTerritoryId)
        {
            var targetIds = new List<uint>(header->NumTargets);
            for (var i = 0; i < header->NumTargets; i++)
            {
                targetIds.Add((uint)targetEntityIds[i].ObjectId);
            }

            discoveryLog.LogActionEffect(casterEntityId, header->ActionId, targetIds);
        }

        actionEffectHook?.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private void RefreshStatusSnapshot()
    {
        IsInDmu = ClientState.IsLoggedIn && ClientState.TerritoryType == DmuTerritoryId;
        if (!IsInDmu)
        {
            ResetHelperState("Left DMU");
            return;
        }

        var nextEntries = new List<PartyStatusEntry>();
        var nextMembers = new List<PartyMemberSnapshot>();
        var now = DateTime.UtcNow;
        var localContentId = PlayerState.ContentId;
        var localEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var partyIndex = 0;
        foreach (var member in PartyList)
        {
            var memberName = member.Name.TextValue;
            var memberKey = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : $"{memberName}:{partyIndex}";
            nextMembers.Add(new PartyMemberSnapshot(
                memberKey,
                memberName,
                partyIndex,
                member.ContentId,
                member.EntityId));

            var isLocalMember =
                (localContentId != 0 && member.ContentId == localContentId)
                || (localEntityId != 0 && member.EntityId == localEntityId);

            foreach (var status in member.Statuses)
            {
                if (status.StatusId == 0)
                {
                    continue;
                }

                var isWatched = WatchedStatuses.TryGetValue(status.StatusId, out var watchedStatus);
                if (!isWatched)
                {
                    continue;
                }

                if (!isLocalMember && status.StatusId != CursedShriekStatusId && !P4Calls.PartyStatusIds.Contains(status.StatusId))
                {
                    continue;
                }

                var entryKey = $"{memberKey}:{status.StatusId}";
                nextEntries.Add(new PartyStatusEntry(
                    entryKey,
                    memberKey,
                    memberName,
                    partyIndex,
                    status.StatusId,
                    watchedStatus!.Name,
                    GetStatusIconId(status.StatusId),
                    GetSmoothedStatusRemainingTime(entryKey, status.RemainingTime, now),
                    true,
                    watchedStatus!.SortOrder,
                    now,
                    member.ClassJob.RowId));
            }

            partyIndex++;
        }

        currentMembers.Clear();
        currentMembers.AddRange(nextMembers.OrderBy(member => member.PartyIndex));

        currentEntries.Clear();
        currentEntries.AddRange(nextEntries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.PartyIndex)
            .ThenBy(entry => entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.StatusId));
        PruneStatusTimerAnchors(currentEntries);
        CaptureActiveFloodWounds(currentEntries, now);
        PruneCapturedFloodWounds(now);

        RefreshBossTellSnapshots();
        RefreshAssignments();
        RefreshPartyGazeAssignments();
        RefreshPartyCallAssignments();
        RefreshLocalAssignments();
        UpdatePullTracking();
        var p4LiveSignal = HasP4LiveSignal();
        if (p4LiveSignal && !p4SeenThisPull)
        {
            p4SeenThisPull = true;
            p3BlackHoleTracker.ClearLiveDisplay("P4 detected");
            p3LimitCutTracker.Reset();
        }

        if (!p4SeenThisPull)
        {
            p3BlackHoleTracker.Refresh(isInDmu: true, suppressLiveForP4: false);
            if (p3BlackHoleTracker.HasLiveSignal)
            {
                p3LimitCutTracker.Reset();
            }
        }

        UpdateDisplayMode();
    }

    private void ResetHelperState(string snapshotReason)
    {
        p3BlackHoleTracker.Refresh(isInDmu: false, suppressLiveForP4: false);
        p3LimitCutTracker.Reset();
        p1Tracker.Reset();
        CaptureCurrentPullSnapshot(snapshotReason);
        currentEntries.Clear();
        currentMembers.Clear();
        currentAssignments.Clear();
        localAssignments.Clear();
        currentGazeAssignments.Clear();
        currentPartyCallAssignments.Clear();
        currentBossTells.Clear();
        latestBossTells.Clear();
        capturedDebuffStates.Clear();
        capturedFloodWounds.Clear();
        statusTimerAnchors.Clear();
        activeBossTellKeysLastFrame.Clear();
        ResetPullState();
        p4SeenThisPull = false;
        UpdateDisplayMode();
    }

    private bool HasP4LiveSignal()
    {
        return currentAssignments.Count > 0 ||
            localAssignments.Count > 0 ||
            currentGazeAssignments.Count > 0 ||
            currentPartyCallAssignments.Count > 0 ||
            currentBossTells.Count > 0;
    }

    private void UpdateDisplayMode()
    {
        DisplayMode = HasP4LiveSignal()
            ? DmuHelperDisplayMode.P4Debuffs
            : !p4SeenThisPull && (p3BlackHoleTracker.HasLiveSignal || p3LimitCutTracker.HasLiveSignal)
                ? DmuHelperDisplayMode.P3BlackHole
                : Configuration.ShowP1Calls && p1Tracker.HasLiveSignal
                    ? DmuHelperDisplayMode.P1Calls
                    : Configuration.PreviewWhenInactive
                        ? DmuHelperDisplayMode.Preview
                        : DmuHelperDisplayMode.Empty;
    }

    private void RefreshBossTellSnapshots()
    {
        currentBossTells.Clear();
        var activeTellKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null
                || !gameObject.IsValid()
                || gameObject is not ICharacter character
                || character is not IBattleChara battleChara)
            {
                continue;
            }

            var boss = ResolveBoss(character);
            if (boss is not (P4Boss.NeoExdeath or P4Boss.Chaos))
            {
                continue;
            }

            LogBossTellChange(boss, battleChara);

            foreach (var status in battleChara.StatusList)
            {
                if (status.StatusId != BossTellStatusId)
                {
                    continue;
                }

                var param = status.Param;
                var reality = BossTellRealities.TryGetValue(param, out var knownReality)
                    ? knownReality
                    : RealityState.Unknown;
                var group = ResolveTellGroup(boss);
                var snapshot = new BossTellSnapshot(boss, group, status.StatusId, param, reality, DateTime.UtcNow);
                currentBossTells.Add(snapshot);
                latestBossTells[GetTellKey(boss, group)] = snapshot;
                var activeTellKey = $"{boss}:{group}:{param}";
                activeTellKeys.Add(activeTellKey);
                if (!activeBossTellKeysLastFrame.Contains(activeTellKey))
                {
                    currentPullBossTells.Add(snapshot);
                }

                break;
            }
        }

        activeBossTellKeysLastFrame.Clear();
        foreach (var key in activeTellKeys)
        {
            activeBossTellKeysLastFrame.Add(key);
        }
    }

    private void RefreshAssignments()
    {
        currentAssignments.Clear();
        PruneCapturedDebuffStates();
        var localMember = GetLocalPartyMember();
        if (localMember is null)
        {
            UpdateCurrentPullDebuffRecords();
            return;
        }

        foreach (var entry in currentEntries.Where(entry => entry.IsWatched && entry.MemberKey == localMember.MemberKey))
        {
            if (!WatchedStatuses.TryGetValue(entry.StatusId, out var rule))
            {
                continue;
            }

            var capturedState = rule.Group == P4MechanicGroup.Flood && P4Flood.UsesTruthLieTell(rule.Id)
                ? GetOrUpdateCapturedDebuffState(entry, rule)
                : new CapturedDebuffState(RealityState.Unknown, null, DateTime.UtcNow);
            var woundColor = rule.Group == P4MechanicGroup.Flood
                ? GetWoundColor(entry.MemberKey)
                : WoundColor.None;
            var floodSide = P4Flood.ResolveSide(rule.Id, woundColor, capturedState.Reality);

            if (rule.Group == P4MechanicGroup.Flood)
            {
                currentAssignments.Add(new P4DebuffAssignment(
                    entry,
                    rule,
                    capturedState.Reality,
                    capturedState.TellParam,
                    GetInstruction(rule, capturedState.Reality, woundColor, floodSide),
                    woundColor,
                    floodSide));
                continue;
            }

            capturedState = GetOrUpdateCapturedDebuffState(entry, rule);

            currentAssignments.Add(new P4DebuffAssignment(
                entry,
                rule,
                capturedState.Reality,
                capturedState.TellParam,
                GetInstruction(rule, capturedState.Reality, woundColor, floodSide),
                woundColor,
                floodSide));
        }

        UpdateCurrentPullDebuffRecords();
    }

    private void RefreshLocalAssignments()
    {
        localAssignments.Clear();
        var localMember = GetLocalPartyMember();
        if (localMember is null)
        {
            return;
        }

        localAssignments.AddRange(currentAssignments
            .Where(assignment => assignment.Entry.MemberKey == localMember.MemberKey));
    }

    private void RefreshPartyGazeAssignments()
    {
        RefreshPartyAssignments(currentGazeAssignments, statusId => statusId == CursedShriekStatusId);
    }

    private void RefreshPartyCallAssignments()
    {
        RefreshPartyAssignments(currentPartyCallAssignments, P4Calls.PartyStatusIds.Contains);
    }

    private void RefreshPartyAssignments(List<P4DebuffAssignment> target, Func<uint, bool> includeStatus)
    {
        target.Clear();
        foreach (var entry in currentEntries.Where(entry => includeStatus(entry.StatusId)))
        {
            if (!WatchedStatuses.TryGetValue(entry.StatusId, out var rule))
            {
                continue;
            }

            var capturedState = GetOrUpdateCapturedDebuffState(entry, rule);
            target.Add(new P4DebuffAssignment(
                entry,
                rule,
                capturedState.Reality,
                capturedState.TellParam,
                GetInstruction(rule, capturedState.Reality, WoundColor.None, FloodSide.None)));
        }
    }

    private PartyMemberSnapshot? GetLocalPartyMember()
    {
        var localContentId = PlayerState.ContentId;
        var localMember = currentMembers.FirstOrDefault(member => member.ContentId != 0 && member.ContentId == localContentId);
        if (localMember is not null)
        {
            return localMember;
        }

        var localEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;
        return localEntityId != 0
            ? currentMembers.FirstOrDefault(member => member.EntityId == localEntityId)
            : null;
    }

    private void UpdateCurrentPullDebuffRecords()
    {
        var activeKeys = currentAssignments
            .Select(assignment => assignment.Entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var staleKey in activeDebuffKeysLastFrame.Where(key => !activeKeys.Contains(key)).ToList())
        {
            activeDebuffRecordIndexes.Remove(staleKey);
        }

        foreach (var assignment in currentAssignments)
        {
            if (!activeDebuffRecordIndexes.TryGetValue(assignment.Entry.Key, out var recordIndex))
            {
                currentPullDebuffRecords.Add(CreateDebuffRecord(assignment));
                activeDebuffRecordIndexes[assignment.Entry.Key] = currentPullDebuffRecords.Count - 1;
                continue;
            }

            var existingRecord = currentPullDebuffRecords[recordIndex];
            if ((existingRecord.Reality == RealityState.Unknown && assignment.Reality != RealityState.Unknown) ||
                (existingRecord.WoundColor == WoundColor.None && assignment.WoundColor != WoundColor.None) ||
                (existingRecord.FloodSide == FloodSide.None && assignment.FloodSide != FloodSide.None))
            {
                currentPullDebuffRecords[recordIndex] = CreateDebuffRecord(assignment) with
                {
                    SeenAtUtc = existingRecord.SeenAtUtc,
                    RemainingTimeAtCapture = existingRecord.RemainingTimeAtCapture,
                };
            }
        }

        activeDebuffKeysLastFrame.Clear();
        foreach (var key in activeKeys)
        {
            activeDebuffKeysLastFrame.Add(key);
        }
    }

    private static P4DebuffRecord CreateDebuffRecord(P4DebuffAssignment assignment)
    {
        return new P4DebuffRecord(
            DateTime.UtcNow,
            assignment.Entry.Key,
            assignment.Entry.MemberKey,
            assignment.Entry.MemberName,
            assignment.Entry.PartyIndex,
            assignment.Entry.StatusId,
            assignment.Rule.Name,
            assignment.Rule.SourceBoss,
            assignment.Rule.Group,
            assignment.Reality,
            assignment.TellParam,
            assignment.Entry.RemainingTime,
            assignment.Instruction,
            assignment.WoundColor,
            assignment.FloodSide);
    }

    private void PruneCapturedDebuffStates()
    {
        if (capturedDebuffStates.Count == 0)
        {
            return;
        }

        var activeKeys = currentEntries
            .Where(entry => entry.IsWatched)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in capturedDebuffStates.Keys.ToArray())
        {
            if (!activeKeys.Contains(key))
            {
                capturedDebuffStates.Remove(key);
            }
        }
    }

    private CapturedDebuffState GetOrUpdateCapturedDebuffState(PartyStatusEntry entry, WatchedStatus rule)
    {
        if (!capturedDebuffStates.TryGetValue(entry.Key, out var capturedState))
        {
            capturedState = CaptureDebuffState(rule);
            capturedDebuffStates[entry.Key] = capturedState;
            LogCapturedDebuffState(entry, capturedState);
            return capturedState;
        }

        if (capturedState.Reality != RealityState.Unknown || !TryGetRelevantTell(rule, out var tell))
        {
            return capturedState;
        }

        capturedState = new CapturedDebuffState(tell!.Reality, tell.Param, DateTime.UtcNow);
        capturedDebuffStates[entry.Key] = capturedState;
        LogCapturedDebuffState(entry, capturedState);
        return capturedState;
    }

    private static void LogCapturedDebuffState(PartyStatusEntry entry, CapturedDebuffState capturedState)
    {
        Log.Information(
            "[P4 capture] {Member} {Status}: {Reality} from tell param {Param} with {Remaining:0.0}s left.",
            entry.MemberName,
            entry.StatusName,
            capturedState.Reality,
            capturedState.TellParam?.ToString() ?? "none",
            entry.RemainingTime);
    }

    // Logs every change to the boss's tell statuses, including when more than one is active at once.
    private void LogBossTellChange(P4Boss boss, IBattleChara battleChara)
    {
        var tellParams = string.Join(", ", battleChara.StatusList
            .Where(status => status.StatusId == BossTellStatusId)
            .Select(status => status.Param));
        if (lastLoggedTellParams.TryGetValue(boss, out var lastParams) && lastParams == tellParams)
        {
            return;
        }

        lastLoggedTellParams[boss] = tellParams;
        Log.Information(
            "[P4 tell] {Boss} tell params now [{Params}].",
            boss,
            tellParams.Length == 0 ? "none" : tellParams);
    }

    private CapturedDebuffState CaptureDebuffState(WatchedStatus rule)
    {
        return TryGetRelevantTell(rule, out var tell)
            ? new CapturedDebuffState(tell!.Reality, tell.Param, DateTime.UtcNow)
            : new CapturedDebuffState(RealityState.Unknown, null, DateTime.UtcNow);
    }

    private void CaptureActiveFloodWounds(IReadOnlyList<PartyStatusEntry> activeEntries, DateTime now)
    {
        foreach (var entry in activeEntries.Where(entry => P4Flood.IsWoundStatus(entry.StatusId)))
        {
            var effectiveWound = P4Flood.GetEffectiveWoundColor(entry.StatusId);
            if (effectiveWound == WoundColor.None)
            {
                continue;
            }

            capturedFloodWounds[entry.MemberKey] = new CapturedFloodWoundState(effectiveWound, now);
        }
    }

    private void PruneCapturedFloodWounds(DateTime now)
    {
        if (capturedFloodWounds.Count == 0)
        {
            return;
        }

        var activeMemberKeys = currentMembers
            .Select(member => member.MemberKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (memberKey, woundState) in capturedFloodWounds.ToArray())
        {
            if (!activeMemberKeys.Contains(memberKey) || now - woundState.CapturedAtUtc > FloodWoundMemory)
            {
                capturedFloodWounds.Remove(memberKey);
            }
        }
    }

    private static P4Boss ResolveBoss(ICharacter character)
    {
        var name = character.Name.TextValue;
        if (string.Equals(name, "Neo Exdeath", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.NeoExdeath;
        }

        if (string.Equals(name, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.Chaos;
        }

        if (string.Equals(name, "Kefka", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.Kefka;
        }

        return P4Boss.Unknown;
    }

    private P4MechanicGroup ResolveTellGroup(P4Boss boss)
    {
        if (boss == P4Boss.Chaos)
        {
            return P4MechanicGroup.Chaos;
        }

        return P4MechanicGroup.GrandCross;
    }

    private bool TryGetRelevantTell(WatchedStatus rule, out BossTellSnapshot? tell)
    {
        tell = null;
        var key = rule.Group == P4MechanicGroup.Flood
            ? GetTellKey(P4Boss.NeoExdeath, P4MechanicGroup.GrandCross)
            : GetTellKey(rule.SourceBoss, rule.Group);
        if (!latestBossTells.TryGetValue(key, out var candidate))
        {
            return false;
        }

        if (DateTime.UtcNow - candidate.SeenAtUtc > BossTellFreshness)
        {
            return false;
        }

        tell = candidate;
        return true;
    }

    private static string GetTellKey(P4Boss boss, P4MechanicGroup group)
    {
        return $"{boss}:{group}";
    }

    private static string GetInstruction(WatchedStatus rule, RealityState reality, WoundColor woundColor, FloodSide floodSide)
    {
        return rule.Group switch
        {
            P4MechanicGroup.GrandCross => GetGrandCrossInstruction(rule.Id, reality),
            P4MechanicGroup.Chaos => GetChaosInstruction(rule.Id, reality),
            P4MechanicGroup.Flood => P4Flood.FormatInstruction(rule.Id, woundColor, floodSide, reality),
            _ => "Tracked status.",
        };
    }

    private static string GetGrandCrossInstruction(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            5545 => reality switch
            {
                RealityState.Real => "Stack with your assigned group.",
                RealityState.Fake => "Spread away from your assigned group.",
                _ => "Water: real stacks, fake spreads.",
            },
            5544 => reality switch
            {
                RealityState.Real => "Spread away from your assigned group.",
                RealityState.Fake => "Stack with your assigned group.",
                _ => "Lightning: real spreads, fake stacks.",
            },
            5543 => reality switch
            {
                RealityState.Real => "Stand out and have players look away from you.",
                RealityState.Fake => "Stand center so players can look at you.",
                _ => "Shriek: real look away, fake look at.",
            },
            5546 => reality switch
            {
                RealityState.Real => "Stop moving and stop actions when it resolves.",
                RealityState.Fake => "Keep moving when it resolves.",
                _ => "Bomb: real stillness, fake motion.",
            },
            _ => "Tracked Grand Cross debuff.",
        };
    }

    private static string GetChaosInstruction(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            5548 => reality switch
            {
                RealityState.Real => "Resolve as delayed donut.",
                RealityState.Fake => "Resolve as delayed point-blank AoE.",
                _ => "Dynamic Fluid: real donut, fake point-blank.",
            },
            5547 => reality switch
            {
                RealityState.Real => "Resolve as delayed point-blank AoE.",
                RealityState.Fake => "Resolve as delayed donut.",
                _ => "Entropy: real point-blank, fake donut.",
            },
            _ => "Tracked Chaos debuff.",
        };
    }

    private WoundColor GetWoundColor(string memberKey)
    {
        foreach (var entry in currentEntries)
        {
            if (!string.Equals(entry.MemberKey, memberKey, StringComparison.Ordinal))
            {
                continue;
            }

            var effectiveWound = P4Flood.GetEffectiveWoundColor(entry.StatusId);
            if (effectiveWound != WoundColor.None)
            {
                return effectiveWound;
            }
        }

        if (capturedFloodWounds.TryGetValue(memberKey, out var capturedWound))
        {
            return capturedWound.EffectiveWoundColor;
        }

        return WoundColor.None;
    }

    internal string GetStatusName(uint statusId)
    {
        if (statusNameCache.TryGetValue(statusId, out var cachedName))
        {
            return cachedName;
        }

        var statusName = $"Status {statusId}";
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            var sheetName = status?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                statusName = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status name for {StatusId}.", statusId);
        }

        statusNameCache[statusId] = statusName;
        return statusName;
    }

    internal uint GetStatusIconId(uint statusId)
    {
        if (statusIconCache.TryGetValue(statusId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            iconId = status?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status icon for {StatusId}.", statusId);
        }

        statusIconCache[statusId] = iconId;
        return iconId;
    }

    private void OnDutyReset(IDutyStateEventArgs args)
    {
        if (args.TerritoryType.RowId != DmuTerritoryId)
        {
            return;
        }

        p3BlackHoleTracker.OnDutyReset();
        p3LimitCutTracker.Reset();
        p1Tracker.Reset();
        if (Configuration.DiscoveryLogging)
        {
            discoveryLog.Reset("Duty start/wipe/reset");
        }

        CaptureCurrentPullSnapshot("Wipe/reset detected");
        currentEntries.Clear();
        currentMembers.Clear();
        currentAssignments.Clear();
        localAssignments.Clear();
        currentGazeAssignments.Clear();
        currentPartyCallAssignments.Clear();
        currentBossTells.Clear();
        latestBossTells.Clear();
        capturedDebuffStates.Clear();
        capturedFloodWounds.Clear();
        statusTimerAnchors.Clear();
        activeBossTellKeysLastFrame.Clear();
        ResetPullState();
        p4SeenThisPull = false;
        UpdateDisplayMode();
    }

    private void UpdatePullTracking()
    {
        if (!HasP4Record())
        {
            return;
        }

        pullStartedAtUtc ??= DateTime.UtcNow;
        lastKnownPullElapsedSeconds = CurrentPullElapsedSeconds;
    }

    private void CaptureCurrentPullSnapshot(string reason)
    {
        if (!HasP4Record())
        {
            return;
        }

        pullSnapshots.Add(new P4PullSnapshot(
            DateTime.UtcNow,
            reason,
            CurrentPullElapsedSeconds,
            currentPullDebuffRecords.ToList(),
            currentPullBossTells.ToList()));
    }

    private bool HasP4Record()
    {
        return currentPullDebuffRecords.Count > 0 ||
            currentPullBossTells.Count > 0 ||
            currentAssignments.Count > 0 ||
            currentGazeAssignments.Count > 0 ||
            currentPartyCallAssignments.Count > 0 ||
            currentBossTells.Count > 0;
    }

    private float GetSmoothedStatusRemainingTime(string key, float sourceRemainingTime, DateTime now)
    {
        if (sourceRemainingTime <= 0.0f)
        {
            statusTimerAnchors.Remove(key);
            return sourceRemainingTime;
        }

        if (!statusTimerAnchors.TryGetValue(key, out var anchor) ||
            MathF.Abs(sourceRemainingTime - anchor.SourceRemainingTime) >= StatusTimerAnchorRefreshThreshold)
        {
            anchor = new StatusTimerAnchor(sourceRemainingTime, now);
            statusTimerAnchors[key] = anchor;
        }

        var elapsedSeconds = (float)(now - anchor.SeenAtUtc).TotalSeconds;
        return MathF.Max(0.0f, anchor.SourceRemainingTime - elapsedSeconds);
    }

    private void PruneStatusTimerAnchors(IReadOnlyList<PartyStatusEntry> activeEntries)
    {
        if (statusTimerAnchors.Count == 0)
        {
            return;
        }

        var activeKeys = activeEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in statusTimerAnchors.Keys.ToArray())
        {
            if (!activeKeys.Contains(key))
            {
                statusTimerAnchors.Remove(key);
            }
        }
    }

    private void ResetPullState()
    {
        currentPullDebuffRecords.Clear();
        CallSetHistory.Clear();
        lastLoggedTellParams.Clear();
        currentPullBossTells.Clear();
        capturedFloodWounds.Clear();
        activeDebuffRecordIndexes.Clear();
        activeDebuffKeysLastFrame.Clear();
        activeBossTellKeysLastFrame.Clear();
        pullStartedAtUtc = null;
        lastKnownPullElapsedSeconds = 0.0f;
    }

    private static IReadOnlyList<string> BuildP3AssignmentOrderLines(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments)
    {
        return GetP3AssignmentChatGroups()
            .Select(group => new
            {
                group.Label,
                Names = assignments
                    .Where(assignment => assignment.HasLine)
                    .Where(assignment => assignment.LineGroup == group.LineGroup && assignment.HadAccretion == group.HadAccretion)
                    .OrderBy(assignment => assignment.PartyIndex)
                    .Select(assignment => assignment.MemberName)
                    .ToList(),
            })
            .Where(group => group.Names.Count > 0)
            .Select(group => $"{group.Label}: {string.Join(", ", group.Names)}")
            .ToList();
    }

    private static IReadOnlyList<(string Label, P3.LineGroup LineGroup, bool HadAccretion)> GetP3AssignmentChatGroups()
    {
        return
        [
            ("FIL", P3.LineGroup.First, false),
            ("SIL", P3.LineGroup.Second, false),
            ("TIL", P3.LineGroup.Third, false),
            ("FIL Accretion", P3.LineGroup.First, true),
            ("SIL Accretion", P3.LineGroup.Second, true),
            ("TIL Accretion", P3.LineGroup.Third, true),
        ];
    }

    private static unsafe void SendPartyChatMessage(string message)
    {
        try
        {
            var uiModule = UIModule.Instance();
            var shellModule = RaptureShellModule.Instance();
            if (uiModule == null || shellModule == null)
            {
                Log.Warning("Could not send DMU helper party chat message because the UI shell is unavailable.");
                return;
            }

            using var command = new Utf8String($"/p {SanitizeChatText(message)}");
            shellModule->ExecuteCommandInner(&command, uiModule);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not send DMU helper party chat message.");
        }
    }

    private static string SanitizeChatText(string message)
    {
        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed record StatusTimerAnchor(float SourceRemainingTime, DateTime SeenAtUtc);
}
