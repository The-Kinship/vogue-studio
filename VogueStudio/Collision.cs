using System;
using System.Collections.Generic;
using System.Numerics;

namespace VogueStudio;

/// <summary>
/// Simple body collision while dragging: invisible rounded tubes ("capsules") around the main body parts,
/// built from the skeleton. If the part you're moving would sink into another part, it stops at the surface.
/// An approximation: real bodies aren't perfect tubes, and clothes and props aren't included.
/// All positions are in Brio's whole-body (character) space.
/// </summary>
public sealed class Collision
{
    private readonly record struct Capsule(string Name, Vector3 A, Vector3 B, float Radius);

    /// <summary>Body parts as tubes: (shown name, start bone, end bone, radius for an average adult body).</summary>
    private static readonly (string Name, string A, string B, float R)[] Shapes =
    {
        ("belly", "j_kosi", "j_sebo_b", 0.11f),
        ("chest", "j_sebo_b", "j_kubi", 0.12f),
        ("neck", "j_kubi", "j_kao", 0.055f),
        ("hips", "j_asi_a_l", "j_asi_a_r", 0.10f),
        ("left thigh", "j_asi_a_l", "j_asi_c_l", 0.075f),
        ("right thigh", "j_asi_a_r", "j_asi_c_r", 0.075f),
        ("left lower leg", "j_asi_c_l", "j_asi_d_l", 0.055f),
        ("right lower leg", "j_asi_c_r", "j_asi_d_r", 0.055f),
        ("left upper arm", "j_ude_a_l", "j_ude_b_l", 0.05f),
        ("right upper arm", "j_ude_a_r", "j_ude_b_r", 0.05f),
        ("left forearm", "j_ude_b_l", "j_te_l", 0.04f),
        ("right forearm", "j_ude_b_r", "j_te_r", 0.04f),
    };

    private readonly List<Capsule> capsules = new();
    private readonly List<(string Bone, Vector3 Pos, float StartDepth)> points = new();

    /// <summary>The body part touched by the last blocked move ("" if none).</summary>
    public string Touching { get; private set; } = "";

    public bool Empty => capsules.Count == 0 || points.Count == 0;

    /// <summary>
    /// Builds the body's tubes (leaving out the moving part and the parts it hangs from at its joint),
    /// and the points on the moving part that mustn't sink in.
    /// </summary>
    public Collision(Dictionary<string, Vector3> bodyPositions, ICollection<string> chain, string pivot, string? pivotParent)
    {
        var cfg = Plugin.Config;
        // Scale tube sizes to this character (a Lalafell is much smaller than a Roegadyn), then by the Body thickness setting.
        var size = 1f;
        if (bodyPositions.TryGetValue("j_kosi", out var hip) && bodyPositions.TryGetValue("j_kubi", out var neck))
            size = Math.Clamp(Vector3.Distance(hip, neck) / 0.55f, 0.4f, 1.6f);
        var thickness = size * cfg.BodyThickness;

        foreach (var (name, a, b, r) in Shapes)
        {
            if (!bodyPositions.TryGetValue(a, out var pa) || !bodyPositions.TryGetValue(b, out var pb)) continue;
            // Leave out tubes that move with the part, or that meet it at its own joint (they'd always "touch").
            if (chain.Contains(a) || chain.Contains(b) || a == pivot || b == pivot || a == pivotParent || b == pivotParent) continue;
            capsules.Add(new Capsule(name, pa, pb, r * thickness));
        }
        if (bodyPositions.TryGetValue("j_kao", out var head) && bodyPositions.TryGetValue("j_kubi", out var n2)
            && !chain.Contains("j_kao") && pivot != "j_kao" && pivot != "j_kubi")
            capsules.Add(new Capsule("head", head, head + (head - n2) * 1.4f, 0.1f * thickness));

        // Points on the moving part: its joints, fingers and toes (not hair, ears, face or clothing bones).
        foreach (var bone in chain)
        {
            if (bone == pivot || !Checked(bone) || !bodyPositions.TryGetValue(bone, out var p)) continue;
            points.Add((bone, p, Depth(p, out _)));
        }
    }

    private static bool Checked(string bone) =>
        (bone.StartsWith("j_") || bone.StartsWith("iv_"))
        && !bone.StartsWith("j_ex") && !bone.StartsWith("j_sk") && !bone.StartsWith("j_kami")
        && !bone.StartsWith("j_mimi") && !bone.StartsWith("j_f_") && !bone.StartsWith("j_zera");

    /// <summary>How deep a point is inside the body (above zero = inside), allowing the "Let it touch" amount.</summary>
    private float Depth(Vector3 p, out string what)
    {
        var deepest = float.MinValue;
        what = "";
        var sink = Plugin.Config.TouchSink;
        foreach (var c in capsules)
        {
            var d = c.Radius * (1f - sink) - DistanceToSegment(p, c.A, c.B);
            if (d > deepest) { deepest = d; what = c.Name; }
        }
        return deepest;
    }

    /// <summary>
    /// True if moving the part's points with this turn (around the pivot) would push any of them deeper into the body
    /// than they started. Points already a little inside may stay or come out, just not go further in.
    /// </summary>
    public bool Blocks(Vector3 pivotPos, Quaternion turn)
    {
        foreach (var (_, pos, startDepth) in points)
        {
            var moved = pivotPos + Vector3.Transform(pos - pivotPos, turn);
            var depth = Depth(moved, out var what);
            if (depth > MathF.Max(startDepth, 0f) + 0.002f) { Touching = what; return true; }
        }
        Touching = "";
        return false;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var len = ab.LengthSquared();
        var t = len < 1e-8f ? 0f : Math.Clamp(Vector3.Dot(p - a, ab) / len, 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }
}
