namespace DMUP4DebuffHelper;

using System;
using System.Collections.Generic;

public enum DmuHelperDisplayMode
{
    Empty,
    Preview,
    P1Calls,
    P3BlackHole,
    P4Debuffs,
}

public enum P4Boss
{
    Unknown,
    Kefka,
    NeoExdeath,
    Chaos,
}

public enum P4MechanicGroup
{
    Raw,
    GrandCross,
    Chaos,
    Flood,
}

public enum RealityState
{
    Unknown,
    Real,
    Fake,
}

public enum WoundColor
{
    None,
    Black,
    White,
}

public enum FloodSide
{
    None,
    Blue,
    Purple,
}

internal static class P4Flood
{
    public const uint AllaganFieldStatusId = 454;
    public const uint WhiteWound1StatusId = 4887;
    public const uint BlackWound1StatusId = 4888;
    public const uint WhiteWound2StatusId = 5541;
    public const uint BlackWound2StatusId = 5542;
    public const uint BeyondDeath1StatusId = 5464;
    public const uint BeyondDeath2StatusId = 1382;

    public static FloodSide GetWoundSide(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => FloodSide.Blue,
            WoundColor.White => FloodSide.Purple,
            _ => FloodSide.None,
        };
    }

    public static FloodSide GetOppositeSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => FloodSide.Purple,
            FloodSide.Purple => FloodSide.Blue,
            _ => FloodSide.None,
        };
    }

    public static WoundColor GetOppositeWound(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => WoundColor.White,
            WoundColor.White => WoundColor.Black,
            _ => WoundColor.None,
        };
    }

    public static WoundColor GetWoundColorForSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => WoundColor.Black,
            FloodSide.Purple => WoundColor.White,
            _ => WoundColor.None,
        };
    }

    public static WoundColor GetStatusWoundColor(uint statusId)
    {
        return statusId switch
        {
            WhiteWound1StatusId or WhiteWound2StatusId => WoundColor.White,
            BlackWound1StatusId or BlackWound2StatusId => WoundColor.Black,
            _ => WoundColor.None,
        };
    }

    public static WoundColor GetEffectiveWoundColor(uint statusId)
    {
        return statusId switch
        {
            WhiteWound1StatusId or BlackWound2StatusId => WoundColor.Black,
            WhiteWound2StatusId or BlackWound1StatusId => WoundColor.White,
            _ => WoundColor.None,
        };
    }

    public static bool IsWoundStatus(uint statusId)
    {
        return GetEffectiveWoundColor(statusId) != WoundColor.None;
    }

    public static bool UsesTruthLieTell(uint statusId)
    {
        return statusId == AllaganFieldStatusId;
    }

    public static FloodSide ResolveSide(uint statusId, WoundColor woundColor, RealityState reality = RealityState.Unknown)
    {
        var destinationWound = ResolveDestinationWound(statusId, woundColor, reality);
        if (destinationWound == WoundColor.None)
        {
            return FloodSide.None;
        }

        return GetWoundSide(destinationWound);
    }

    public static bool? MustDieToResolve(uint statusId, RealityState reality = RealityState.Unknown)
    {
        return statusId switch
        {
            AllaganFieldStatusId when reality == RealityState.Real => false,
            AllaganFieldStatusId when reality == RealityState.Fake => true,
            BeyondDeath1StatusId => false,
            BeyondDeath2StatusId => true,
            _ => null,
        };
    }

    public static WoundColor ResolveDestinationWound(uint statusId, WoundColor woundColor, RealityState reality = RealityState.Unknown)
    {
        if (woundColor == WoundColor.None)
        {
            return WoundColor.None;
        }

        return MustDieToResolve(statusId, reality) switch
        {
            true => woundColor,
            false => GetOppositeWound(woundColor),
            _ => WoundColor.None,
        };
    }

    public static string FormatSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => "Blue",
            FloodSide.Purple => "Purple",
            _ => "Unknown",
        };
    }

    public static string FormatWound(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => "Black",
            WoundColor.White => "White",
            _ => "Unknown",
        };
    }

    public static string FormatWoundDebuff(WoundColor woundColor)
    {
        return woundColor == WoundColor.None
            ? "Wound"
            : $"{FormatWound(woundColor)} Wound";
    }

    public static string FormatInstruction(uint statusId, WoundColor woundColor, FloodSide floodSide)
    {
        return FormatInstruction(statusId, woundColor, floodSide, RealityState.Unknown);
    }

    public static string FormatInstruction(uint statusId, WoundColor woundColor, FloodSide floodSide, RealityState reality)
    {
        var destinationWound = ResolveDestinationWound(statusId, woundColor, reality);
        if (destinationWound == WoundColor.None)
        {
            destinationWound = GetWoundColorForSide(floodSide);
        }

        var destinationText = destinationWound != WoundColor.None
            ? FormatWound(destinationWound).ToLowerInvariant()
            : null;

        return statusId switch
        {
            AllaganFieldStatusId when destinationText is not null => $"Allagan Field: go {destinationText}.",
            AllaganFieldStatusId => "Allagan Field: waiting for the truth/lie tell.",
            BeyondDeath1StatusId or BeyondDeath2StatusId when destinationText is not null => $"Beyond Death: go {destinationText}.",
            BeyondDeath1StatusId or BeyondDeath2StatusId => "Beyond Death: waiting for Wound.",
            WhiteWound1StatusId or WhiteWound2StatusId => "White Wound.",
            BlackWound1StatusId or BlackWound2StatusId => "Black Wound.",
            _ => "Tracked Flood debuff.",
        };
    }
}

public sealed record WatchedStatus(
    uint Id,
    string Name,
    P4Boss SourceBoss,
    P4MechanicGroup Group,
    int SortOrder);

public sealed record BossTellSnapshot(
    P4Boss Boss,
    P4MechanicGroup Group,
    uint StatusId,
    ushort Param,
    RealityState Reality,
    DateTime SeenAtUtc);

public sealed record PartyMemberSnapshot(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    ulong ContentId,
    uint EntityId);

public sealed record PartyStatusEntry(
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint StatusId,
    string StatusName,
    uint IconId,
    float RemainingTime,
    bool IsWatched,
    int SortOrder,
    DateTime SeenAtUtc,
    uint ClassJobId = 0);

public sealed record P4DebuffAssignment(
    PartyStatusEntry Entry,
    WatchedStatus Rule,
    RealityState Reality,
    ushort? TellParam,
    string Instruction,
    WoundColor WoundColor = WoundColor.None,
    FloodSide FloodSide = FloodSide.None);

public sealed record CapturedDebuffState(
    RealityState Reality,
    ushort? TellParam,
    DateTime CapturedAtUtc);

public sealed record CapturedFloodWoundState(
    WoundColor EffectiveWoundColor,
    DateTime CapturedAtUtc);

public sealed record P4DebuffRecord(
    DateTime SeenAtUtc,
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint StatusId,
    string StatusName,
    P4Boss SourceBoss,
    P4MechanicGroup Group,
    RealityState Reality,
    ushort? TellParam,
    float RemainingTimeAtCapture,
    string Instruction,
    WoundColor WoundColor = WoundColor.None,
    FloodSide FloodSide = FloodSide.None);

public sealed record P4PullSnapshot(
    DateTime CapturedAtUtc,
    string Reason,
    float CombatElapsedSeconds,
    IReadOnlyList<P4DebuffRecord> Debuffs,
    IReadOnlyList<BossTellSnapshot> BossTells);
