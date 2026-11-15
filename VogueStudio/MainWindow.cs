using System;
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

        if (Posing.Message.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Warn, Posing.Message);
            ImGui.PopTextWrapPos();
        }

        var selected = Joints.Parts.FirstOrDefault(p => p.Bone == Overlay.Selected);
        ImGui.Spacing();
        ImGui.TextColored(Gold, Overlay.Selected == Overlay.WholeBody ? "Selected: Whole body (the diamond at the feet)"
            : selected != null ? "Selected: " + selected.Name : "Click a circle to select a body part.");
        if (Model.Locked.Length > 0) ImGui.TextColored(Gold, "Moving the whole body " + Model.Locked + " (let go to finish)");
        else if (Posing.Locked.Length > 0) ImGui.TextColored(Gold, "Moving " + Posing.Locked + (Posing.AtStop ? " - reached its limit" : " (let go to finish)"));
        else
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Dim, "Drag a circle up/down or sideways. Mouse wheel on a circle moves it the hard-to-drag way. "
                + "The diamond at the feet moves the whole body (wheel turns them).");
            ImGui.PopTextWrapPos();
        }

        // Undo / Redo / Reset, always in reach.
        var posed = Joints.Target();
        if (posed != null && Plugin.ClientState.IsGPosing)
        {
            if (!Posing.CanUndo) ImGui.BeginDisabled();
            if (ImGui.Button("Undo")) Posing.Undo(posed);
            if (!Posing.CanUndo) ImGui.EndDisabled();
            ImGui.SameLine();
            if (!Posing.CanRedo) ImGui.BeginDisabled();
            if (ImGui.Button("Redo")) Posing.Redo(posed);
            if (!Posing.CanRedo) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Reset pose")) Posing.Reset(posed);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Puts the character back to their normal pose (you can Undo this).");
            ImGui.SameLine();
            if (ImGui.Button("Reset place"))
            {
                if (Model.Get(posed) is { } place) Posing.RememberPlace(place);
                Model.Reset(posed);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Puts the whole character back where they were standing, facing the original way (you can Undo this).");
        }
        ImGui.Separator();

        // --- Body parts, big and easy to click ---
        if (ImGui.BeginChild("##parts", new Vector2(0, -190)))
        {
            if (ImGui.Selectable("   Whole body (move and turn)##whole", Overlay.Selected == Overlay.WholeBody, ImGuiSelectableFlags.None, new Vector2(0, ImGui.GetTextLineHeight() * 1.4f)))
                Overlay.Selected = Overlay.WholeBody;
            string group = "";
            foreach (var part in Joints.Shown())
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
        var simple = cfg.SimpleMode;
        if (ImGui.Checkbox("Simple mode (only the main joints)", ref simple)) { cfg.SimpleMode = simple; cfg.Save(); }
        var labels = cfg.ShowLabels;
        if (ImGui.Checkbox("Show every body part's name", ref labels)) { cfg.ShowLabels = labels; cfg.Save(); }
        var size = cfg.CircleSize;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderFloat("Circle size", ref size, 8f, 40f, "%.0f")) cfg.CircleSize = size;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
        var flexNames = new[] { "Normal", "Flexible", "No limits" };
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("Flexibility", flexNames[Math.Clamp(cfg.Flexibility, 0, 2)]))
        {
            for (var i = 0; i < 3; i++)
                if (ImGui.Selectable(flexNames[i], cfg.Flexibility == i)) { cfg.Flexibility = i; cfg.Save(); }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("How far joints can bend before they stop.\nNormal: like a person. Flexible: about a third more. No limits: anything goes.");
        var strength = cfg.DragStrength;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderFloat("Drag strength", ref strength, 0.2f, 1.5f, "%.1fx")) cfg.DragStrength = strength;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Lower = finer, slower bending. 1 = the part follows your mouse.");
    }
}
