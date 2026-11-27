using System;
using System.IO;
using EFT.InventoryLogic;
using UnityEngine;

namespace Hawks.RonShields
{
    internal sealed class ShieldPose
    {
        internal Vector3 Shield,
            Left,
            Right,
            RightShoulder,
            RightElbow;
        internal float AimWeight,
            LeftWristYaw;
        internal Vector3 ShieldTrack,
            LeftArmTrack,
            RightArmTrack,
            WeaponTrack;
        internal Quaternion ShieldRotation,
            RightWristRotation;
        internal readonly Vector3[] Fingers = new Vector3[15];

        internal void Blend(ShieldPose a, ShieldPose b, float t)
        {
            AimWeight = Mathf.Lerp(a.AimWeight, b.AimWeight, t);
            ShieldTrack = Vector3.Lerp(a.ShieldTrack, b.ShieldTrack, t);
            LeftArmTrack = Vector3.Lerp(a.LeftArmTrack, b.LeftArmTrack, t);
            RightArmTrack = Vector3.Lerp(a.RightArmTrack, b.RightArmTrack, t);
            WeaponTrack = Vector3.Lerp(a.WeaponTrack, b.WeaponTrack, t);
            LeftWristYaw = Mathf.LerpAngle(a.LeftWristYaw, b.LeftWristYaw, t);
            Shield = Vector3.Lerp(a.Shield, b.Shield, t);
            Left = Vector3.Lerp(a.Left, b.Left, t);
            Right = Vector3.Lerp(a.Right, b.Right, t);
            RightShoulder = Vector3.Lerp(a.RightShoulder, b.RightShoulder, t);
            RightElbow = Vector3.Lerp(a.RightElbow, b.RightElbow, t);
            ShieldRotation = Quaternion.Slerp(a.ShieldRotation, b.ShieldRotation, t);
            RightWristRotation = Quaternion.Slerp(a.RightWristRotation, b.RightWristRotation, t);
            for (int i = 0; i < Fingers.Length; i++)
                Fingers[i] = Vector3.Lerp(a.Fingers[i], b.Fingers[i], t);
        }
    }

    internal sealed class ShieldPlayback
    {
        readonly ShieldPose sampled = new ShieldPose(),
            blendStart = new ShieldPose();
        int phaseId = -1;
        float switchedAt;

        internal void Reset() => phaseId = -1;

        internal void Evaluate(
            ShieldPose pose,
            bool stowing,
            float draw,
            bool extending,
            float extension,
            float holster,
            ShieldPose[] frames
        )
        {
            int id =
                stowing ? 3
                : draw < 1 ? 0
                : extending ? 1
                : 2;
            if (id == 0)
                ShieldMotion.Sample(1, 49, draw, sampled, frames);
            else if (id == 3)
                ShieldMotion.Sample(142, 179, holster, sampled, frames);
            else if (id == 1)
                ShieldMotion.Sample(80, 100, extension, sampled, frames);
            else
                ShieldMotion.Sample(121, 141, 1 - extension, sampled, frames);
            if (phaseId != id)
            {
                if (phaseId < 0)
                {
                    pose.Blend(sampled, sampled, 0);
                    phaseId = id;
                    switchedAt = Time.time - 1;
                    blendStart.Blend(sampled, sampled, 0);
                    return;
                }
                switchedAt = Time.time;
                blendStart.Blend(pose, pose, 0);
                phaseId = id;
            }
            pose.Blend(
                blendStart,
                sampled,
                Mathf.SmoothStep(0, 1, (Time.time - switchedAt) / .10f)
            );
        }
    }

    internal readonly struct ShieldPoseFrame
    {
        internal readonly Vector3 Center,
            Left,
            Right,
            Upper,
            Elbow,
            ShieldPosition,
            LeftShoulderShift;
        internal readonly Quaternion LeftBasis,
            Rotation,
            LeftWristTurn;

        internal ShieldPoseFrame(
            ShieldPose p,
            Vector3 shoulder,
            Quaternion facing,
            Quaternion fatigue,
            float scale,
            Quaternion weaponBasis,
            bool armed
        )
        {
            var turn =
                p.ShieldRotation * Quaternion.Inverse(ShieldMotion.Frames[48].ShieldRotation);
            var basis = turn * Quaternion.Inverse(p.ShieldRotation);
            var rightBasis = facing * basis;
            Center = shoulder + facing * (p.Shield * scale);
            Rotation = facing * fatigue * turn;
            ShieldPosition = Center + facing * (p.ShieldTrack * scale);
            LeftShoulderShift = weaponBasis * (p.LeftArmTrack * scale);
            LeftWristTurn = Quaternion.AngleAxis(p.LeftWristYaw, weaponBasis * Vector3.up);
            var rightArmShift = weaponBasis * (p.RightArmTrack * scale);
            // With a sidearm, its marker and the hand share the weapon position track.
            var rightHandShift = armed ? weaponBasis * (p.WeaponTrack * scale) : rightArmShift;
            LeftBasis = facing * fatigue * basis;
            Left = Center + LeftBasis * ((p.Left - p.Shield) * scale) + LeftShoulderShift;
            var rest = Shields.RightRestCm;
            var aim = new Vector3(5f, -1.5f, 0f) + (Shields.RightAimCm);
            var rightShift = facing * (Vector3.Lerp(rest, aim, p.AimWeight) * (.01f * scale));
            Right =
                Center + rightBasis * ((p.Right - p.Shield) * scale) + rightShift + rightHandShift;
            Upper = Center + rightBasis * ((p.RightShoulder - p.Shield) * scale) + rightArmShift;
            Elbow =
                Center
                + rightBasis * ((p.RightElbow - p.Shield) * scale)
                + rightShift * .5f
                + rightArmShift;
        }
    }

    internal struct ShieldSprintBasis
    {
        const float ReleaseDuration = 0.24f;
        bool hasReference,
            wasSprinting;
        Quaternion relative;
        float stoppedAt;
        internal float NativeWeight { get; private set; }

        internal void Reset() => this = default;

        internal Quaternion Resolve(Quaternion facing, Quaternion weapon, bool sprinting)
        {
            if (!hasReference)
            {
                relative = Quaternion.Inverse(facing) * weapon;
                stoppedAt = Time.time - ReleaseDuration;
                hasReference = true;
            }

            if (wasSprinting && !sprinting)
                stoppedAt = Time.time;

            wasSprinting = sprinting;
            NativeWeight = sprinting
                ? 0f
                : Mathf.SmoothStep(0f, 1f, (Time.time - stoppedAt) / ReleaseDuration);
            if (NativeWeight >= 1f)
                relative = Quaternion.Inverse(facing) * weapon;
            return Quaternion.Slerp(facing * relative, weapon, NativeWeight);
        }
    }

    internal static class ShieldMotion
    {
        internal static ShieldPose HoldPose => Frames[140];

        internal static Vector3 LiftLeftElbow(
            Vector3 shoulder,
            Vector3 elbow,
            Vector3 wrist,
            Quaternion facing,
            bool vant
        )
        {
            var axis = wrist - shoulder;
            var bend = Vector3.ProjectOnPlane(elbow - shoulder, axis);
            if (bend.sqrMagnitude < 1e-6f)
                return Vector3.zero;
            bend += Vector3.ProjectOnPlane(facing * Vector3.up * (vant ? .012f : .02f), axis);
            return Vector3.Cross(bend, axis).normalized;
        }

        internal static Vector3 SprintSway() =>
            new Vector3(.008f * Mathf.Sin(Time.time * 10), .005f * Mathf.Sin(Time.time * 20), 0);

        internal static Quaternion FatigueRotation(float fatigue)
        {
            float t = Time.time;
            return Quaternion.Euler(
                fatigue * 4f * Mathf.Sin(t * 3.8f),
                fatigue * 2.8f * Mathf.Sin(t * 2.7f),
                fatigue * 3f * Mathf.Sin(t * 3.1f)
            );
        }

        internal static void AdjustRightArm(
            ref Vector3 wrist,
            ref Vector3 elbow,
            Quaternion facing,
            float scale,
            float aim,
            float magazine
        )
        {
            float t = Time.time * 2.8f;
            wrist +=
                facing
                * (
                    new Vector3(Mathf.Sin(t) * .0018f, Mathf.Sin(t * .71f) * .0012f, 0)
                    * (aim * scale)
                );
            if (magazine <= 0)
                return;
            float tuck = ShieldCarry.EaseMagazinePose(magazine);
            float arc = 4f * tuck * (1f - tuck);
            wrist +=
                facing
                * (
                    (
                        new Vector3(.13f, -.34f, -.17f) * tuck
                        + new Vector3(.023f, .012f, -.02f) * arc
                    ) * scale
                );
            elbow += facing * (new Vector3(.09f, -.13f, -.095f) * (tuck * scale));
        }

        internal static Quaternion WristDelta(ShieldPose pose)
        {
            var rest = Quaternion.Euler(Shields.RightRestDegrees);
            var aim = Quaternion.Euler(Shields.RightAimDegrees);
            var adjustment = Quaternion.Slerp(rest, aim, pose.AimWeight);
            return adjustment
                * Quaternion.AngleAxis(-pose.AimWeight, Vector3.right)
                * pose.RightWristRotation
                * Quaternion.Inverse(Frames[100].RightWristRotation);
        }

        internal static readonly ShieldPose[] Frames = Read("RoN.ShieldMotion", false);
        static readonly ShieldPose[] VantVmFrames = Read("RoN.VantVmMotion", true);
        static readonly ShieldPose[] VantLmFrames = Read("RoN.VantLmMotion", true);

        internal static ShieldPose[] For(Item shield)
        {
            if (!Shields.IsVant(shield))
                return Frames;
            return shield.TemplateId == "692340510000000000000005" ? VantVmFrames : VantLmFrames;
        }

        static Vector3 V(BinaryReader b) =>
            new Vector3(b.ReadSingle(), b.ReadSingle(), b.ReadSingle());

        static Quaternion Q(BinaryReader b) =>
            new Quaternion(b.ReadSingle(), b.ReadSingle(), b.ReadSingle(), b.ReadSingle());

        static ShieldPose[] Read(string resource, bool hasTracks)
        {
            using (var stream = typeof(ShieldMotion).Assembly.GetManifestResourceStream(resource))
            using (var b = new BinaryReader(stream))
            {
                if (stream.Length != 179 * (hasTracks ? 81 : 68) * sizeof(float))
                    throw new InvalidDataException("Invalid shield motion track");
                var frames = new ShieldPose[179];
                for (int i = 0; i < frames.Length; i++)
                {
                    var p = new ShieldPose
                    {
                        Shield = V(b),
                        ShieldRotation = Q(b),
                        Left = V(b),
                        Right = V(b),
                        RightWristRotation = Q(b),
                        RightShoulder = V(b),
                        RightElbow = V(b),
                    };
                    for (int j = 0; j < p.Fingers.Length; j++)
                        p.Fingers[j] = V(b);
                    if (hasTracks)
                    {
                        p.ShieldTrack = V(b);
                        p.LeftArmTrack = V(b);
                        p.RightArmTrack = V(b);
                        p.WeaponTrack = V(b);
                        p.LeftWristYaw = b.ReadSingle();
                    }
                    // Author a lateral target on the raise / hold / lower tracks before pose blending and IK.
                    int frame = i + 1;
                    float extension = Mathf.Min(
                        Mathf.InverseLerp(80, 100, frame),
                        Mathf.InverseLerp(141, 121, frame)
                    );
                    p.AimWeight = Mathf.SmoothStep(0f, 1f, extension);
                    frames[i] = p;
                }
                return frames;
            }
        }

        internal static void Sample(
            int first,
            int last,
            float phase,
            ShieldPose output,
            ShieldPose[] frames
        )
        {
            float frame = first - 1 + (last - first) * Mathf.Clamp01(phase);
            int i = (int)frame;
            output.Blend(frames[i], frames[Math.Min(i + 1, last - 1)], frame - i);
        }
    }
}
