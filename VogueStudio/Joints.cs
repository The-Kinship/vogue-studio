using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace VogueStudio;

/// <summary>How a body part is allowed to move (doll style: never twisting along its own length).</summary>
public enum MoveKind
{
    /// <summary>Back, chest, neck, head: bend forward/back, turn, lean.</summary>
    Free,
    /// <summary>Arms, legs, hands: swing two ways, never twist.</summary>
    TwoWay,
    /// <summary>Elbows and knees: a single hinge.</summary>
    Hinge,
    /// <summary>Feet: point and flex only.</summary>
    FootHinge,
}

/// <summary>A body part you can pick: its name, the joint it turns at, the joint at its far end (to see where it points), and how it may move.</summary>
public sealed record BodyPart(string Name, string Bone, string? End, string Group, MoveKind Kind, bool Simple = true);

/// <summary>Everything needed to move one part: where it is, where it points, and its doll-style movement directions.</summary>
public sealed class Rig
{
    public Vector3 Joint;                 // the pivot, in the world
    public Vector3 Tip;                   // a point at the far end of the part, in the world
    public Quaternion SkeletonRotation;   // the whole character's turn
    public Quaternion ParentRotation;     // the parent joint's current turn (character space)
    public List<Vector3> Axes = new();    // allowed movement axes, in the parent joint's own frame
}

/// <summary>
/// Reads the character's skeleton (read only): where the joints are, and, from the skeleton's built-in
/// standing pose, which ways each part should bend.
/// </summary>
public static unsafe class Joints
{
    /// <summary>The body parts, doll style. "Left" and "right" are the character's own left and right.</summary>
    public static readonly BodyPart[] Parts =
    {
        new("Hips", "j_kosi", "j_sebo_b", "Body", MoveKind.Free),
        new("Lower back", "j_sebo_a", "j_sebo_c", "Body", MoveKind.Free, false),
        new("Middle back", "j_sebo_b", "j_kubi", "Body", MoveKind.Free, false),
        new("Chest", "j_sebo_c", "j_kubi", "Body", MoveKind.Free),
        new("Neck", "j_kubi", "j_kao", "Body", MoveKind.Free),
        new("Head", "j_kao", null, "Body", MoveKind.Free),

        new("Left shoulder", "j_sako_l", "j_ude_a_l", "Left arm", MoveKind.TwoWay, false),
        new("Left arm", "j_ude_a_l", "j_ude_b_l", "Left arm", MoveKind.TwoWay),
        new("Left elbow", "j_ude_b_l", "j_te_l", "Left arm", MoveKind.Hinge),
        new("Left hand", "j_te_l", "j_naka_a_l", "Left arm", MoveKind.TwoWay),
        new("Right shoulder", "j_sako_r", "j_ude_a_r", "Right arm", MoveKind.TwoWay, false),
        new("Right arm", "j_ude_a_r", "j_ude_b_r", "Right arm", MoveKind.TwoWay),
        new("Right elbow", "j_ude_b_r", "j_te_r", "Right arm", MoveKind.Hinge),
        new("Right hand", "j_te_r", "j_naka_a_r", "Right arm", MoveKind.TwoWay),

        new("Left leg", "j_asi_a_l", "j_asi_c_l", "Left leg", MoveKind.TwoWay),
        new("Left knee", "j_asi_c_l", "j_asi_d_l", "Left leg", MoveKind.Hinge),
        new("Left foot", "j_asi_d_l", "j_asi_e_l", "Left leg", MoveKind.FootHinge),
        new("Right leg", "j_asi_a_r", "j_asi_c_r", "Right leg", MoveKind.TwoWay),
        new("Right knee", "j_asi_c_r", "j_asi_d_r", "Right leg", MoveKind.Hinge),
        new("Right foot", "j_asi_d_r", "j_asi_e_r", "Right leg", MoveKind.FootHinge),
    };

    /// <summary>The parts shown right now (Simple mode leaves out the extra back and shoulder joints).</summary>
    public static IEnumerable<BodyPart> Shown()
    {
        foreach (var p in Parts)
            if (p.Simple || !Plugin.Config.SimpleMode) yield return p;
    }

    /// <summary>The character being posed: your GPose target, or yourself in GPose.</summary>
    public static IGameObject? Target()
    {
        var t = Plugin.TargetManager.GPoseTarget;
        if (t != null) return t;
        return Plugin.ObjectTable[201];   // your own character's GPose copy
    }

    // Character space: Y is up, Z is the way the character faces, X is to the side.
    private static readonly Vector3 Side = Vector3.UnitX, Up = Vector3.UnitY, Forward = Vector3.UnitZ;

    /// <summary>The movement setup for one part (null if its joints can't be found).</summary>
    public static Rig? RigFor(IGameObject actor, BodyPart part)
    {
        var go = (GameObjectStruct*)actor.Address;
        if (go == null || go->DrawObject == null) return null;
        var skel = ((CharacterBase*)go->DrawObject)->Skeleton;
        if (skel == null || skel->PartialSkeletons == null) return null;

        var t = skel->Transform;
        var tPos = new Vector3(t.Position.X, t.Position.Y, t.Position.Z);
        var tScale = new Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z);
        var tRot = new Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W);
        Vector3 ToWorld(Vector3 model) => tPos + Vector3.Transform(model * tScale, tRot);

        for (var s = 0; s < skel->PartialSkeletonCount; s++)
        {
            var pose = skel->PartialSkeletons[s].GetHavokPose(0);
            if (pose == null || pose->Skeleton == null) continue;
            var hk = pose->Skeleton;
            var count = Math.Min(hk->Bones.Length, pose->ModelPose.Length);
            int index = -1, end = -1;
            for (var i = 0; i < count; i++)
            {
                var name = hk->Bones[i].Name.String;
                if (name == part.Bone) index = i;
                else if (part.End != null && name == part.End) end = i;
            }
            if (index < 0) continue;

            var parent = index < hk->ParentIndices.Length ? hk->ParentIndices[index] : (short)-1;
            var rig = new Rig { SkeletonRotation = tRot };
            rig.Joint = ToWorld(Pos(pose->ModelPose[index]));
            rig.ParentRotation = parent >= 0 ? Rot(pose->ModelPose[parent]) : Quaternion.Identity;

            // Where the part points now (the far end), for working out which way it moves on screen.
            if (end >= 0) rig.Tip = ToWorld(Pos(pose->ModelPose[end]));
            else if (parent >= 0) rig.Tip = rig.Joint + (rig.Joint - ToWorld(Pos(pose->ModelPose[parent])));   // head: carry on past the neck
            else rig.Tip = rig.Joint + Vector3.Transform(Up, tRot) * 0.2f;

            // The skeleton's built-in standing pose: which way this part points there, and how its parent is turned.
            var (refPos, refRot) = ReferencePose(hk);
            var n = refPos.Length;
            var refParent = parent >= 0 && parent < n ? refRot[parent] : Quaternion.Identity;
            var refDir = index >= n ? Up
                : end >= 0 && end < n ? refPos[end] - refPos[index]
                : parent >= 0 && parent < n ? refPos[index] - refPos[parent] : Up;
            refDir = refDir.LengthSquared() > 1e-8f ? Vector3.Normalize(refDir) : Up;

            // Doll-style movement directions in character space, then into the parent joint's own frame.
            var axes = new List<Vector3>();
            switch (part.Kind)
            {
                case MoveKind.Free:
                    axes.AddRange(new[] { Side, Up, Forward });
                    break;
                case MoveKind.TwoWay:
                    // All three, minus the one along the part itself (that would be a twist).
                    foreach (var a in new[] { Side, Up, Forward })
                        if (MathF.Abs(Vector3.Dot(a, refDir)) < 0.7f) axes.Add(a);
                    if (axes.Count == 3) { axes.Sort((a, b) => MathF.Abs(Vector3.Dot(a, refDir)).CompareTo(MathF.Abs(Vector3.Dot(b, refDir)))); axes.RemoveAt(2); }
                    break;
                case MoveKind.Hinge:
                    axes.Add(SafeCross(refDir, Forward, Side));
                    break;
                case MoveKind.FootHinge:
                    axes.Add(SafeCross(refDir, Up, Side));
                    break;
            }
            var toParent = Quaternion.Inverse(refParent);
            foreach (var a in axes) rig.Axes.Add(Vector3.Normalize(Vector3.Transform(a, toParent)));
            return rig;
        }
        return null;
    }

    /// <summary>
    /// The part and everything below it (shoulder: arm, elbow, hand and fingers; neck: head, face and hair),
    /// so the whole chain moves together like a doll's limb.
    /// </summary>
    public static HashSet<string> Subtree(IGameObject actor, string boneName)
    {
        var names = new HashSet<string>();
        var go = (GameObjectStruct*)actor.Address;
        if (go == null || go->DrawObject == null) return names;
        var skel = ((CharacterBase*)go->DrawObject)->Skeleton;
        if (skel == null || skel->PartialSkeletons == null || skel->PartialSkeletonCount == 0) return names;

        // The main body skeleton: walk down from the bone through every child.
        var body = skel->PartialSkeletons[0].GetHavokPose(0);
        if (body == null || body->Skeleton == null) return names;
        var hk = body->Skeleton;
        var n = Math.Min(hk->Bones.Length, hk->ParentIndices.Length);
        var inside = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var name = hk->Bones[i].Name.String;
            var p = hk->ParentIndices[i];
            // Parents always come before their children in the list, so one pass is enough.
            inside[i] = name == boneName || (p >= 0 && p < i && inside[p]);
            if (inside[i] && name != null) names.Add(name);
        }

        // Face, hair and other attached skeletons: included when they hang off a bone in the chain.
        for (var s = 1; s < skel->PartialSkeletonCount; s++)
        {
            var partial = &skel->PartialSkeletons[s];
            var at = partial->ConnectedParentBoneIndex;
            if (at < 0 || at >= n || !inside[at]) continue;
            var pose = partial->GetHavokPose(0);
            if (pose == null || pose->Skeleton == null) continue;
            for (var i = 0; i < pose->Skeleton->Bones.Length; i++)
            {
                var name = pose->Skeleton->Bones[i].Name.String;
                if (name != null) names.Add(name);
            }
        }
        return names;
    }

    /// <summary>The standing pose, worked out in character space (positions and turns of every bone).</summary>
    private static (Vector3[] Pos, Quaternion[] Rot) ReferencePose(FFXIVClientStructs.Havok.Animation.Rig.hkaSkeleton* hk)
    {
        var n = Math.Min(hk->ReferencePose.Length, hk->ParentIndices.Length);
        var pos = new Vector3[n];
        var rot = new Quaternion[n];
        for (var i = 0; i < n; i++)
        {
            var local = hk->ReferencePose[i];
            var lp = Pos(local);
            var lr = Rot(local);
            var p = hk->ParentIndices[i];
            if (p >= 0 && p < i)
            {
                pos[i] = pos[p] + Vector3.Transform(lp, rot[p]);
                rot[i] = Quaternion.Normalize(rot[p] * lr);
            }
            else { pos[i] = lp; rot[i] = lr; }
        }
        return (pos, rot);
    }

    private static Vector3 SafeCross(Vector3 a, Vector3 b, Vector3 fallback)
    {
        var c = Vector3.Cross(a, b);
        return c.LengthSquared() > 1e-4f ? Vector3.Normalize(c) : fallback;
    }

    private static Vector3 Pos(FFXIVClientStructs.Havok.Common.Base.Math.QsTransform.hkQsTransformf t) =>
        new(t.Translation.X, t.Translation.Y, t.Translation.Z);

    private static Quaternion Rot(FFXIVClientStructs.Havok.Common.Base.Math.QsTransform.hkQsTransformf t) =>
        new(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W);

    /// <summary>World positions of the shown parts' joints right now (bones the character doesn't have are left out).</summary>
    public static Dictionary<string, Vector3> WorldPositions(IGameObject actor)
    {
        var result = new Dictionary<string, Vector3>();
        var go = (GameObjectStruct*)actor.Address;
        if (go == null || go->DrawObject == null) return result;
        var skel = ((CharacterBase*)go->DrawObject)->Skeleton;
        if (skel == null || skel->PartialSkeletons == null) return result;

        var wanted = new HashSet<string>();
        foreach (var p in Parts) wanted.Add(p.Bone);

        var t = skel->Transform;
        var tPos = new Vector3(t.Position.X, t.Position.Y, t.Position.Z);
        var tScale = new Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z);
        var tRot = new Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W);
        for (var s = 0; s < skel->PartialSkeletonCount; s++)
        {
            var pose = skel->PartialSkeletons[s].GetHavokPose(0);
            if (pose == null || pose->Skeleton == null) continue;
            var bones = pose->Skeleton->Bones;
            var count = Math.Min(bones.Length, pose->ModelPose.Length);
            for (var i = 0; i < count; i++)
            {
                var name = bones[i].Name.String;
                if (name == null || !wanted.Contains(name) || result.ContainsKey(name)) continue;
                result[name] = tPos + Vector3.Transform(Pos(pose->ModelPose[i]) * tScale, tRot);
            }
        }
        return result;
    }
}
