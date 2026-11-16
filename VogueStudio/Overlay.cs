using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace VogueStudio;

/// <summary>
/// The big joint circles drawn over your character in GPose. Each circle is its own tiny window,
/// so clicking it doesn't also click the game (and the camera still works everywhere else).
/// </summary>
public static class Overlay
{
    /// <summary>The selected body part's bone ("" = none).</summary>
    public static string Selected { get; set; } = "";
    /// <summary>The body part under the mouse this frame ("" = none).</summary>
    public static string Hovered { get; private set; } = "";
    /// <summary>Where the mouse was pressed on a circle (a drag starts once it moves a few pixels).</summary>
    private static Vector2 pressAt;
    private static string pressedBone = "";

    /// <summary>A short line about why nothing is showing ("" when circles are up).</summary>
    public static string Problem { get; private set; } = "";

    private static readonly Vector4 Gold = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 0.95f);

    public const string WholeBody = "#whole";

    /// <summary>Detail mode: the hand or foot whose fingers/toes are shown ("" = normal body circles).</summary>
    public static string DetailRoot { get; private set; } = "";
    private static List<BodyPart> detailParts = new();

    /// <summary>The "Ears & tail" detail mode.</summary>
    public const string Extras = "#extras";

    public static void EnterDetail(Dalamud.Game.ClientState.Objects.Types.IGameObject actor, string root)
    {
        detailParts = root == Extras ? Joints.ExtraParts(actor) : Joints.DetailParts(actor, root);
        DetailRoot = root;
        Selected = root == Extras ? "" : root;
    }

    public static void LeaveDetail()
    {
        Selected = DetailRoot == Extras ? "" : DetailRoot;
        DetailRoot = "";
        detailParts = new();
    }

    /// <summary>The circles to show: the fingers/toes in Detail mode, otherwise the body parts.</summary>
    public static IEnumerable<BodyPart> Current() => DetailRoot.Length > 0 ? detailParts : Joints.Shown();

    /// <summary>The "Whole body" handle: a diamond at the character's feet. Drag to move them, mouse wheel to turn them.</summary>
    private static void DrawWholeBody(Dalamud.Game.ClientState.Objects.Types.IGameObject actor, List<(Vector2 Pos, string Text, bool Strong)> labels,
        float r, uint dark, uint gold, uint white, uint fill)
    {
        if (!Plugin.GameGui.WorldToScreen(actor.Position, out var screen)) return;
        var size = new Vector2(r * 3 + 6, r * 3 + 6);
        ImGui.SetNextWindowPos(screen - size / 2);
        ImGui.SetNextWindowSize(size);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (ImGui.Begin("##vogue_whole", flags))
        {
            ImGui.InvisibleButton("hit", size);
            var hovered = ImGui.IsItemHovered();
            var mouse = ImGui.GetMousePos();
            if (ImGui.IsItemActivated()) { Selected = WholeBody; pressAt = mouse; pressedBone = WholeBody; }
            if (ImGui.IsItemActive() && pressedBone == WholeBody)
            {
                if (!Model.Dragging && Vector2.Distance(mouse, pressAt) > 4f) Model.Start(actor, pressAt);
                if (Model.Dragging) Model.Update(mouse);
            }
            if (ImGui.IsItemDeactivated() && pressedBone == WholeBody)
            {
                if (Model.Dragging) Model.End(mouse);
                pressedBone = "";
            }
            var wheel = ImGui.GetIO().MouseWheel;
            if (hovered && wheel != 0 && !Model.Dragging) { Selected = WholeBody; Model.Turn(actor, wheel); }

            var selected = Selected == WholeBody;
            var d = r * 1.4f;
            var pts = new[] { screen + new Vector2(0, -d), screen + new Vector2(d, 0), screen + new Vector2(0, d), screen + new Vector2(-d, 0) };
            var draw = ImGui.GetWindowDrawList();
            for (var i = 0; i < 4; i++) draw.AddLine(pts[i], pts[(i + 1) % 4], dark, selected ? 7f : 5f);
            if (selected || hovered) draw.AddQuadFilled(pts[0], pts[1], pts[2], pts[3], fill);
            for (var i = 0; i < 4; i++) draw.AddLine(pts[i], pts[(i + 1) % 4], selected ? gold : white, selected ? 4f : 2.5f);
            if (selected || hovered || Plugin.Config.ShowLabels)
                labels.Add((screen + new Vector2(d + 6, -ImGui.GetTextLineHeight() / 2), "Whole body", selected || hovered));
        }
        ImGui.End();
    }

    public static void Draw()
    {
        Hovered = "";
        var cfg = Plugin.Config;
        if (!Plugin.ClientState.IsGPosing) { Problem = "Not in GPose."; return; }
        var actor = Joints.Target();
        if (actor == null) { Problem = "No character found to pose."; return; }

        Dictionary<string, Vector3> world;
        try { world = Joints.WorldPositions(actor, Current()); }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Couldn't read the skeleton."); Problem = "Couldn't read the character's skeleton."; return; }
        if (world.Count == 0) { Problem = "This character's joints couldn't be found."; return; }
        Problem = "";
        if (!cfg.ShowCircles) return;

        var r = DetailRoot.Length > 0 ? MathF.Max(6f, cfg.CircleSize * 0.55f) : cfg.CircleSize;   // smaller circles for fingers and toes
        var dark = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.8f));
        var gold = ImGui.ColorConvertFloat4ToU32(Gold);
        var white = ImGui.ColorConvertFloat4ToU32(White);
        var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.25f, 0.35f));
        var labels = new List<(Vector2 Pos, string Text, bool Strong)>();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(2, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        try
        {
            foreach (var part in Current())
            {
                if (!world.TryGetValue(part.Bone, out var pos)) continue;
                if (!Plugin.GameGui.WorldToScreen(pos, out var screen)) continue;

                var size = new Vector2(r * 2 + 6, r * 2 + 6);
                ImGui.SetNextWindowPos(screen - size / 2);
                ImGui.SetNextWindowSize(size);
                const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
                if (ImGui.Begin("##vogue_" + part.Bone, flags))
                {
                    ImGui.InvisibleButton("hit", size);
                    var hovered = ImGui.IsItemHovered();
                    if (hovered) Hovered = part.Bone;

                    // Press: select it. Hold and move: bend it (through Brio). Let go: done.
                    var mouse = ImGui.GetMousePos();
                    if (ImGui.IsItemActivated())
                    {
                        Selected = part.Bone;
                        pressAt = mouse;
                        pressedBone = part.Bone;
                    }
                    if (ImGui.IsItemActive() && pressedBone == part.Bone)
                    {
                        if (!Posing.Dragging && Vector2.Distance(mouse, pressAt) > 4f) Posing.Start(actor, part, pressAt);
                        if (Posing.Dragging) Posing.Update(mouse);
                    }
                    if (ImGui.IsItemDeactivated() && pressedBone == part.Bone)
                    {
                        if (Posing.Dragging) Posing.End(mouse);
                        pressedBone = "";
                    }

                    // Mouse wheel: the movement that's hard to drag from this camera angle.
                    var wheel = ImGui.GetIO().MouseWheel;
                    if (hovered && wheel != 0 && !Posing.Dragging)
                    {
                        Selected = part.Bone;
                        // Wheel on a hand turns the palm: that's the forearm rolling, so it's done at the elbow.
                        var target = part;
                        if (part.Bone.StartsWith("j_te_"))
                            target = Array.Find(Joints.Parts, p => p.Bone == "j_ude_b" + part.Bone[^2..]) ?? part;
                        Posing.Wheel(actor, target, wheel, mouse);
                    }

                    var selected = Selected == part.Bone;
                    var draw = ImGui.GetWindowDrawList();
                    // Dark ring underneath so the circle shows on any background, then the colored ring.
                    draw.AddCircle(screen, r, dark, 32, selected ? 7f : 5f);
                    if (selected || hovered) draw.AddCircleFilled(screen, r - 2, fill, 32);
                    draw.AddCircle(screen, r, selected ? gold : white, 32, selected ? 4f : 2.5f);
                    if (selected || hovered || cfg.ShowLabels) labels.Add((screen + new Vector2(r + 6, -ImGui.GetTextLineHeight() / 2), part.Name, selected || hovered));
                }
                ImGui.End();
            }
            if (DetailRoot.Length == 0) DrawWholeBody(actor, labels, r, dark, gold, white, fill);
        }
        finally { ImGui.PopStyleVar(3); }

        // Names on top of everything, with a dark backing so they're readable.
        var fg = ImGui.GetForegroundDrawList();
        foreach (var (p, text, strong) in labels)
        {
            if (!strong && !cfg.ShowLabels) continue;
            var ts = ImGui.CalcTextSize(text);
            fg.AddRectFilled(p - new Vector2(4, 2), p + ts + new Vector2(4, 2), dark, 4f);
            fg.AddText(p, strong ? gold : white, text);
        }
    }
}
