using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Dalamud.Game.ClientState.Objects.Types;

namespace VogueStudio;

/// <summary>
/// Doll-style dragging. Each body part only moves the ways a doll's would (elbows and knees are hinges,
/// nothing twists along its own length). Drag up or down and the part moves up or down on screen; drag sideways
/// and it moves sideways. Once a drag starts in one direction it locks to that one movement until you let go.
/// The edited pose is handed to Brio, which holds it on the character.
/// </summary>
public static class Posing
{
    /// <summary>A short line about what's happening or what went wrong ("" when all is fine).</summary>
    public static string Message { get; private set; } = "";

    /// <summary>How the drag is locked right now, for the window ("" when not dragging).</summary>
    public static string Locked { get; private set; } = "";

    private sealed class Drag
    {
        public required IGameObject Actor;
        public required BodyPart Part;
        public required JsonNode Pose;            // the pose when the drag started
        public required Vector3 Pivot;            // the joint it turns at (character space)
        public required List<Vector3> Axes;       // allowed axes (character space)
        public required Dictionary<string, (Vector3 Pos, Quaternion Rot)> Chain;  // the part and everything below it, as at the start
        public required List<Vector2> ScreenMove; // how the tip moves on screen per unit of turn, for each axis
        public required Vector2 MouseStart;
        public int Axis = -1;                     // the locked axis (-1 = not decided yet)
        public float LastAngle;
        public long LastSent;
    }

    private static Drag? drag;
    private static readonly Stack<string> undo = new();
    private static readonly Stack<string> redo = new();

    public static bool Dragging => drag != null;
    public static bool CanUndo => undo.Count > 0;
    public static bool CanRedo => redo.Count > 0;

    /// <summary>The mouse moved a few pixels while pressed on a part's circle.</summary>
    public static void Start(IGameObject actor, BodyPart part, Vector2 mouse)
    {
        drag = null;
        Locked = "";
        var json = BrioIpc.GetPose(actor);
        if (json == null) { Message = BrioIpc.LastError; return; }
        JsonNode? pose;
        try { pose = JsonNode.Parse(json); } catch (Exception) { pose = null; }
        var bones = pose?["Bones"] as JsonObject;
        if (pose == null || bones == null || !TryBone(bones, part.Bone, out var pivot, out _))
        {
            Message = "Brio's pose doesn't have this body part, so it can't be moved.";
            return;
        }

        // Brio keeps every joint in whole-body space, so the part AND everything below it must move together.
        var chain = new Dictionary<string, (Vector3, Quaternion)>();
        foreach (var name in Joints.Subtree(actor, part.Bone))
            if (TryBone(bones, name, out var bp, out var br)) chain[name] = (bp, br);

        var rig = Joints.RigFor(actor, part);
        if (rig == null || rig.Axes.Count == 0) { Message = "Couldn't find this body part on the character."; return; }
        if (!Plugin.GameGui.WorldToScreen(rig.Tip, out var tipScreen)) { Message = "This body part is off screen."; return; }

        // For each allowed movement: its axis in character space, and which way (and how far) the tip moves on screen for a small turn.
        const float step = 0.05f;
        var screenMoves = new List<Vector2>();
        var modelAxes = new List<Vector3>();
        foreach (var a in rig.Axes)
        {
            var modelAxis = Vector3.Normalize(Vector3.Transform(a, rig.ParentRotation));
            modelAxes.Add(modelAxis);
            var worldAxis = Vector3.Normalize(Vector3.Transform(modelAxis, rig.SkeletonRotation));
            var moved = rig.Joint + Vector3.Transform(rig.Tip - rig.Joint, Quaternion.CreateFromAxisAngle(worldAxis, step));
            screenMoves.Add(Plugin.GameGui.WorldToScreen(moved, out var ms) ? (ms - tipScreen) / step : Vector2.Zero);
        }

        drag = new Drag
        {
            Actor = actor, Part = part, Pose = pose, Pivot = pivot, Chain = chain,
            Axes = modelAxes, ScreenMove = screenMoves, MouseStart = mouse,
        };
        undo.Push(json);
        redo.Clear();
        Message = "";
    }

    /// <summary>Mouse moved while held: turn the part around its locked axis (sent to Brio a few times a second).</summary>
    public static void Update(Vector2 mouse, bool final = false)
    {
        if (drag == null) return;
        var moved = mouse - drag.MouseStart;

        // Lock to one movement: the one whose on-screen direction best matches how you started dragging.
        if (drag.Axis < 0)
        {
            if (moved.Length() < 10f) { if (final) Finish(); return; }
            var dir = Vector2.Normalize(moved);
            var best = -1;
            var bestScore = 0f;
            for (var i = 0; i < drag.ScreenMove.Count; i++)
            {
                var sm = drag.ScreenMove[i];
                if (sm.Length() < 5f) continue;   // this movement barely shows from this camera angle
                var score = MathF.Abs(Vector2.Dot(Vector2.Normalize(sm), dir));
                if (score > bestScore) { best = i; bestScore = score; }
            }
            if (best < 0) { Message = "This part can't move that way from this camera angle. Try turning the camera."; if (final) Finish(); return; }
            drag.Axis = best;
            Locked = MathF.Abs(dir.Y) >= MathF.Abs(dir.X) ? "up / down" : "side to side";
        }

        // How far to turn: the mouse's movement along the tip's on-screen direction, so the tip follows the mouse.
        var screen = drag.ScreenMove[drag.Axis];
        var angle = Vector2.Dot(moved, screen) / screen.LengthSquared() * Plugin.Config.DragStrength;
        angle = Math.Clamp(angle, -MathF.PI, MathF.PI);

        var now = Environment.TickCount64;
        if (!final && (now - drag.LastSent < 90 || MathF.Abs(angle - drag.LastAngle) < 0.004f)) return;
        drag.LastSent = now;
        drag.LastAngle = angle;

        // Turn the part and everything below it around the chosen axis, pinned at its joint: rigid, doll style.
        var turn = Quaternion.CreateFromAxisAngle(drag.Axes[drag.Axis], angle);
        var bones = drag.Pose["Bones"]!;
        foreach (var (name, (pos, rot)) in drag.Chain)
        {
            var bone = bones[name]!;
            bone["Position"] = FormatVec(drag.Pivot + Vector3.Transform(pos - drag.Pivot, turn));
            bone["Rotation"] = FormatQuat(Quaternion.Normalize(turn * rot));
        }
        if (!BrioIpc.LoadPose(drag.Actor, drag.Pose.ToJsonString())) Message = BrioIpc.LastError;
        if (final) Finish();
    }

    /// <summary>Mouse let go.</summary>
    public static void End(Vector2 mouse) => Update(mouse, final: true);

    private static void Finish()
    {
        // A click without a real drag changes nothing, so it shouldn't leave an Undo step behind.
        if (drag != null && drag.Axis < 0 && undo.Count > 0) undo.Pop();
        drag = null;
        Locked = "";
    }

    public static void Undo(IGameObject actor)
    {
        if (undo.Count == 0) return;
        var now = BrioIpc.GetPose(actor);
        if (now != null) redo.Push(now);
        if (!BrioIpc.LoadPose(actor, undo.Pop())) Message = BrioIpc.LastError;
    }

    public static void Redo(IGameObject actor)
    {
        if (redo.Count == 0) return;
        var now = BrioIpc.GetPose(actor);
        if (now != null) undo.Push(now);
        if (!BrioIpc.LoadPose(actor, redo.Pop())) Message = BrioIpc.LastError;
    }

    public static void Reset(IGameObject actor)
    {
        var now = BrioIpc.GetPose(actor);
        if (now != null) { undo.Push(now); redo.Clear(); }
        if (!BrioIpc.ResetPose(actor)) Message = BrioIpc.LastError.Length > 0 ? BrioIpc.LastError : "Brio couldn't reset the pose.";
    }

    /// <summary>A bone's position and turn from Brio's pose text.</summary>
    private static bool TryBone(JsonObject bones, string name, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.Identity;
        if (bones[name] is not JsonObject b) return false;
        var pt = b["Position"]?.GetValue<string>();
        var rt = b["Rotation"]?.GetValue<string>();
        return pt != null && rt != null && TryParseVec(pt, out pos) && TryParseQuat(rt, out rot);
    }

    private static bool TryParseVec(string text, out Vector3 v)
    {
        v = default;
        var parts = text.Split(',');
        if (parts.Length != 3) return false;
        var f = new float[3];
        for (var i = 0; i < 3; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
        v = new Vector3(f[0], f[1], f[2]);
        return true;
    }

    private static string FormatVec(Vector3 v) =>
        string.Join(", ", new[] { v.X, v.Y, v.Z }.Select(f => f.ToString(CultureInfo.InvariantCulture)));

    private static bool TryParseQuat(string text, out Quaternion q)
    {
        q = Quaternion.Identity;
        var parts = text.Split(',');
        if (parts.Length != 4) return false;
        var v = new float[4];
        for (var i = 0; i < 4; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        q = new Quaternion(v[0], v[1], v[2], v[3]);
        return true;
    }

    private static string FormatQuat(Quaternion q) =>
        string.Join(", ", new[] { q.X, q.Y, q.Z, q.W }.Select(f => f.ToString(CultureInfo.InvariantCulture)));
}
