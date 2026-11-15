using System;
using Dalamud.Configuration;

namespace VogueStudio;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Size of the joint circles on screen (radius in pixels).</summary>
    public float CircleSize { get; set; } = 16f;
    /// <summary>Show the body part's name next to each circle.</summary>
    public bool ShowLabels { get; set; } = true;
    /// <summary>Show the circles at all (you can hide them while taking the picture).</summary>
    public bool ShowCircles { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
