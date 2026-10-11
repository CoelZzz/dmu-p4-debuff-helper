namespace DMUP4DebuffHelper;

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;
using System.Linq;

public enum ForsakenMarker
{
    None,
    Stack,
    Circle,
    Cone,
}

// P2 Forsaken calls, decoded from BossMod replays:
// - Each player gets a stack (715), circle/defam (716) or cone (717) headmarker; soaking a tower hands out a new one.
// - Tower sets appear as arena effects 1-8 (two towers per set, 8 sets) and resolve as Path of Light about 10s later.
// - Buddy pairs where either buddy starts with the stack soak sets 1, 2, 3 and 8; the other pairs soak sets 4-7.
// - Buddies with matching markers split into different towers.
// - Kefka casts Future's End or Past's End during the even sets.
public sealed class P2Tracker : IDisposable
{
    public const uint StackIcon = 715;
    public const uint CircleIcon = 716;
    public const uint ConeIcon = 717;
    public const uint FuturesEndActionId = 47826;
    public const uint PastsEndActionId = 47827;
    public const int TowerSetCount = 8;

    private static readonly int[] StackGroupSoakSets = [1, 2, 3, 8];
    private static readonly int[] OtherGroupSoakSets = [4, 5, 6, 7];
    private static readonly TimeSpan OpeningMarkerWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TowerBurstWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TowerSoakDelay = TimeSpan.FromSeconds(10.1);
    private static readonly TimeSpan SequenceTimeout = TimeSpan.FromSeconds(25);

    private readonly Plugin plugin;
    private readonly GameEventHooks hooks;
    private readonly Dictionary<uint, ForsakenMarker> markers = new();
    private readonly Dictionary<uint, ForsakenMarker> openingMarkers = new();
    private DateTime? sequenceStartedAtUtc;
    private DateTime? lastEventAtUtc;
    private DateTime? lastTowerSetAtUtc;
    private int towerSet;
    private uint endsActionId;
    private float endsRemaining;

    public P2Tracker(Plugin plugin, GameEventHooks hooks)
    {
        this.plugin = plugin;
        this.hooks = hooks;
        hooks.ActorControlReceived += OnActorControl;
        hooks.MapEffectApplied += OnMapEffect;
    }

    public bool HasLiveSignal => lastEventAtUtc is { } lastEvent && DateTime.UtcNow - lastEvent <= SequenceTimeout;

    public void Dispose()
    {
        hooks.ActorControlReceived -= OnActorControl;
        hooks.MapEffectApplied -= OnMapEffect;
    }

    public void Reset()
    {
        markers.Clear();
        openingMarkers.Clear();
        sequenceStartedAtUtc = null;
        lastEventAtUtc = null;
        lastTowerSetAtUtc = null;
        towerSet = 0;
        endsActionId = 0;
    }

    // Polled each frame in DMU: Past's End / Future's End are plain casts, which have no actor control event.
    public void Update()
    {
        endsActionId = 0;
        if (!HasLiveSignal)
        {
            return;
        }

        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is IBattleChara { IsCasting: true } caster
                && caster.CastActionId is FuturesEndActionId or PastsEndActionId)
            {
                endsActionId = caster.CastActionId;
                endsRemaining = MathF.Max(0.0f, caster.TotalCastTime - caster.CurrentCastTime);
                lastEventAtUtc = DateTime.UtcNow;
                return;
            }
        }
    }

    public List<CallBlock> GetCalls(DateTime now)
    {
        var calls = new List<CallBlock>();
        if (!HasLiveSignal)
        {
            return calls;
        }

        var localId = Plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        var buddy = FindBuddy();
        var set = Math.Clamp(towerSet, 1, TowerSetCount);
        var remaining = lastTowerSetAtUtc is { } towerSeenAt
            ? MathF.Max(0.0f, (float)(towerSeenAt + TowerSoakDelay - now).TotalSeconds)
            : 0.0f;

        markers.TryGetValue(localId, out var myMarker);
        var buddyMarker = buddy is not null && markers.TryGetValue(buddy.EntityId, out var known) ? known : ForsakenMarker.None;
        var soaksFirst = IsStackGroup(localId, buddy?.EntityId);

        // The buddy line stays first: their marker matters for the whole phase, not just this set.
        if (buddy is not null)
        {
            var soaking = soaksFirst is { } stackGroup && GetSoakSets(stackGroup).Contains(set);
            calls.Add(BuildBuddyCall(buddy.Name.TextValue, buddyMarker, myMarker, soaking));
        }

        calls.Add(BuildSetCall(set, myMarker, soaksFirst, remaining));

        if (endsActionId != 0)
        {
            calls.Add(BuildEndsCall(endsActionId == PastsEndActionId, endsRemaining));
        }

        return calls;
    }

    // soaksFirst: true = stack group (sets 1, 2, 3, 8), false = other group (sets 4-7), null = group not known yet.
    public static CallBlock BuildSetCall(int set, ForsakenMarker marker, bool? soaksFirst, float remainingTime)
    {
        if (soaksFirst is not { } stackGroup)
        {
            return new CallBlock(
                remainingTime,
                [new P4CallLine([new($"{set}"), FormatMarker(marker)]), new P4CallLine([new("Pick your P2 buddy", P4CallTone.Muted)])],
                "Your soak group depends on whether you or your buddy started with the stack.");
        }

        var soakSets = GetSoakSets(stackGroup);
        var soaking = soakSets.Contains(set);
        var nextSwitch = Enumerable.Range(set + 1, Math.Max(0, TowerSetCount - set))
            .Cast<int?>()
            .FirstOrDefault(next => soakSets.Contains(next!.Value) != soaking);

        // Matches the call style "1 BAIT - 4 CONE" / "1 CONE - 4 BAIT": this set, then when it next switches.
        var segments = new List<P4CallSegment> { new($"{set}") };
        segments.Add(soaking ? FormatMarker(marker) : new P4CallSegment("BAIT", P4CallTone.Direction));
        if (nextSwitch is { } next)
        {
            segments.Add(new("-", P4CallTone.Muted));
            segments.Add(new($"{next}"));
            segments.Add(soaking ? new P4CallSegment("BAIT", P4CallTone.Direction) : FormatMarker(marker));
        }

        var tooltip = $"Tower set {set} of {TowerSetCount}. You soak sets {string.Join(", ", soakSets)}.";
        return new CallBlock(remainingTime, [new P4CallLine(segments)], tooltip);
    }

    // No timer: the buddy line stays up for the whole phase.
    public static CallBlock BuildBuddyCall(string buddyName, ForsakenMarker buddyMarker, ForsakenMarker myMarker, bool soaking)
    {
        var segments = new List<P4CallSegment> { new("Buddy", P4CallTone.Muted), FormatMarker(buddyMarker) };
        if (soaking && buddyMarker != ForsakenMarker.None && myMarker != ForsakenMarker.None)
        {
            // Buddies share a tower unless their markers match; then the melee flexes to the other tower.
            segments.Add(new("-", P4CallTone.Muted));
            segments.Add(buddyMarker == myMarker
                ? new P4CallSegment("SPLIT TOWERS", P4CallTone.Stop)
                : new P4CallSegment("SAME TOWER", P4CallTone.In));
        }

        return new CallBlock(float.NaN, [new P4CallLine(segments)], $"Buddy: {buddyName}");
    }

    public static CallBlock BuildEndsCall(bool isPast, float remainingTime)
    {
        var line = isPast
            ? new P4CallLine([new("PAST", P4CallTone.Direction), new("-", P4CallTone.Muted), new("BETWEEN")])
            : new P4CallLine([new("FUTURE", P4CallTone.Direction), new("-", P4CallTone.Muted), new("ACROSS")]);
        return new CallBlock(remainingTime, [line], isPast ? "Past's End" : "Future's End");
    }

    private static P4CallSegment FormatMarker(ForsakenMarker marker)
    {
        return marker switch
        {
            ForsakenMarker.Stack => new("STACK", P4CallTone.Stack),
            ForsakenMarker.Circle => new("CIRCLE", P4CallTone.Circle),
            ForsakenMarker.Cone => new("CONE", P4CallTone.Cone),
            _ => new("?", P4CallTone.Muted),
        };
    }

    private static int[] GetSoakSets(bool stackGroup)
    {
        return stackGroup ? StackGroupSoakSets : OtherGroupSoakSets;
    }

    private bool? IsStackGroup(uint localId, uint? buddyId)
    {
        var mineIsStack = openingMarkers.TryGetValue(localId, out var mine) && mine == ForsakenMarker.Stack;
        if (mineIsStack)
        {
            return true;
        }

        if (buddyId is not { } id)
        {
            return null;
        }

        return openingMarkers.TryGetValue(id, out var theirs) && theirs == ForsakenMarker.Stack;
    }

    private IPlayerCharacter? FindBuddy()
    {
        var buddyName = plugin.Configuration.P2BuddyName;
        if (string.IsNullOrWhiteSpace(buddyName))
        {
            return null;
        }

        return Plugin.ObjectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(player => string.Equals(player.Name.TextValue, buddyName, StringComparison.OrdinalIgnoreCase));
    }

    private void OnActorControl(uint entityId, uint category, uint[] args)
    {
        if (!plugin.IsInDmu
            || category != GameEventHooks.HeadmarkerCategory
            || args[0] is not (StackIcon or CircleIcon or ConeIcon)
            || Plugin.ObjectTable.SearchByEntityId(entityId) is not IPlayerCharacter)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!HasLiveSignal)
        {
            // The first markers of a new Forsaken: everyone gets one at once, before any tower appears.
            Reset();
            sequenceStartedAtUtc = now;
        }

        var marker = args[0] switch
        {
            StackIcon => ForsakenMarker.Stack,
            CircleIcon => ForsakenMarker.Circle,
            _ => ForsakenMarker.Cone,
        };
        markers[entityId] = marker;
        if (sequenceStartedAtUtc is { } startedAt && now - startedAt <= OpeningMarkerWindow)
        {
            openingMarkers[entityId] = marker;
        }

        lastEventAtUtc = now;
    }

    private void OnMapEffect(uint index, ushort state, ushort flags)
    {
        // Tower spots are arena effects 1-8; state 1 shows a tower. Two appear together per set.
        if (!HasLiveSignal || index is < 1 or > 8 || state != 1)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (lastTowerSetAtUtc is not { } lastSet || now - lastSet > TowerBurstWindow)
        {
            towerSet = Math.Min(towerSet + 1, TowerSetCount);
            lastTowerSetAtUtc = now;
        }

        lastEventAtUtc = now;
    }
}
