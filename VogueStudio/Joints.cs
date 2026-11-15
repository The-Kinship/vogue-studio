using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace VogueStudio;

/// <summary>A body part you can pick: its friendly name and the game's bone name.</summary>
public sealed record BodyPart(string Name, string Bone, string Group);

/// <summary>
/// Reads where your character's joints are (read only), from the game's own skeleton,
/// and works out where they sit on screen.
/// </summary>
public static unsafe class Joints
{
    /// <summary>The main body parts, in a sensible order. "Left" and "right" are your character's own left and right.</summary>
    public static readonly BodyPart[] Parts =
    {
        new("Hips", "j_kosi", "Body"),
        new("Lower back", "j_sebo_a", "Body"),
        new("Middle back", "j_sebo_b", "Body"),
        new("Chest", "j_sebo_c", "Body"),
        new("Neck", "j_kubi", "Body"),
        new("Head", "j_kao", "Body"),
        new("Left shoulder", "j_sako_l", "Left arm"),
        new("Left upper arm", "j_ude_a_l", "Left arm"),
        new("Left forearm", "j_ude_b_l", "Left arm"),
        new("Left hand", "j_te_l", "Left arm"),
        new("Right shoulder", "j_sako_r", "Right arm"),
        new("Right upper arm", "j_ude_a_r", "Right arm"),
        new("Right forearm", "j_ude_b_r", "Right arm"),
        new("Right hand", "j_te_r", "Right arm"),
        new("Left thigh", "j_asi_a_l", "Left leg"),
        new("Left lower leg", "j_asi_c_l", "Left leg"),
        new("Left foot", "j_asi_d_l", "Left leg"),
        new("Right thigh", "j_asi_a_r", "Right leg"),
        new("Right lower leg", "j_asi_c_r", "Right leg"),
        new("Right foot", "j_asi_d_r", "Right leg"),
    };

    /// <summary>The character being posed: your GPose target, or yourself in GPose.</summary>
    public static IGameObject? Target()
    {
        var t = Plugin.TargetManager.GPoseTarget;
        if (t != null) return t;
        return Plugin.ObjectTable[201];   // your own character's GPose copy
    }

    /// <summary>World positions of the main joints right now (bones the character doesn't have are left out).</summary>
    public static Dictionary<string, Vector3> WorldPositions(IGameObject actor)
    {
        var result = new Dictionary<string, Vector3>();
        var go = (GameObjectStruct*)actor.Address;
        if (go == null || go->DrawObject == null) return result;
        var cb = (CharacterBase*)go->DrawObject;
        var skel = cb->Skeleton;
        if (skel == null || skel->PartialSkeletons == null) return result;

        var wanted = new HashSet<string>();
        foreach (var p in Parts) wanted.Add(p.Bone);

        // The game library has its own vector types; copy them into plain System.Numerics ones.
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
                var tr = pose->ModelPose[i].Translation;
                var model = new Vector3(tr.X, tr.Y, tr.Z) * tScale;
                result[name] = tPos + Vector3.Transform(model, tRot);
            }
        }
        return result;
    }
}
