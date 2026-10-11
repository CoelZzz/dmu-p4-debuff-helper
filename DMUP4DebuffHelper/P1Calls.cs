namespace DMUP4DebuffHelper;

using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

// P1 (Kefka) calls, decoded from discovery logs and checked against BossMod replays:
// - Kefka's ring headmarkers say which mechanic each ring controls and whether it is real (even) or fake (odd).
// - The statue gaze symbol is an arena object that animates about 10s before the gaze resolves.
// - Rocks always drop from the statue standing at RockStatuePosition, on whichever role group it tethered.
// - A cleave warning object animates about 5s before a centre statue cleaves half the arena.
public sealed class P1Tracker : IDisposable
{
    public const uint StackSpreadFakeIcon = 673;
    public const uint StackSpreadRealIcon = 674;
    public const uint IceFakeIcon = 675;
    public const uint IceRealIcon = 676;
    public const uint LightningFakeIcon = 677;
    public const uint LightningRealIcon = 678;
    public const uint SpreadMarkerIcon = 127;
    public const uint StackMarkerIcon = 128;
    public const uint RocksActionId = 47792;
    public const uint WestCleaveActionId = 47793;
    public const uint EastCleaveActionId = 47794;

    private const uint KefkaBaseId = 19504;
    private const uint GazeRealSymbolBaseId = 2015166;
    private const uint GazeFakeSymbolBaseId = 2015167;
    private const uint ObjectAnimationShown = 64;
    private const uint ObjectAnimationResolved = 256;
    private const uint GravenImageBaseId = 19505;
    private const uint StatueTetherId = 45;
    private const uint WestCleaveWarningBaseId = 2015164;
    private const uint EastCleaveWarningBaseId = 2015165;
    private const float RockStatueTolerance = 3.0f;
    private static readonly Vector2 RockStatuePosition = new(126.0f, 41.5f);
    private static readonly TimeSpan MarkerBurstWindow = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan RingResolveDelay = TimeSpan.FromSeconds(5.1);
    private static readonly TimeSpan GazeResolveDelay = TimeSpan.FromSeconds(9.9);
    private static readonly TimeSpan FirstRocksResolveDelay = TimeSpan.FromSeconds(10.6);
    private static readonly TimeSpan SecondRocksResolveDelay = TimeSpan.FromSeconds(12.4);
    private static readonly TimeSpan RocksSetGap = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan CleaveResolveDelay = TimeSpan.FromSeconds(5.2);
    private static readonly TimeSpan MaxCallOverrun = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CallLinger = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PhaseLinger = TimeSpan.FromSeconds(30);

    private readonly Plugin plugin;
    private readonly GameEventHooks hooks;
    private readonly HashSet<uint> ringIcons = [];
    private DateTime? ringSeenAtUtc;
    private uint? playerMarkerIcon;
    private DateTime? playerMarkerSeenAtUtc;
    private RealityState gazeReality = RealityState.Unknown;
    private DateTime? gazeSeenAtUtc;
    private readonly List<P1RockTarget> rockTargets = [];
    private DateTime? rocksSeenAtUtc;
    private DateTime? lastRocksSetAtUtc;
    private TimeSpan rocksResolveDelay = FirstRocksResolveDelay;
    private bool cleavesWest;
    private DateTime? cleaveSeenAtUtc;
    private DateTime? lastEventAtUtc;

    public P1Tracker(Plugin plugin, GameEventHooks hooks)
    {
        this.plugin = plugin;
        this.hooks = hooks;
        hooks.ActorControlReceived += OnActorControl;
    }

    // True while a call is showing, and for a while after, so the P1 view does not flicker between mechanics.
    public bool HasLiveSignal => lastEventAtUtc is { } lastEvent && DateTime.UtcNow - lastEvent <= PhaseLinger;

    public void Dispose()
    {
        hooks.ActorControlReceived -= OnActorControl;
    }

    public void Reset()
    {
        ringIcons.Clear();
        ringSeenAtUtc = null;
        playerMarkerIcon = null;
        playerMarkerSeenAtUtc = null;
        gazeReality = RealityState.Unknown;
        gazeSeenAtUtc = null;
        rockTargets.Clear();
        rocksSeenAtUtc = null;
        lastRocksSetAtUtc = null;
        cleaveSeenAtUtc = null;
        lastEventAtUtc = null;
    }

    // Called for every action effect in DMU so rocks and cleave calls clear the moment they resolve.
    public void OnActionEffect(uint actionId)
    {
        switch (actionId)
        {
            case RocksActionId:
                rocksSeenAtUtc = null;
                break;
            case WestCleaveActionId or EastCleaveActionId:
                cleaveSeenAtUtc = null;
                break;
        }
    }

    public List<CallBlock> GetCalls(DateTime now)
    {
        var calls = new List<CallBlock>();
        if (ringSeenAtUtc is { } ringSeenAt && now - ringSeenAt <= RingResolveDelay + CallLinger)
        {
            var marker = playerMarkerSeenAtUtc is { } markerSeenAt && (markerSeenAt - ringSeenAt).Duration() <= MarkerBurstWindow
                ? playerMarkerIcon
                : null;
            calls.Add(BuildRingCall(ringIcons, marker, Remaining(ringSeenAt + RingResolveDelay, now)));
        }

        if (gazeSeenAtUtc is { } gazeSeenAt && now - gazeSeenAt <= GazeResolveDelay + CallLinger)
        {
            calls.Add(BuildGazeCall(gazeReality, Remaining(gazeSeenAt + GazeResolveDelay, now)));
        }

        // Rocks and cleaves clear on their resolving action, with a timeout in case it is missed (e.g. a wipe).
        if (rocksSeenAtUtc is { } rocksSeenAt && now - rocksSeenAt <= rocksResolveDelay + MaxCallOverrun)
        {
            calls.Add(BuildRocksCall(rockTargets, Remaining(rocksSeenAt + rocksResolveDelay, now)));
        }

        if (cleaveSeenAtUtc is { } cleaveSeenAt && now - cleaveSeenAt <= CleaveResolveDelay + MaxCallOverrun)
        {
            calls.Add(BuildCleaveCall(cleavesWest, Remaining(cleaveSeenAt + CleaveResolveDelay, now)));
        }

        return calls
            .OrderBy(call => call.RemainingTime)
            .ToList();
    }

    public static CallBlock BuildRocksCall(IReadOnlyList<P1RockTarget> targets, float remainingTime)
    {
        var supportCount = targets.Count(target => target.IsSupport);
        var group = supportCount == targets.Count
            ? "SUPPORT"
            : supportCount == 0
                ? "DPS"
                : null;
        var names = string.Join(", ", targets.Select(target => target.Name));
        var line = group is null
            ? new P4CallLine([new("ROCKS", P4CallTone.Rocks), new("on " + names, P4CallTone.Muted)])
            : new P4CallLine([new(group), new("ROCKS", P4CallTone.Rocks)]);
        return new CallBlock(remainingTime, [line], $"Tethered to the rock statue: {names}");
    }

    public static CallBlock BuildCleaveCall(bool cleavesWest, float remainingTime)
    {
        // The statue cleaves one half of the arena, so the call is the other half.
        var safeSide = cleavesWest ? "EAST" : "WEST";
        var tooltip = cleavesWest
            ? "Gravitational Wave cleaves the west half."
            : "Intemperate Will cleaves the east half.";
        return new CallBlock(remainingTime, [new P4CallLine([new(safeSide, P4CallTone.Direction)])], tooltip);
    }

    public static CallBlock BuildRingCall(IReadOnlySet<uint> icons, uint? playerMarkerIcon, float remainingTime)
    {
        var stackSpread = GetRingReality(icons, StackSpreadRealIcon, StackSpreadFakeIcon);
        var ice = GetRingReality(icons, IceRealIcon, IceFakeIcon);
        var lightning = GetRingReality(icons, LightningRealIcon, LightningFakeIcon);
        var segments = new List<P4CallSegment>();

        if (stackSpread is not null)
        {
            if (playerMarkerIcon is StackMarkerIcon or SpreadMarkerIcon)
            {
                // A real ring keeps the marker's meaning; a fake ring flips it.
                var isStack = (playerMarkerIcon == StackMarkerIcon) == (stackSpread == RealityState.Real);
                segments.Add(isStack ? new("STACK", P4CallTone.Stack) : new("SPREAD", P4CallTone.Spread));
            }
            else
            {
                segments.Add(new(stackSpread == RealityState.Real ? "FOLLOW MARKERS" : "OPPOSITE OF MARKERS", P4CallTone.Muted));
            }

            AddFloorSegments(segments, ice, "THE ICE", P4CallTone.Ice);
            AddFloorSegments(segments, lightning, "LIGHTNING", P4CallTone.Lightning);
        }
        else if (ice is not null && lightning is not null)
        {
            if (ice == lightning)
            {
                segments.Add(ice == RealityState.Fake ? new("BE IN", P4CallTone.In) : new("DODGE", P4CallTone.Out));
                segments.Add(new("BOTH"));
            }
            else if (ice == RealityState.Fake)
            {
                segments.Add(new("BE IN", P4CallTone.In));
                segments.Add(new("ICE", P4CallTone.Ice));
            }
            else
            {
                segments.Add(new("BE IN", P4CallTone.In));
                segments.Add(new("LIGHTNING", P4CallTone.Lightning));
            }
        }
        else if (ice is not null || lightning is not null)
        {
            var isIce = ice is not null;
            var reality = ice ?? lightning!.Value;
            segments.Add(reality == RealityState.Fake ? new("BE IN", P4CallTone.In) : new("BE OUT OF", P4CallTone.Out));
            segments.Add(isIce ? new("ICE", P4CallTone.Ice) : new("LIGHTNING", P4CallTone.Lightning));
        }
        else
        {
            segments.Add(new("Unknown Kefka rings", P4CallTone.Muted));
        }

        return new CallBlock(remainingTime, [new P4CallLine(segments)], FormatRingTooltip(stackSpread, ice, lightning, playerMarkerIcon, icons));
    }

    public static CallBlock BuildGazeCall(RealityState reality, float remainingTime)
    {
        var segment = reality == RealityState.Real
            ? new P4CallSegment("LOOK AWAY", P4CallTone.LookAway)
            : new P4CallSegment("LOOK AT IT", P4CallTone.LookAt);
        var tooltip = $"Statue symbol: {(reality == RealityState.Real ? "REAL" : "FAKE")}";
        return new CallBlock(remainingTime, [new P4CallLine([segment])], tooltip);
    }

    private void OnActorControl(uint entityId, uint category, uint[] args)
    {
        if (!plugin.IsInDmu)
        {
            return;
        }

        if (category == GameEventHooks.HeadmarkerCategory)
        {
            OnHeadmarker(entityId, args[0]);
        }
        else if (category == GameEventHooks.TetherCategory)
        {
            OnTether(entityId, args[1], args[2]);
        }
        else if (category == GameEventHooks.ObjectAnimationCategory)
        {
            OnObjectAnimation(entityId, args[0]);
        }
    }

    private void OnHeadmarker(uint entityId, uint icon)
    {
        var now = DateTime.UtcNow;
        var target = Plugin.ObjectTable.SearchByEntityId(entityId);
        if (icon is >= StackSpreadFakeIcon and <= LightningRealIcon && target?.BaseId == KefkaBaseId)
        {
            if (ringSeenAtUtc is not { } ringSeenAt || now - ringSeenAt > MarkerBurstWindow)
            {
                ringIcons.Clear();
                ringSeenAtUtc = now;
            }

            ringIcons.Add(icon);
            lastEventAtUtc = now;
        }
        else if (icon is StackMarkerIcon or SpreadMarkerIcon && target is IPlayerCharacter)
        {
            playerMarkerIcon = icon;
            playerMarkerSeenAtUtc = now;
        }
    }

    private void OnTether(uint sourceEntityId, uint tetherId, uint targetEntityId)
    {
        if (tetherId != StatueTetherId)
        {
            return;
        }

        var statue = Plugin.ObjectTable.SearchByEntityId(sourceEntityId);
        if (statue?.BaseId != GravenImageBaseId
            || Vector2.Distance(new Vector2(statue.Position.X, statue.Position.Z), RockStatuePosition) > RockStatueTolerance
            || Plugin.ObjectTable.SearchByEntityId(targetEntityId) is not IPlayerCharacter player)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (rocksSeenAtUtc is not { } rocksSeenAt || now - rocksSeenAt > MarkerBurstWindow)
        {
            // The second rocks set of a statue sequence comes about 15s after the first and resolves a little later.
            rocksResolveDelay = lastRocksSetAtUtc is { } lastSet && now - lastSet <= RocksSetGap
                ? SecondRocksResolveDelay
                : FirstRocksResolveDelay;
            rockTargets.Clear();
            rocksSeenAtUtc = now;
            lastRocksSetAtUtc = now;
        }

        // Tanks (role 1) and healers (role 4) are supports.
        var role = player.ClassJob.ValueNullable?.Role ?? 0;
        rockTargets.Add(new P1RockTarget(player.Name.TextValue, role is 1 or 4));
        lastEventAtUtc = now;
    }

    private void OnObjectAnimation(uint entityId, uint state)
    {
        var baseId = Plugin.ObjectTable.SearchByEntityId(entityId)?.BaseId;
        if (baseId is WestCleaveWarningBaseId or EastCleaveWarningBaseId)
        {
            if (state == ObjectAnimationShown)
            {
                cleavesWest = baseId == WestCleaveWarningBaseId;
                cleaveSeenAtUtc = DateTime.UtcNow;
                lastEventAtUtc = cleaveSeenAtUtc;
            }

            return;
        }

        if (baseId is not (GazeRealSymbolBaseId or GazeFakeSymbolBaseId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (state == ObjectAnimationShown)
        {
            gazeReality = baseId == GazeRealSymbolBaseId ? RealityState.Real : RealityState.Fake;
            gazeSeenAtUtc = now;
            lastEventAtUtc = now;
        }
        else if (state == ObjectAnimationResolved)
        {
            gazeSeenAtUtc = null;
            lastEventAtUtc = now;
        }
    }

    private static RealityState? GetRingReality(IReadOnlySet<uint> icons, uint realIcon, uint fakeIcon)
    {
        return icons.Contains(realIcon)
            ? RealityState.Real
            : icons.Contains(fakeIcon)
                ? RealityState.Fake
                : null;
    }

    private static void AddFloorSegments(List<P4CallSegment> segments, RealityState? reality, string floorName, P4CallTone floorTone)
    {
        if (reality is null)
        {
            return;
        }

        segments.Add(reality == RealityState.Fake ? new("IN", P4CallTone.In) : new("OUT OF", P4CallTone.Out));
        segments.Add(new(floorName, floorTone));
    }

    private static string FormatRingTooltip(
        RealityState? stackSpread,
        RealityState? ice,
        RealityState? lightning,
        uint? playerMarkerIcon,
        IReadOnlySet<uint> icons)
    {
        var lines = new List<string> { "Kefka's rings" };
        if (stackSpread is not null)
        {
            var marker = playerMarkerIcon switch
            {
                StackMarkerIcon => "stack",
                SpreadMarkerIcon => "spread",
                _ => "not seen",
            };
            lines.Add($"Stack/spread ring: {FormatReality(stackSpread.Value)} (player markers: {marker})");
        }

        if (ice is not null)
        {
            lines.Add($"Ice ring: {FormatReality(ice.Value)}");
        }

        if (lightning is not null)
        {
            lines.Add($"Lightning ring: {FormatReality(lightning.Value)}");
        }

        lines.Add($"Markers: {string.Join(", ", icons.Order())}");
        return string.Join("\n", lines);
    }

    private static string FormatReality(RealityState reality)
    {
        return reality == RealityState.Real ? "REAL" : "FAKE";
    }

    private static float Remaining(DateTime resolveAtUtc, DateTime now)
    {
        return MathF.Max(0.0f, (float)(resolveAtUtc - now).TotalSeconds);
    }
}

public sealed record P1RockTarget(string Name, bool IsSupport);
