using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VogueStudio;

/// <summary>The Vogue Studio window: what's going on, the body part list, and settings.</summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.6f, 0.4f, 1f);

    public MainWindow() : base("Rafter Cat's Vogue Studio##VogueMain")
    {
        Size = new Vector2(330, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = Plugin.Config;

        // --- Status, in words ---
        var brio = BrioIpc.Version();
        ImGui.TextColored(brio.Length > 0 ? Dim : Warn, brio.Length > 0 ? $"Brio found (version {brio})." : "Brio isn't answering. Is it installed and turned on?");
        if (Overlay.Problem.Length > 0) ImGui.TextColored(Warn, Overlay.Problem);
        else
        {
            var actor = Joints.Target();
            ImGui.TextColored(Dim, "Posing: " + (actor?.Name.ToString() ?? "?"));
        }

        var selected = Joints.Parts.FirstOrDefault(p => p.Bone == Overlay.Selected);
        ImGui.Spacing();
        ImGui.TextColored(Gold, selected != null ? "Selected: " + selected.Name : "Click a circle (or a name below) to select a body part.");
        ImGui.TextColored(Dim, "(Dragging to move parts comes in the next piece.)");
        ImGui.Separator();

        // --- Body parts, big and easy to click ---
        if (ImGui.BeginChild("##parts", new Vector2(0, -110)))
        {
            string group = "";
            foreach (var part in Joints.Parts)
            {
                if (part.Group != group)
                {
                    group = part.Group;
                    ImGui.Spacing();
                    ImGui.TextColored(Dim, group);
                }
                var isSel = Overlay.Selected == part.Bone;
                if (ImGui.Selectable("   " + part.Name + "##" + part.Bone, isSel, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight() * 1.4f)))
                    Overlay.Selected = isSel ? "" : part.Bone;
            }
        }
        ImGui.EndChild();

        // --- Settings ---
        ImGui.Separator();
        var show = cfg.ShowCircles;
        if (ImGui.Checkbox("Show circles (hide them for the picture)", ref show)) { cfg.ShowCircles = show; cfg.Save(); }
        var labels = cfg.ShowLabels;
        if (ImGui.Checkbox("Show every body part's name", ref labels)) { cfg.ShowLabels = labels; cfg.Save(); }
        var size = cfg.CircleSize;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderFloat("Circle size", ref size, 8f, 40f, "%.0f")) cfg.CircleSize = size;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }
}
