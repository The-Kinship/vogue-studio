using System;

namespace VogueStudio;

/// <summary>Talks to Brio through its official plugin connection. (Piece 1 only checks that Brio is there.)</summary>
public static class BrioIpc
{
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
}
