namespace DMUP4DebuffHelper;

using System;
using System.Collections.Generic;
using System.Linq;

// Declared in display order within a set.
public enum P4CallKind
{
    GrandCrossSet,
    Shrieks,
    Chaos,
    Flood,
}

public enum P4CallTone
{
    Plain,
    Muted,
    Water,
    Lightning,
    LookAway,
    LookAt,
    Donut,
    Twister,
    Stop,
    Motion,
    Stack,
    Spread,
    In,
    Out,
    Ice,
    Rocks,
    Direction,
    FloodBlue,
    FloodPurple,
}

public enum P4CallPlayerDisplay
{
    FullName,
    FirstName,
    JobIcon,
}

public sealed record P4CallPlayer(string Name, uint ClassJobId);

// A segment either shows Text or, when Players is set, a list of players drawn per the player display setting.
public sealed record P4CallSegment(
    string Text,
    P4CallTone Tone = P4CallTone.Plain,
    uint StatusId = 0,
    IReadOnlyList<P4CallPlayer>? Players = null);

public sealed record P4CallLine(IReadOnlyList<P4CallSegment> Segments);

// One mechanic's call as drawn in a call panel: its lines, the timer shown beside them, and a hover tooltip.
public sealed record CallBlock(float RemainingTime, IReadOnlyList<P4CallLine> Lines, string Tooltip);

public sealed record P4Call(
    P4CallKind Kind,
    int SetNumber,
    float RemainingTime,
    IReadOnlyList<P4CallLine> Lines,
    string Tooltip);

// Remembers when each set resolves so "2nd spread" keeps its number after the 1st set falls off.
public sealed class P4CallSetHistory
{
    private const double SetToleranceSeconds = 2.0;
    private readonly List<(P4CallKind Kind, DateTime ResolveAtUtc)> sets = [];

    public DateTime GetSetResolveTime(P4CallKind kind, DateTime resolveAtUtc)
    {
        foreach (var set in sets)
        {
            if (set.Kind == kind && Math.Abs((set.ResolveAtUtc - resolveAtUtc).TotalSeconds) <= SetToleranceSeconds)
            {
                return set.ResolveAtUtc;
            }
        }

        sets.Add((kind, resolveAtUtc));
        return resolveAtUtc;
    }

    public int GetOrdinal(P4CallKind kind, DateTime setResolveAtUtc)
    {
        return 1 + sets.Count(set => set.Kind == kind && set.ResolveAtUtc < setResolveAtUtc);
    }

    public void Clear()
    {
        sets.Clear();
    }
}

public static class P4Calls
{
    public const uint CompressedWaterStatusId = 5545;
    public const uint ForkedLightningStatusId = 5544;
    public const uint CursedShriekStatusId = 5543;
    public const uint AccelerationBombStatusId = 5546;
    public const uint DynamicFluidStatusId = 5548;
    public const uint EntropyStatusId = 5547;

    public static readonly IReadOnlySet<uint> PartyStatusIds = new HashSet<uint>
    {
        CompressedWaterStatusId,
        ForkedLightningStatusId,
        AccelerationBombStatusId,
        DynamicFluidStatusId,
        EntropyStatusId,
    };

    // The Flood call is personal, so it comes from the local player's assignments rather than the party's.
    public static List<P4Call> Build(
        IEnumerable<P4DebuffAssignment> assignments,
        IEnumerable<P4DebuffAssignment> localAssignments,
        P4CallSetHistory history,
        DateTime now,
        bool showStopOnly)
    {
        var active = assignments
            .Where(assignment => assignment.Entry.RemainingTime > 0.0f)
            .ToList();
        var calls = new List<P4Call>();

        foreach (var set in GroupIntoSets(active, P4CallKind.GrandCrossSet, history, now, CompressedWaterStatusId, ForkedLightningStatusId, AccelerationBombStatusId))
        {
            calls.Add(BuildGrandCrossCall(set.Assignments, history.GetOrdinal(P4CallKind.GrandCrossSet, set.ResolveAtUtc), showStopOnly));
        }

        foreach (var set in GroupIntoSets(active, P4CallKind.Shrieks, history, now, CursedShriekStatusId))
        {
            calls.Add(BuildShriekCall(set.Assignments, history.GetOrdinal(P4CallKind.Shrieks, set.ResolveAtUtc)));
        }

        AddChaosCall(calls, active, history, now, EntropyStatusId, "Inferno");
        AddChaosCall(calls, active, history, now, DynamicFluidStatusId, "Tsunami");
        AddFloodCall(calls, localAssignments);

        return calls
            .OrderBy(call => call.SetNumber)
            .ThenBy(call => call.Kind)
            .ToList();
    }

    private static IEnumerable<(DateTime ResolveAtUtc, List<P4DebuffAssignment> Assignments)> GroupIntoSets(
        IReadOnlyList<P4DebuffAssignment> assignments,
        P4CallKind kind,
        P4CallSetHistory history,
        DateTime now,
        params uint[] statusIds)
    {
        return assignments
            .Where(assignment => statusIds.Contains(assignment.Entry.StatusId))
            .OrderBy(assignment => assignment.Entry.RemainingTime)
            .GroupBy(assignment => history.GetSetResolveTime(kind, now.AddSeconds(assignment.Entry.RemainingTime)))
            .Select(group => (group.Key, group.ToList()));
    }

    private static P4Call BuildGrandCrossCall(IReadOnlyList<P4DebuffAssignment> set, int ordinal, bool showStopOnly)
    {
        var lines = new List<P4CallLine>();
        var setName = FormatOrdinal(ordinal);
        var elements = new[]
            {
                (StatusId: CompressedWaterStatusId, Name: "WATER", Tone: P4CallTone.Water),
                (StatusId: ForkedLightningStatusId, Name: "LIGHTNING", Tone: P4CallTone.Lightning),
            }
            .Select(element => (element.StatusId, element.Name, element.Tone, Reality: GetReality(set, element.StatusId)))
            .Where(element => element.Reality is not null)
            .ToList();
        var knownElements = elements
            .Where(element => element.Reality is not (RealityState.Unknown or MixedReality))
            .ToList();

        if (elements.Any(element => element.Reality == MixedReality))
        {
            lines.Add(new P4CallLine([new($"{setName} spread: mixed tells, check your debuff", P4CallTone.Muted)]));
        }
        else if (elements.Count > 0 && knownElements.Count == 0)
        {
            lines.Add(new P4CallLine([new($"{setName} spread: waiting for tell", P4CallTone.Muted)]));
        }
        else if (knownElements.Count > 0)
        {
            var spreads = knownElements
                .Where(element => IsSpread(element.StatusId, element.Reality!.Value))
                .ToList();
            var shown = spreads.Count > 0 ? spreads : knownElements;
            var segments = new List<P4CallSegment> { new($"{setName} {(spreads.Count > 0 ? "spread" : "stack")} is") };
            for (var index = 0; index < shown.Count; index++)
            {
                if (index > 0)
                {
                    segments.Add(new("+"));
                }

                segments.Add(new(shown[index].Name, shown[index].Tone, shown[index].StatusId));
            }

            lines.Add(new P4CallLine(segments));
        }

        var accels = set
            .Where(assignment => assignment.Entry.StatusId == AccelerationBombStatusId)
            .OrderBy(assignment => assignment.Entry.PartyIndex)
            .ToList();
        if (accels.Count == 0)
        {
            lines.Add(new P4CallLine([new("No accel this set", P4CallTone.Muted, AccelerationBombStatusId)]));
        }
        else
        {
            var accelGroups = accels
                .GroupBy(assignment => assignment.Reality)
                .Where(group => !showStopOnly || group.Key != RealityState.Fake)
                .OrderBy(group => GetAccelOrder(group.Key))
                .ToList();
            if (accelGroups.Count == 0)
            {
                lines.Add(new P4CallLine([new("No STOP this set", P4CallTone.Muted, AccelerationBombStatusId)]));
            }

            foreach (var group in accelGroups)
            {
                var accelCall = group.Key switch
                {
                    RealityState.Real => new P4CallSegment("STOP", P4CallTone.Stop),
                    RealityState.Fake => new P4CallSegment("MOTION", P4CallTone.Motion),
                    _ => new P4CallSegment("waiting", P4CallTone.Muted),
                };
                lines.Add(new P4CallLine(
                [
                    new($"{setName} accel", P4CallTone.Plain, AccelerationBombStatusId),
                    accelCall,
                    new("-", P4CallTone.Muted),
                    new(string.Empty, Players: group
                        .Select(assignment => new P4CallPlayer(assignment.Entry.MemberName, assignment.Entry.ClassJobId))
                        .ToList()),
                ]));
            }
        }

        return new P4Call(P4CallKind.GrandCrossSet, ordinal, set.Min(assignment => assignment.Entry.RemainingTime), lines, FormatTooltip($"{setName} Grand Cross set", set));
    }

    private static P4Call BuildShriekCall(IReadOnlyList<P4DebuffAssignment> set, int ordinal)
    {
        var shrieks = $"{FormatOrdinal(ordinal)} shrieks";
        var line = (GetReality(set, CursedShriekStatusId) ?? RealityState.Unknown) switch
        {
            MixedReality => new P4CallLine([new($"{shrieks}: mixed tells, check your debuff", P4CallTone.Muted, CursedShriekStatusId)]),
            RealityState.Real => new P4CallLine(
            [
                new("LOOK AWAY", P4CallTone.LookAway, CursedShriekStatusId),
                new($"from {shrieks}"),
            ]),
            RealityState.Fake => new P4CallLine(
            [
                new("LOOK AT", P4CallTone.LookAt, CursedShriekStatusId),
                new(shrieks),
            ]),
            _ => new P4CallLine([new($"{shrieks}: waiting for tell", P4CallTone.Muted, CursedShriekStatusId)]),
        };

        return new P4Call(P4CallKind.Shrieks, ordinal, set.Min(assignment => assignment.Entry.RemainingTime), [line], FormatTooltip(shrieks, set));
    }

    private static void AddChaosCall(
        List<P4Call> calls,
        IReadOnlyList<P4DebuffAssignment> assignments,
        P4CallSetHistory history,
        DateTime now,
        uint statusId,
        string name)
    {
        var holders = assignments
            .Where(assignment => assignment.Entry.StatusId == statusId)
            .ToList();
        if (holders.Count == 0)
        {
            return;
        }

        // Inferno and Tsunami are numbered together, so the 1st one to resolve belongs to the 1st set.
        var remainingTime = holders.Min(assignment => assignment.Entry.RemainingTime);
        var setResolveAtUtc = history.GetSetResolveTime(P4CallKind.Chaos, now.AddSeconds(remainingTime));
        var reality = GetReality(holders, statusId) ?? RealityState.Unknown;
        var call = reality == MixedReality
            ? new P4CallSegment("mixed tells, check your debuff", P4CallTone.Muted)
            : reality == RealityState.Unknown
            ? new P4CallSegment("waiting for tell", P4CallTone.Muted)
            : IsDonut(statusId, reality)
                ? new P4CallSegment("DONUT", P4CallTone.Donut)
                : new P4CallSegment("TWISTER", P4CallTone.Twister);

        calls.Add(new P4Call(
            P4CallKind.Chaos,
            history.GetOrdinal(P4CallKind.Chaos, setResolveAtUtc),
            remainingTime,
            [new P4CallLine([new($"{name}:", P4CallTone.Plain, statusId), call])],
            FormatTooltip(name, holders)));
    }

    // Holders whose captured tells disagree; never assigned to a debuff, only returned by GetReality.
    private const RealityState MixedReality = (RealityState)(-1);

    private static void AddFloodCall(List<P4Call> calls, IEnumerable<P4DebuffAssignment> localAssignments)
    {
        // Allagan Field or Beyond Death decides the side; the Wound statuses only feed into it.
        var flood = localAssignments
            .Where(assignment => assignment.Entry.RemainingTime > 0.0f
                && assignment.Rule.Group == P4MechanicGroup.Flood
                && !P4Flood.IsWoundStatus(assignment.Rule.Id))
            .OrderBy(assignment => assignment.Entry.RemainingTime)
            .FirstOrDefault();
        if (flood is null)
        {
            return;
        }

        var line = flood.FloodSide switch
        {
            FloodSide.Blue => new P4CallLine([new("GO"), new("BLUE", P4CallTone.FloodBlue)]),
            FloodSide.Purple => new P4CallLine([new("GO"), new("PURPLE", P4CallTone.FloodPurple)]),
            _ => new P4CallLine([new($"{flood.Rule.Name}: waiting for {(P4Flood.UsesTruthLieTell(flood.Rule.Id) ? "tell" : "Wound")}", P4CallTone.Muted, flood.Rule.Id)]),
        };

        // Set 0 puts the Flood block first: it is short and personal, so it is usually the next thing to resolve.
        calls.Add(new P4Call(P4CallKind.Flood, 0, flood.Entry.RemainingTime, [line], $"{flood.Rule.Name}\n{flood.Instruction}"));
    }

    // Null when nobody in the set has the status, Unknown when no tell was captured yet,
    // and MixedReality when holders were captured with different tells.
    private static RealityState? GetReality(IReadOnlyList<P4DebuffAssignment> set, uint statusId)
    {
        var holders = set
            .Where(assignment => assignment.Entry.StatusId == statusId)
            .ToList();
        if (holders.Count == 0)
        {
            return null;
        }

        var known = holders
            .Select(assignment => assignment.Reality)
            .Where(reality => reality != RealityState.Unknown)
            .Distinct()
            .ToList();
        return known.Count switch
        {
            0 => RealityState.Unknown,
            1 => known[0],
            _ => MixedReality,
        };
    }

    private static int GetAccelOrder(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => 0,
            RealityState.Fake => 1,
            _ => 2,
        };
    }

    private static bool IsSpread(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            CompressedWaterStatusId => reality == RealityState.Fake,
            ForkedLightningStatusId => reality == RealityState.Real,
            _ => false,
        };
    }

    private static bool IsDonut(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            DynamicFluidStatusId => reality == RealityState.Real,
            EntropyStatusId => reality == RealityState.Fake,
            _ => false,
        };
    }

    private static string FormatTooltip(string title, IEnumerable<P4DebuffAssignment> assignments)
    {
        var lines = assignments
            .GroupBy(assignment => assignment.Entry.StatusId)
            .Select(group =>
            {
                var players = string.Join(", ", group
                    .OrderBy(assignment => assignment.Entry.PartyIndex)
                    .Select(assignment => $"{assignment.Entry.MemberName} ({FormatReality(assignment.Reality)})"));
                return $"{group.First().Rule.Name}: {players}";
            });
        return $"{title}\n{string.Join("\n", lines)}";
    }

    private static string FormatReality(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => "Real",
            RealityState.Fake => "Fake",
            _ => "Unknown",
        };
    }

    private static string FormatOrdinal(int ordinal)
    {
        var suffix = (ordinal % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (ordinal % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            },
        };
        return $"{ordinal}{suffix}";
    }
}
