namespace DMUP4DebuffHelper;

using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using System;

// Hooks game events that have no Dalamud service (headmarkers, tethers, object animations, arena effects)
// and re-raises them so trackers and the discovery log can share one set of hooks.
public sealed class GameEventHooks : IDisposable
{
    public const uint HeadmarkerCategory = 34;
    public const uint TetherCategory = 35;
    public const uint ObjectAnimationCategory = 413;

    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? actorControlHook;
    private readonly unsafe Hook<ContentDirector.Delegates.ApplyMapEffect>? mapEffectHook;

    public unsafe GameEventHooks()
    {
        try
        {
            actorControlHook = Plugin.GameInteropProvider.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
                PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket,
                OnActorControl);
            actorControlHook.Enable();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not hook actor control packets; headmarkers, tethers and P1 calls are unavailable.");
        }

        try
        {
            mapEffectHook = Plugin.GameInteropProvider.HookFromAddress<ContentDirector.Delegates.ApplyMapEffect>(
                ContentDirector.MemberFunctionPointers.ApplyMapEffect,
                OnApplyMapEffect);
            mapEffectHook.Enable();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not hook map effects; arena effects are unavailable.");
        }
    }

    public delegate void ActorControlHandler(uint entityId, uint category, uint[] args);

    public delegate void MapEffectHandler(uint index, ushort state, ushort flags);

    public event ActorControlHandler? ActorControlReceived;

    public event MapEffectHandler? MapEffectApplied;

    public void Dispose()
    {
        actorControlHook?.Dispose();
        mapEffectHook?.Dispose();
    }

    private void OnActorControl(
        uint entityId,
        uint category,
        uint arg1,
        uint arg2,
        uint arg3,
        uint arg4,
        uint arg5,
        uint arg6,
        uint arg7,
        uint arg8,
        GameObjectId targetId,
        bool isRecorded)
    {
        try
        {
            ActorControlReceived?.Invoke(entityId, category, [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8]);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Actor control handler failed for category {Category}.", category);
        }

        actorControlHook!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
    }

    private unsafe void OnApplyMapEffect(ContentDirector* director, uint index, ushort state, ushort flags)
    {
        try
        {
            MapEffectApplied?.Invoke(index, state, flags);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Map effect handler failed for index {Index}.", index);
        }

        mapEffectHook!.Original(director, index, state, flags);
    }
}
