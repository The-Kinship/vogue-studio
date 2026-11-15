using System;
using Dalamud.Configuration;

namespace VogueStudio;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>Only the main joints (leaves out the extra back and shoulder joints, which crowd the chest).</summary>
    public bool SimpleMode { get; set; } = true;
    /// <summary>How strongly a drag bends the joint (1 = follows the mouse).</summary>
    public float DragStrength { get; set; } = 1f;
    /// <summary>Joint stops: 0 = normal, 1 = flexible (about a third more), 2 = no limits.</summary>
    public int Flexibility { get; set; }

    /// <summary>Size of the joint circles on screen (radius in pixels).</summary>
    public float CircleSize { get; set; } = 16f;
    /// <summary>Show the body part's name next to each circle.</summary>
    public bool ShowLabels { get; set; }
    /// <summary>Show the circles at all (you can hide them while taking the picture).</summary>
    public bool ShowCircles { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
