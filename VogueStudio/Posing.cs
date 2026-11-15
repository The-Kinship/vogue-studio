using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Dalamud.Game.ClientState.Objects.Types;

namespace VogueStudio;

/// <summary>
/// Click-and-drag bending. When you drag a joint's circle, the joint turns like a paper doll's:
/// around the line from the camera to the joint, following your mouse around the circle.
/// The edited pose is handed to Brio, which holds it on the character.
/// </summary>
public static class Posing
{
    /// <summary>A short line about what's happening or what went wrong ("" when all is fine).</summary>
    public static string Message { get; private set; } = "";

    private sealed class Drag
    {
        public required IGameObject Actor;
        public required string Bone;
        public required JsonNode Pose;             // the pose when the drag started
        public required Quaternion StartLocal;     // the joint's own turn when the drag started
        public required Vector3 Axis;              // camera-to-joint line, in the world
        public required float Sign;                // which way round the world turn shows on screen
        public required Quaternion Skeleton;       // the whole character's turn
        public required Quaternion Parent;         // the parent joint's turn
        public required Vector2 JointScreen;
        public required Vector2 MouseStart;
        public float LastAngle;
        public long LastSent;
    }

    private static Drag? drag;
    private static readonly Stack<string> undo = new();
    private static readonly Stack<string> redo = new();

    public static bool Dragging => drag != null;
    public static bool CanUndo => undo.Count > 0;
    public static bool CanRedo => redo.Count > 0;

    /// <summary>Mouse pressed on a joint's circle.</summary>
    public static void Start(IGameObject actor, string bone, Vector2 mouse)
    {
        drag = null;
        var json = BrioIpc.GetPose(actor);
        if (json == null) { Message = BrioIpc.LastError; return; }
        JsonNode? pose;
        try { pose = JsonNode.Parse(json); } catch (Exception) { pose = null; }
        var rotText = pose?["Bones"]?[bone]?["Rotation"]?.GetValue<string>();
        if (pose == null || rotText == null || !TryParseQuat(rotText, out var local))
        {
            Message = "Brio's pose doesn't have this body part, so it can't be moved.";
            return;
        }

        var info = Joints.Info(actor, bone);
        if (info is not { } bi) { Message = "Couldn't find this joint on the character."; return; }
        if (!Plugin.GameGui.WorldToScreen(bi.World, out var jointScreen)) { Message = "This joint is off screen."; return; }
        if (!ViewAxis(bi.World, jointScreen, out var axis)) { Message = "Couldn't work out the camera direction here."; return; }

        drag = new Drag
        {
            Actor = actor, Bone = bone, Pose = pose, StartLocal = local, Axis = axis,
            Sign = ScreenSign(bi.World, jointScreen, axis),
            Skeleton = bi.SkeletonRotation, Parent = bi.ParentModelRotation,
            JointScreen = jointScreen, MouseStart = mouse,
        };
        undo.Push(json);
        redo.Clear();
        Message = "";
    }

    /// <summary>Mouse moved while held: turn the joint to follow it (sent to Brio a few times a second).</summary>
    public static void Update(Vector2 mouse, bool final = false)
    {
        if (drag == null) return;
        var from = drag.MouseStart - drag.JointScreen;
        var to = mouse - drag.JointScreen;
        if (to.Length() < 6f || from.Length() < 1f) { if (final) drag = null; return; }

        var angle = SignedAngle(from, to) * Plugin.Config.DragStrength;
        var now = Environment.TickCount64;
        if (!final && (now - drag.LastSent < 90 || MathF.Abs(angle - drag.LastAngle) < 0.004f)) return;
        drag.LastSent = now;
        drag.LastAngle = angle;

        // The turn in the world, converted into the joint's own turn:
        // world turn of the joint = skeleton * parent * local, so the new local = (skeleton*parent)^-1 * turn * (skeleton*parent) * local.
        var world = Quaternion.CreateFromAxisAngle(drag.Axis, angle * drag.Sign);
        var frame = drag.Skeleton * drag.Parent;
        var newLocal = Quaternion.Normalize(Quaternion.Inverse(frame) * world * frame * drag.StartLocal);

        drag.Pose["Bones"]![drag.Bone]!["Rotation"] = FormatQuat(newLocal);
        if (!BrioIpc.LoadPose(drag.Actor, drag.Pose.ToJsonString())) Message = BrioIpc.LastError;
        if (final) drag = null;
    }

    /// <summary>Mouse let go.</summary>
    public static void End(Vector2 mouse) => Update(mouse, final: true);

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

    // ---------- Math ----------

    /// <summary>
    /// The direction from the camera through the joint, found by nudging a point around the joint and seeing which
    /// direction doesn't move it on screen.
    /// </summary>
    private static bool ViewAxis(Vector3 p, Vector2 s, out Vector3 axis)
    {
        axis = default;
        const float h = 0.05f;
        if (!Plugin.GameGui.WorldToScreen(p + new Vector3(h, 0, 0), out var sx)) return false;
        if (!Plugin.GameGui.WorldToScreen(p + new Vector3(0, h, 0), out var sy)) return false;
        if (!Plugin.GameGui.WorldToScreen(p + new Vector3(0, 0, h), out var sz)) return false;
        var rowX = new Vector3(sx.X - s.X, sy.X - s.X, sz.X - s.X);
        var rowY = new Vector3(sx.Y - s.Y, sy.Y - s.Y, sz.Y - s.Y);
        var c = Vector3.Cross(rowX, rowY);
        if (c.LengthSquared() < 1e-8f) return false;
        axis = Vector3.Normalize(c);
        return true;
    }

    /// <summary>+1 or -1: whether turning around the axis goes the same way round on screen as the mouse angle.</summary>
    private static float ScreenSign(Vector3 p, Vector2 s, Vector3 axis)
    {
        var helper = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var arm = Vector3.Normalize(Vector3.Cross(axis, helper)) * 0.3f;
        var turned = Vector3.Transform(arm, Quaternion.CreateFromAxisAngle(axis, 0.2f));
        if (!Plugin.GameGui.WorldToScreen(p + arm, out var a) || !Plugin.GameGui.WorldToScreen(p + turned, out var b)) return 1f;
        return SignedAngle(a - s, b - s) >= 0 ? 1f : -1f;
    }

    private static float SignedAngle(Vector2 a, Vector2 b)
    {
        var d = MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X);
        while (d > MathF.PI) d -= 2 * MathF.PI;
        while (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }

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
