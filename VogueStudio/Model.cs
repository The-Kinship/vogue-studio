using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;

namespace VogueStudio;

/// <summary>
/// The whole character's position and turn, through Brio (the same as Brio's Model Transform boxes).
/// Drag the "Whole body" handle up/down to raise or lower them, sideways to slide them along the floor;
/// the mouse wheel turns them.
/// </summary>
public static class Model
{
    public readonly record struct State(Vector3 Position, Quaternion Rotation, Vector3 Scale);

    public static State? Get(IGameObject actor)
    {
        try
        {
            var (p, r, s) = Plugin.PluginInterface
                .GetIpcSubscriber<IGameObject, ValueTuple<Vector3?, Quaternion?, Vector3?>>("Brio.GetModelTransform.V3").InvokeFunc(actor);
            if (p == null || r == null) return null;
            return new State(p.Value, r.Value, s ?? Vector3.One);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Brio GetModelTransform failed."); return null; }
    }

    public static bool Set(IGameObject actor, State state)
    {
        try
        {
            return Plugin.PluginInterface
                .GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.SetModelTransform.V3")
                .InvokeFunc(actor, state.Position, state.Rotation, state.Scale, false);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Brio SetModelTransform failed."); return false; }
    }

    public static bool Reset(IGameObject actor)
    {
        try { return Plugin.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.ResetModelTransform.V3").InvokeFunc(actor); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Brio ResetModelTransform failed."); return false; }
    }

    // ---------- Dragging the whole body ----------

    private static IGameObject? actor;
    private static State start;
    private static Vector2 mouseStart;
    private static Vector2 perUp;      // how far (pixels) the character moves on screen per 1 unit up
    private static Vector2 perX, perZ; // ...per 1 unit along the floor's two directions
    private static int locked;         // 0 = not decided, 1 = up/down, 2 = sideways
    private static long lastSent;

    public static bool Dragging => actor != null;
    public static string Locked => locked == 1 ? "up / down" : locked == 2 ? "sideways along the floor" : "";

    public static void Start(IGameObject who, Vector2 mouse)
    {
        actor = null;
        locked = 0;
        if (Get(who) is not { } s) { Posing.SetMessage("Brio couldn't give the character's position (are they in Brio's list?)."); return; }
        var at = s.Position;
        if (!Plugin.GameGui.WorldToScreen(at, out var here)
            || !Plugin.GameGui.WorldToScreen(at + Vector3.UnitY, out var up)
            || !Plugin.GameGui.WorldToScreen(at + Vector3.UnitX, out var x)
            || !Plugin.GameGui.WorldToScreen(at + Vector3.UnitZ, out var z))
        { Posing.SetMessage("The character's feet are off screen."); return; }
        perUp = up - here;
        perX = x - here;
        perZ = z - here;
        start = s;
        mouseStart = mouse;
        actor = who;
        Posing.RememberPlace(s);
        Posing.SetMessage("");
    }

    public static void Update(Vector2 mouse, bool final = false)
    {
        if (actor == null) return;
        var moved = mouse - mouseStart;
        if (locked == 0)
        {
            if (moved.Length() < 10f) { if (final) actor = null; return; }
            locked = MathF.Abs(moved.Y) >= MathF.Abs(moved.X) ? 1 : 2;
        }

        var now = Environment.TickCount64;
        if (!final && now - lastSent < 60) return;
        lastSent = now;

        var offset = Vector3.Zero;
        if (locked == 1 && perUp.LengthSquared() > 1f)
        {
            // Up/down: follow the mouse's vertical movement.
            offset = Vector3.UnitY * (moved.Y / perUp.Y);
        }
        else if (locked == 2)
        {
            // Sideways: the floor direction that moves straight across the screen, as far as the mouse moved.
            var det = perX.X * perZ.Y - perZ.X * perX.Y;
            if (MathF.Abs(det) > 1e-3f)
            {
                var a = moved.X * perZ.Y / det;
                var c = -moved.X * perX.Y / det;
                offset = new Vector3(a, 0, c);
            }
        }
        if (!Set(actor, start with { Position = start.Position + offset })) Posing.SetMessage("Brio didn't accept the new position.");
        if (final) actor = null;
    }

    public static void End(Vector2 mouse)
    {
        Update(mouse, final: true);
        actor = null;
        locked = 0;
    }

    private static long lastTurnTime;

    /// <summary>Mouse wheel on the Whole body handle: turn 5 degrees per click. A quick run of clicks is one Undo step.</summary>
    public static void Turn(IGameObject who, float notches)
    {
        if (Get(who) is not { } s) return;
        var now = Environment.TickCount64;
        if (now - lastTurnTime > 1200) Posing.RememberPlace(s);
        lastTurnTime = now;
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, notches * 5f * MathF.PI / 180f);
        Set(who, s with { Rotation = Quaternion.Normalize(turn * s.Rotation) });
    }
}
