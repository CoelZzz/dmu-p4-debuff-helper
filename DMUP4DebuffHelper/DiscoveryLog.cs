namespace DMUP4DebuffHelper;

using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;
using System.Linq;

// Research log for mapping unknown mechanics: writes every non-player status, NPC cast and NPC action,
// plus headmarkers, tethers and arena effects seen in DMU to /xllog.
public sealed class DiscoveryLog : IDisposable
{
    private const uint InvalidEntityId = 0xE0000000;
    private const uint DamageOverTimeTickCategory = 23;
    private static readonly TimeSpan ActionDedupeWindow = TimeSpan.FromSeconds(0.5);

    private readonly Plugin plugin;
    private readonly Dictionary<uint, Dictionary<string, TrackedStatus>> statusesByObject = new();
    private readonly Dictionary<uint, uint> castsByObject = new();
    private readonly Dictionary<(uint CasterId, uint ActionId), DateTime> recentActions = new();
    private readonly Dictionary<string, DateTime> recentActorControls = new(StringComparer.Ordinal);
    private readonly HashSet<uint> playerCategoriesSeen = [];
    private readonly Dictionary<uint, string> actionNameCache = new();
    private int pullNumber;
    private DateTime? pullStartedAtUtc;
    private bool wasInCombat;
    private readonly GameEventHooks hooks;

    public DiscoveryLog(Plugin plugin, GameEventHooks hooks)
    {
        this.plugin = plugin;
        this.hooks = hooks;
        hooks.ActorControlReceived += OnActorControl;
        hooks.MapEffectApplied += OnMapEffect;
    }

    private bool IsLogging => plugin.Configuration.DiscoveryLogging && plugin.IsInDmu;

    public void Dispose()
    {
        hooks.ActorControlReceived -= OnActorControl;
        hooks.MapEffectApplied -= OnMapEffect;
    }

    private void OnActorControl(uint entityId, uint category, uint[] args)
    {
        if (IsLogging)
        {
            LogActorControl(entityId, category, args);
        }
    }

    private void OnMapEffect(uint index, ushort state, ushort flags)
    {
        if (IsLogging)
        {
            Write($"MAPEFFECT index {index} state {state} flags {flags}");
        }
    }

    private void LogActorControl(uint entityId, uint category, uint[] args)
    {
        var entity = Plugin.ObjectTable.SearchByEntityId(entityId);
        var who = DescribeObject(entity, entityId);
        switch (category)
        {
            case GameEventHooks.HeadmarkerCategory:
                Write($"HEADMARKER {who} icon {args[0]} ({DescribeArgs(args)})");
                return;
            case GameEventHooks.TetherCategory:
                Write($"TETHER {who} ({DescribeArgs(args)})");
                return;
            case DamageOverTimeTickCategory:
                return;
        }

        // Players get a constant stream of routine actor controls, so only the first of each category per pull is logged.
        if (entity is IPlayerCharacter)
        {
            if (playerCategoriesSeen.Add(category))
            {
                Write($"ACTORCONTROL {who} category {category} ({DescribeArgs(args)}) [first for players this pull]");
            }

            return;
        }

        var argsText = DescribeArgs(args);
        var key = $"{entityId}:{category}:{argsText}";
        var now = DateTime.UtcNow;
        if (recentActorControls.TryGetValue(key, out var lastSeen) && now - lastSeen < ActionDedupeWindow)
        {
            return;
        }

        recentActorControls[key] = now;
        Write($"ACTORCONTROL {who} category {category} ({argsText})");
    }

    // Lists the non-zero args, naming any that match an actor's entity ID (tether targets usually do).
    private static string DescribeArgs(uint[] args)
    {
        var parts = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == 0)
            {
                continue;
            }

            var part = $"a{index + 1}={args[index]}";
            if (args[index] >= 0x10000000 && args[index] != InvalidEntityId
                && Plugin.ObjectTable.SearchByEntityId(args[index]) is { } match)
            {
                part += $" ({DescribeObject(match, args[index])})";
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? "no args" : string.Join(", ", parts);
    }

    public void Update()
    {
        UpdatePullBoundaries();
        var seenObjects = new HashSet<uint>();
        foreach (var gameObject in Plugin.ObjectTable)
        {
            if (gameObject is not IBattleChara character
                || !gameObject.IsValid()
                || character.EntityId is 0 or InvalidEntityId)
            {
                continue;
            }

            seenObjects.Add(character.EntityId);
            UpdateStatuses(character);
            if (character is not IPlayerCharacter)
            {
                UpdateCast(character);
            }
        }

        foreach (var entityId in statusesByObject.Keys.Where(entityId => !seenObjects.Contains(entityId)).ToList())
        {
            statusesByObject.Remove(entityId);
        }

        foreach (var entityId in castsByObject.Keys.Where(entityId => !seenObjects.Contains(entityId)).ToList())
        {
            castsByObject.Remove(entityId);
        }
    }

    public void LogActionEffect(uint casterEntityId, uint actionId, IReadOnlyList<uint> targetIds)
    {
        var caster = Plugin.ObjectTable.SearchByEntityId(casterEntityId);
        if (caster is IPlayerCharacter)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = (casterEntityId, actionId);
        if (recentActions.TryGetValue(key, out var lastSeen) && now - lastSeen < ActionDedupeWindow)
        {
            return;
        }

        recentActions[key] = now;
        var targets = targetIds.Count == 0
            ? "no targets"
            : string.Join(", ", targetIds.Select(targetId => DescribeObject(Plugin.ObjectTable.SearchByEntityId(targetId), targetId)));
        Write($"ACTION {DescribeObject(caster, casterEntityId)} used {GetActionName(actionId)} ({actionId}) on {targetIds.Count}: {targets}");
    }

    // Numbers pulls by party combat state so every line can be tied to a pull; any player in combat
    // keeps the pull going, so the local player dying mid-pull does not split it.
    private void UpdatePullBoundaries()
    {
        var inCombat = Plugin.ObjectTable
            .OfType<IPlayerCharacter>()
            .Any(player => player.StatusFlags.HasFlag(StatusFlags.InCombat));
        if (inCombat == wasInCombat)
        {
            return;
        }

        wasInCombat = inCombat;
        if (inCombat)
        {
            pullNumber++;
            pullStartedAtUtc = DateTime.UtcNow;
            Write($"==== Pull {pullNumber} started ====");
        }
        else
        {
            Write($"==== Pull {pullNumber} ended ====");
        }
    }

    public void Reset(string reason)
    {
        statusesByObject.Clear();
        castsByObject.Clear();
        recentActions.Clear();
        recentActorControls.Clear();
        playerCategoriesSeen.Clear();
        Write($"---- {reason} ----");
    }

    private void UpdateStatuses(IBattleChara character)
    {
        if (!statusesByObject.TryGetValue(character.EntityId, out var previous))
        {
            previous = new Dictionary<string, TrackedStatus>(StringComparer.Ordinal);
            statusesByObject[character.EntityId] = previous;
        }

        var current = new Dictionary<string, TrackedStatus>(StringComparer.Ordinal);
        foreach (var status in character.StatusList)
        {
            if (status.StatusId == 0)
            {
                continue;
            }

            var tracked = new TrackedStatus(status.StatusId, status.Param, status.SourceId);
            var key = $"{tracked.StatusId}:{tracked.Param}:{tracked.SourceId}";
            current[key] = tracked;
            if (previous.ContainsKey(key) || IsFromPlayer(status.SourceId))
            {
                continue;
            }

            Write($"GAIN {DescribeObject(character, character.EntityId)} {plugin.GetStatusName(status.StatusId)} ({status.StatusId}) param {status.Param}, {status.RemainingTime:0.0}s, from {DescribeSource(status.SourceId)}");
        }

        foreach (var (key, tracked) in previous)
        {
            if (!current.ContainsKey(key) && !IsFromPlayer(tracked.SourceId))
            {
                Write($"LOSE {DescribeObject(character, character.EntityId)} {plugin.GetStatusName(tracked.StatusId)} ({tracked.StatusId}) param {tracked.Param}");
            }
        }

        statusesByObject[character.EntityId] = current;
    }

    private void UpdateCast(IBattleChara character)
    {
        if (!character.IsCasting)
        {
            castsByObject.Remove(character.EntityId);
            return;
        }

        if (castsByObject.TryGetValue(character.EntityId, out var actionId) && actionId == character.CastActionId)
        {
            return;
        }

        castsByObject[character.EntityId] = character.CastActionId;
        var target = Plugin.ObjectTable.SearchByEntityId((uint)character.CastTargetObjectId);
        Write($"CAST {DescribeObject(character, character.EntityId)} {GetActionName(character.CastActionId)} ({character.CastActionId}) {character.TotalCastTime:0.0}s at {DescribeObject(target, (uint)character.CastTargetObjectId)}");
    }

    private static bool IsFromPlayer(uint sourceId)
    {
        return Plugin.ObjectTable.SearchByEntityId(sourceId) is IPlayerCharacter;
    }

    private static string DescribeSource(uint sourceId)
    {
        return sourceId is 0 or InvalidEntityId
            ? "none"
            : DescribeObject(Plugin.ObjectTable.SearchByEntityId(sourceId), sourceId);
    }

    private static string DescribeObject(Dalamud.Game.ClientState.Objects.Types.IGameObject? gameObject, uint entityId)
    {
        if (gameObject is null)
        {
            return entityId is 0 or InvalidEntityId ? "nobody" : $"[unknown {entityId:X}]";
        }

        var name = gameObject.Name.TextValue;
        if (gameObject is IPlayerCharacter)
        {
            return name;
        }

        // NPC position (x, z) and facing let statue cleaves and drop spots be mapped to arena directions.
        var position = gameObject.Position;
        return $"{(string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name)} [base {gameObject.BaseId}, id {entityId:X}, at {position.X:0.0},{position.Z:0.0} facing {FormatFacing(gameObject.Rotation)}]";
    }

    // Game rotation 0 faces south (+Z) and increases toward east (+X).
    private static string FormatFacing(float rotation)
    {
        var degreesFromNorth = ((180.0 - rotation * 180.0 / Math.PI) % 360.0 + 360.0) % 360.0;
        string[] compass = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return $"{compass[(int)Math.Round(degreesFromNorth / 45.0) % 8]} ({degreesFromNorth:0}deg)";
    }

    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var actionName = $"Action {actionId}";
        try
        {
            var sheetName = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(actionId)?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                actionName = sheetName;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not load action name for {ActionId}.", actionId);
        }

        actionNameCache[actionId] = actionName;
        return actionName;
    }

    private void Write(string message)
    {
        var pullLabel = pullStartedAtUtc is { } startedAt
            ? $"pull {pullNumber} t+{(DateTime.UtcNow - startedAt).TotalSeconds:0.0}s"
            : "before first pull";
        if (!wasInCombat && pullStartedAtUtc is not null)
        {
            pullLabel += " (out of combat)";
        }

        Plugin.Log.Information("[DMU discovery] {PullLabel} {Message}", pullLabel, message);
    }

    private sealed record TrackedStatus(uint StatusId, ushort Param, uint SourceId);
}
