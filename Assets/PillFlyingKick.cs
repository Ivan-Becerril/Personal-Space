using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillFlyingKick
    {
        private const float Duration = 0.54f;
        private const float StrikeStart = 0.17f;
        private const float StrikeEnd = 0.38f;
        private const float HoldPoseTime = 0.37f;
        private readonly Leg lead;
        private readonly Leg trail;
        private float elapsed = Duration;
        private float poseTime = Duration;
        private bool spentJump;
        private bool hit;

        internal bool IsActive => poseTime < Duration;
        internal bool IsStriking => elapsed >= StrikeStart && elapsed < StrikeEnd;
        internal Vector3 Direction { get; private set; }
        internal float Weight => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.10f, poseTime))
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoldPoseTime, Duration, poseTime)));

        private sealed class Leg
        {
            internal readonly Transform Hip;
            internal readonly float UpperLength;
            internal readonly float LowerLength;
            internal readonly float FootLength;
            internal Vector3 Knee;
            internal Vector3 Ankle;
            internal Vector3 Toe;

            internal Leg(Transform hip, Transform knee, Transform ankle, Transform toe)
            {
                Hip = hip;
                UpperLength = Vector3.Distance(hip.position, knee.position);
                LowerLength = Vector3.Distance(knee.position, ankle.position);
                FootLength = toe != null ? Vector3.Distance(ankle.position, toe.position) : 0f;
            }

            internal void Solve(Vector3 reach, Vector3 bend, Vector3 toeDirection)
            {
                Knee = SolveJoint(Hip.position, reach, bend, UpperLength, LowerLength, out Ankle);
                Toe = Ankle + toeDirection.normalized * FootLength;
            }
        }

        internal PillFlyingKick(Animator animator)
        {
            lead = CreateLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            trail = CreateLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
        }

        private static Leg CreateLeg(Animator animator, HumanBodyBones hipBone, HumanBodyBones kneeBone,
            HumanBodyBones ankleBone, HumanBodyBones toeBone)
        {
            Transform hip = animator.GetBoneTransform(hipBone);
            Transform knee = animator.GetBoneTransform(kneeBone);
            Transform ankle = animator.GetBoneTransform(ankleBone);
            return hip != null && knee != null && ankle != null
                ? new Leg(hip, knee, ankle, animator.GetBoneTransform(toeBone)) : null;
        }

        internal void Start(Vector3 direction)
        {
            if (spentJump || lead == null || trail == null)
                return;

            spentJump = true;
            hit = false;
            Direction = direction;
            elapsed = 0f;
            poseTime = 0f;
            UpdateLegs();
        }

        internal void Step(float step, bool held)
        {
            // The hit window ends at the same time even when the pose is held.
            elapsed = Mathf.Min(elapsed + step, Duration);
            float poseLimit = held && poseTime <= HoldPoseTime ? HoldPoseTime : Duration;
            poseTime = Mathf.Min(poseTime + step, poseLimit);
        }

        internal void Cancel()
        {
            elapsed = Duration;
            poseTime = Duration;
        }

        internal void Land()
        {
            Cancel();
            spentJump = false;
        }

        internal bool TryConsumeHit()
        {
            if (!IsStriking || hit)
                return false;
            hit = true;
            return true;
        }

        internal void UpdateLegs()
        {
            if (!IsActive)
                return;

            Vector3 up = Vector3.up;
            float extension = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.055f, 0.23f, poseTime));
            float leadLength = lead.UpperLength + lead.LowerLength;
            float trailLength = trail.UpperLength + trail.LowerLength;
            Vector3 chamber = Direction * 0.22f - up * 0.46f;
            Vector3 extended = Direction * 0.97f - up * 0.18f;
            lead.Solve(Vector3.Lerp(chamber, extended, extension) * leadLength, up,
                Direction - up * 0.18f);
            trail.Solve((-Direction * 0.18f - up * 0.38f) * trailLength, -Direction,
                Direction + up * 0.1f);
        }

        internal void ShapeLeg(HumanBodyBones bone, ref Vector3 start, ref Vector3 end)
        {
            if (!IsActive)
                return;
            float weight = Weight;
            switch (bone)
            {
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.LeftUpperLeg:
                    end = Vector3.Lerp(end, bone == HumanBodyBones.RightUpperLeg ? lead.Knee : trail.Knee, weight);
                    break;
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.LeftLowerLeg:
                    Leg shin = bone == HumanBodyBones.RightLowerLeg ? lead : trail;
                    start = Vector3.Lerp(start, shin.Knee, weight);
                    end = Vector3.Lerp(end, shin.Ankle, weight);
                    break;
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.LeftFoot:
                    Leg foot = bone == HumanBodyBones.RightFoot ? lead : trail;
                    start = Vector3.Lerp(start, foot.Ankle, weight);
                    end = Vector3.Lerp(end, foot.Toe, weight);
                    break;
            }
        }

        internal void GetGuard(PillArmMotion arm, bool leading, out Vector3 elbow, out Vector3 hand)
        {
            float upper = Vector3.Distance(arm.Shoulder, arm.Elbow);
            float lower = Vector3.Distance(arm.Elbow, arm.Hand);
            Vector3 reach = Direction * (leading ? 0.76f : 0.32f) + Vector3.down * 0.18f;
            Vector3 joint = SolveJoint(arm.Shoulder, reach * (upper + lower), Vector3.down,
                upper, lower, out Vector3 wrist);
            elbow = Vector3.Lerp(arm.Elbow, joint, Weight);
            hand = Vector3.Lerp(arm.Hand, wrist, Weight);
        }

        private static Vector3 SolveJoint(Vector3 root, Vector3 reach, Vector3 bend,
            float upper, float lower, out Vector3 tip)
        {
            Vector3 axis = reach.normalized;
            float length = Mathf.Clamp(reach.magnitude, Mathf.Abs(upper - lower) + 0.001f, (upper + lower) * 0.99f);
            float along = (upper * upper - lower * lower + length * length) / (2f * length);
            float offset = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            tip = root + axis * length;
            return root + axis * along + Vector3.ProjectOnPlane(bend, axis).normalized * offset;
        }
    }
}
