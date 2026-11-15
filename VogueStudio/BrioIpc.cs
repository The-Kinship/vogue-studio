using System;
using Dalamud.Game.ClientState.Objects.Types;

namespace VogueStudio;

/// <summary>
/// Talks to Brio (version 3) through its official plugin connection:
/// get a character's pose, load an edited pose back, and reset.
/// </summary>
public static class BrioIpc
{
    /// <summary>The last problem talking to Brio ("" if none).</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Brio's version ("" if Brio isn't answering).</summary>
    public static string Version()
    {
        try
        {
            var (major, minor) = Plugin.PluginInterface.GetIpcSubscriber<ValueTuple<int, int>>("Brio.ApiVersion").InvokeFunc();
            return $"{major}.{minor}";
        }
        catch (Exception) { return ""; }
    }

    /// <summary>The character's whole pose as Brio's pose text (null if Brio can't give it, e.g. not in GPose).</summary>
    public static string? GetPose(IGameObject actor)
    {
        try
        {
            var json = Plugin.PluginInterface.GetIpcSubscriber<IGameObject, string?>("Brio.GetPoseAsJson.V3").InvokeFunc(actor);
            LastError = json == null ? "Brio couldn't give the pose (is this character in Brio's list?)." : "";
            return json;
        }
        catch (Exception ex) { return Fail<string?>(ex, null); }
    }

    /// <summary>Puts a pose (Brio's pose text) on the character.</summary>
    public static bool LoadPose(IGameObject actor, string json)
    {
        try
        {
            var ok = Plugin.PluginInterface.GetIpcSubscriber<IGameObject, bool, string, bool>("Brio.LoadPoseFromJson.V3")
                .InvokeFunc(actor, false, json);
            LastError = ok ? "" : "Brio didn't accept the pose.";
            return ok;
        }
        catch (Exception ex) { return Fail(ex, false); }
    }

    /// <summary>Back to the character's normal pose.</summary>
    public static bool ResetPose(IGameObject actor)
    {
        try { return Plugin.PluginInterface.GetIpcSubscriber<IGameObject, bool, bool>("Brio.ResetPose.V3").InvokeFunc(actor, false); }
        catch (Exception ex) { return Fail(ex, false); }
    }

    private static T Fail<T>(Exception ex, T value)
    {
        Plugin.Log.Warning(ex, "Brio call failed.");
        var inner = ex.InnerException ?? ex;
        LastError = $"Brio problem: {inner.GetType().Name}: {inner.Message}";
        return value;
    }
}
