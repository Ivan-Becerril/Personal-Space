using UnityEngine;

namespace PersonalSpace
{
    internal sealed class RagdollWalkingGait
    {
        internal sealed class Leg
        {
            private readonly ConfigurableJoint thigh;
            private readonly ConfigurableJoint shin;
            private readonly ConfigurableJoint foot;
            private readonly ConfigurableJoint arm;
            private readonly Rigidbody footBody;
            private readonly BoxCollider sole;
            private readonly RagdollFootContact contact;
            private readonly float thighLength;
            private readonly float shinLength;

            internal Leg(ConfigurableJoint thigh, ConfigurableJoint shin, ConfigurableJoint foot,
                ConfigurableJoint arm, BoxCollider sole)
            {
                this.thigh = thigh;
                this.shin = shin;
                this.foot = foot;
                this.arm = arm;
                this.sole = sole;
                footBody = foot.GetComponent<Rigidbody>();
                contact = foot.gameObject.AddComponent<RagdollFootContact>();
                thighLength = Vector3.Distance(thigh.anchor, shin.connectedAnchor);
                shinLength = Vector3.Distance(shin.anchor, foot.connectedAnchor);
                AnkleClearance = Ankle.y - sole.bounds.min.y;
            }

            internal bool HasSupport => contact.HasSupport;

            internal float GroundGap(RagdollTestArena arena)
            {
                Bounds bounds = sole.bounds;
                if (arena.Raycast(new Ray(bounds.center, Vector3.down), bounds.extents.y + 0.18f,
                    out RaycastHit hit) && hit.normal.y > 0.5f)
                    return hit.distance - bounds.extents.y;

                return float.PositiveInfinity;
            }
            internal float AnkleClearance { get; }
            internal Vector3 Hip => JointPivot(thigh.connectedBody, thigh.connectedAnchor);
            internal Vector3 Ankle => JointPivot(footBody, foot.anchor);

            internal void Pose(Vector3 ankle, Vector3 heading, Vector3 hipAdvance, float armPitch)
            {
                // Solve the leg for a requested hip and ankle position. The resulting
                // rotations are motor targets so all movement still goes through the joints.
                Vector3 hip = Hip + hipAdvance;
                Vector3 axis = (ankle - hip).normalized;
                if (axis.sqrMagnitude < 0.5f)
                    axis = Vector3.down;

                float span = thighLength + shinLength;
                float distance = Mathf.Clamp(Vector3.Distance(hip, ankle), span * 0.59f, span * 0.994f);
                Vector3 end = hip + axis * distance;
                Vector3 bend = Vector3.ProjectOnPlane(heading, axis).normalized;
                if (bend.sqrMagnitude < 0.5f)
                    bend = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;

                float kneeAlongAxis = (thighLength * thighLength - shinLength * shinLength +
                    distance * distance) / (2f * distance);
                float kneeOut = Mathf.Sqrt(Mathf.Max(0f, thighLength * thighLength - kneeAlongAxis * kneeAlongAxis));
                Vector3 knee = hip + axis * kneeAlongAxis + bend * kneeOut;
                Vector3 right = Vector3.Cross(-axis, bend).normalized;
                Quaternion thighRotation = AlignLeg(hip - knee, right);
                Quaternion shinRotation = AlignLeg(knee - end, right);
                Quaternion footRotation = Quaternion.LookRotation(heading, Vector3.up);

                // All rig bodies share their initial joint orientation. Unity's
                // local target is the inverse of the desired child-to-parent rotation.
                thigh.targetRotation = Quaternion.Inverse(thighRotation) * thigh.connectedBody.rotation;
                shin.targetRotation = Quaternion.Inverse(shinRotation) * thighRotation;
                foot.targetRotation = Quaternion.Inverse(footRotation) * shinRotation;
                arm.targetRotation = Quaternion.Euler(armPitch, 0f, 0f);
            }

            internal void ResetPose()
            {
                thigh.targetRotation = Quaternion.identity;
                shin.targetRotation = Quaternion.identity;
                foot.targetRotation = Quaternion.identity;
                arm.targetRotation = Quaternion.identity;
            }

            private static Quaternion AlignLeg(Vector3 up, Vector3 right)
            {
                return Quaternion.LookRotation(Vector3.Cross(right, up), up);
            }

            private static Vector3 JointPivot(Rigidbody body, Vector3 localAnchor)
            {
                return body.position + body.rotation * localAnchor;
            }
        }

        private const float ReferenceWalkingSpeed = 3.2f;
        private readonly Rigidbody hips;
        private readonly RagdollTestArena arena;
        private readonly WalkMotionReference motion;
        private readonly Leg left;
        private readonly Leg right;
        private Leg stance;
        private Leg swing;
        private Vector3 heading;
        private Vector3 plantedAnkle;
        private Vector3 swingStart;
        private Vector3 swingEnd;
        private float elapsed;
        private float duration;

        internal RagdollWalkingGait(Rigidbody hips, RagdollTestArena arena, Leg left, Leg right,
            WalkMotionReference motion)
        {
            this.hips = hips;
            this.arena = arena;
            this.left = left;
            this.right = right;
            this.motion = motion;
        }

        internal bool Step(Vector3 movement, float requestedSpeed, float deltaTime)
        {
            float speedLimit = requestedSpeed;
            bool wantsStep = movement.sqrMagnitude > 0.01f && speedLimit > 0f;

            if (stance == null)
            {
                if (!wantsStep)
                    return false;

                // A resting foot can be close to the floor without a recent collision callback.
                float leftGap = left.GroundGap(arena);
                float rightGap = right.GroundGap(arena);
                if (!left.HasSupport && !right.HasSupport && Mathf.Min(leftGap, rightGap) > 0.14f)
                    return true;

                bool startRight = right.HasSupport || (!left.HasSupport && rightGap <= leftGap);
                BeginStep(startRight ? right : left, movement, speedLimit);
            }

            elapsed += deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float forwardSpeed = Vector3.Dot(hips.linearVelocity, heading);
            float speedError = Mathf.Clamp01(1f - forwardSpeed / Mathf.Max(0.25f, speedLimit));
            bool stanceIsPlanted = stance.HasSupport || stance.GroundGap(arena) <= 0.025f;
            float push = wantsStep && stanceIsPlanted
                ? Mathf.Lerp(0.07f, 0.28f, progress) * speedError
                : 0f;

            Vector3 rightward = Vector3.Cross(Vector3.up, heading);
            float shift = Mathf.Clamp(Vector3.Dot(plantedAnkle - hips.position, rightward), -0.08f, 0.08f);
            Vector3 stanceAdvance = stanceIsPlanted ? heading * push + rightward * shift : Vector3.zero;

            // A planted leg motor tries to move the hips past the contact. With no
            // ground contact, the leg can't produce a horizontal push on the body.
            stance.Pose(plantedAnkle, heading, stanceAdvance, 18f * progress);
            Vector2 swingPose = motion?.Sample(progress) ?? new Vector2(Mathf.SmoothStep(0f, 1f, progress),
                Mathf.Sin(progress * Mathf.PI));
            Vector3 ankle = Vector3.Lerp(swingStart, swingEnd, swingPose.x);
            ankle.y += swingPose.y * 0.17f;
            swing.Pose(ankle, heading, Vector3.zero, -18f * progress);

            if (progress >= 0.82f && (swing.HasSupport || swing.GroundGap(arena) <= 0.035f))
            {
                if (wantsStep)
                    BeginStep(swing, movement, speedLimit);
                else
                    Stop();
            }
            else if (elapsed > duration + 0.22f)
            {
                // Release unreachable steps so an obstacle or a shove can't pin the gait.
                Stop();
            }

            return wantsStep || stance != null;
        }

        internal void Stop()
        {
            if (stance == null)
                return;

            stance = null;
            swing = null;
            left.ResetPose();
            right.ResetPose();
        }

        private void BeginStep(Leg nextStance, Vector3 movement, float speedLimit)
        {
            stance = nextStance;
            swing = stance == left ? right : left;
            Vector3 facing = Vector3.ProjectOnPlane(hips.rotation * Vector3.forward, Vector3.up).normalized;
            heading = Vector3.RotateTowards(facing, movement.normalized, 38f * Mathf.Deg2Rad, 1f).normalized;
            if (heading.sqrMagnitude < 0.5f)
                heading = movement.normalized;

            float speedFraction = Mathf.Clamp01(speedLimit / ReferenceWalkingSpeed);
            elapsed = 0f;
            duration = Mathf.Lerp(0.62f, motion?.StepDuration ?? 0.38f, speedFraction);
            plantedAnkle = stance.Ankle;
            float gap = stance.GroundGap(arena);
            if (!float.IsInfinity(gap))
                plantedAnkle -= Vector3.up * Mathf.Max(0f, gap);
            swingStart = swing.Ankle;
            float reach = Mathf.Lerp(0.21f, 0.36f, speedFraction);
            float momentumLead = Mathf.Clamp(Vector3.Dot(hips.linearVelocity, heading) * 0.075f, -0.11f, 0.14f);
            Vector3 candidate = swing.Hip + heading * (reach + momentumLead);

            Vector3 sameHeight = new Vector3(candidate.x, plantedAnkle.y, candidate.z);
            var probe = new Ray(sameHeight + Vector3.up * 0.55f, Vector3.down);
            if (arena.Raycast(probe, 1.3f, out RaycastHit ground) &&
                Mathf.Abs(ground.point.y + swing.AnkleClearance - plantedAnkle.y) <= 0.25f)
                swingEnd = ground.point + Vector3.up * swing.AnkleClearance;
            else
                swingEnd = swingStart;
        }
    }
}
