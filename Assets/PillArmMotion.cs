using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillArmMotion
    {
        private const float JabDuration = 0.23f;
        private const float WindupTime = 0.032f;
        private const float ExtensionEnd = 0.12f;
        private const float HoldTime = 0.15f;
        private const float ReachScale = 1.16f;
        private const float TapTime = 0.16f;
        private readonly Transform shoulderBone;
        private readonly Transform elbowBone;
        private readonly Transform handBone;
        private readonly Transform player;
        private readonly float side;
        private readonly float sizeScale;
        private readonly float upperLength;
        private readonly float lowerLength;
        private float jabTime = JabDuration;
        private Vector3 localJabDirection;
        private bool hitConsumed;
        private float heldTime = -1f;
        private bool grabInput;
        private bool waitForRelease;
        private bool latchedThisPress;
        private ConfigurableJoint grip;
        private Collider gripSurface;
        private Vector3 surfacePoint;
        private Vector3 surfaceNormal;
        private float minimumClimbGripHeight;

        internal Vector3 Shoulder { get; private set; }
        internal Vector3 Elbow { get; private set; }
        internal Vector3 Hand { get; private set; }
        internal Vector3 StrikeDirection { get; private set; }
        internal bool IsRetracting { get; private set; }
        internal bool IsReachingUp { get; private set; }
        internal bool GrabHeld => grabInput;
        internal bool WantsGrip => grabInput && !waitForRelease;
        internal float Reach => (upperLength + lowerLength) * 0.99f;
        internal PillBodyContact HeldFighterPart { get; private set; }
        internal bool HasGrip => grip != null && gripSurface != null;
        internal Collider GripSurface => gripSurface;
        internal Vector3 GripPoint => gripSurface.transform.TransformPoint(surfacePoint);
        internal Vector3 GripNormal => gripSurface.transform.TransformDirection(surfaceNormal);

        internal PillArmMotion(Transform player, Transform shoulder, Transform elbow, Transform hand, float side, float sizeScale)
        {
            this.player = player;
            shoulderBone = shoulder;
            elbowBone = elbow;
            handBone = hand;
            this.side = side;
            this.sizeScale = sizeScale;
            upperLength = ReachScale * Vector3.Distance(shoulder.position, elbow.position);
            lowerLength = ReachScale * Vector3.Distance(elbow.position, hand.position);
        }

        internal bool UpdateButton(bool pressed, bool released, bool held, float step)
        {
            if (pressed)
                heldTime = 0f;
            if (heldTime < 0f)
                return false;

            if (!held)
            {
                bool tapped = released && heldTime < TapTime && !latchedThisPress;
                ResetInput();
                return tapped;
            }

            grabInput = true;
            heldTime += step;
            if (grip != null && (gripSurface == null || !gripSurface.enabled || !gripSurface.gameObject.activeInHierarchy))
                ReleaseGrip(true);
            return false;
        }

        internal void ResetInput()
        {
            heldTime = -1f;
            grabInput = false;
            latchedThisPress = false;
            IsReachingUp = false;
            ReleaseGrip(false);
        }

        internal void ReleaseGrip(bool wait)
        {
            if (grip != null)
                Object.Destroy(grip);
            grip = null;
            gripSurface = null;
            HeldFighterPart = null;
            waitForRelease = wait;
        }

        internal void TryGrip(Rigidbody hand, Collider surface, Rigidbody other, Vector3 point, Vector3 normal)
        {
            if (!WantsGrip || HasGrip || surface == null || surface is CharacterController)
                return;
            if (other == null && normal.y > 0.65f && point.y < player.position.y + 0.25f)
                return;
            if (IsReachingUp && point.y < minimumClimbGripHeight)
                return;

            latchedThisPress = true;
            jabTime = JabDuration;
            StrikeDirection = Vector3.zero;
            HeldFighterPart = other != null ? other.GetComponent<PillBodyContact>() : null;
            var joint = hand.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = other;
            joint.anchor = PhysicsTransform(hand).inverse.MultiplyPoint3x4(point);
            joint.connectedAnchor = other != null ? PhysicsTransform(other).inverse.MultiplyPoint3x4(point) : point;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.025f;
            joint.projectionAngle = 12f;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = true;
            joint.breakForce = 2200f;
            grip = joint;
            gripSurface = surface;
            surfacePoint = surface.transform.InverseTransformPoint(point);
            surfaceNormal = surface.transform.InverseTransformDirection(normal);
        }

        internal void PullFromGrip(ConfigurableJoint shoulder, Vector3 intoLedge, bool hoisting, bool pressing,
            float forceLimit)
        {
            if (!HasGrip || shoulder?.connectedBody == null)
                return;

            Rigidbody hand = grip.GetComponent<Rigidbody>();
            Rigidbody chest = shoulder.connectedBody;
            Vector3 palm = PhysicsTransform(hand).MultiplyPoint3x4(grip.anchor);
            Vector3 socket = PhysicsTransform(chest).MultiplyPoint3x4(shoulder.connectedAnchor);
            Vector3 toPalm = palm - socket;
            float distance = toPalm.magnitude;
            if (distance < 0.001f)
                return;

            Vector3 relativeVelocity = hand.GetPointVelocity(palm) - chest.GetPointVelocity(socket);
            Vector3 axis = toPalm / distance;
            Vector3 effort;
            if (pressing)
            {
                // With the palm over the lip, extend the arm and press the chest onto the ledge.
                Vector3 raisedSocket = palm + (Vector3.up * 0.76f + intoLedge * 0.52f) * Reach;
                effort = (raisedSocket - socket) * 1250f + relativeVelocity * 135f;
            }
            else
            {
                float length = Reach * (hoisting ? 0.31f : 0.94f);
                float closingForce = (distance - length) * 1700f + Vector3.Dot(relativeVelocity, axis) * 125f;
                effort = axis * Mathf.Max(0f, closingForce);
            }

            // Both ends of the muscle receive opposite forces. The ledge supplies the reaction.
            effort = Vector3.ClampMagnitude(effort, forceLimit);
            chest.AddForceAtPosition(effort, socket);
            hand.AddForceAtPosition(-effort, palm);
        }

        private static Matrix4x4 PhysicsTransform(Rigidbody body)
            => Matrix4x4.TRS(body.position, body.rotation, body.transform.lossyScale);

        internal void Jab(Vector3? target)
        {
            if (jabTime < JabDuration)
                return;

            Vector3 world = target.HasValue ? target.Value - Shoulder : player.forward;
            Vector3 local = player.InverseTransformDirection(world);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
            float pitch = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) *
                Mathf.Rad2Deg, -70f, 70f);
            localJabDirection = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            jabTime = 0f;
            hitConsumed = false;
        }

        internal bool TryConsumeHit()
        {
            if (hitConsumed || StrikeDirection.sqrMagnitude <= 0f)
                return false;

            hitConsumed = true;
            return true;
        }

        internal void Update(float step, Vector3 torso, bool blockJabs, Vector3? physicalShoulder = null,
            PillGripTargeting targeting = null, Vector3? otherGrip = null, Vector3 intoLedge = default)
        {
            Vector3 outward = player.right * side;
            Shoulder = physicalShoulder ?? (shoulderBone.position + outward * (0.055f * sizeScale));
            Vector3 extendedElbow = Shoulder + (elbowBone.position - shoulderBone.position) * ReachScale;
            Vector3 extendedHand = extendedElbow + (handBone.position - elbowBone.position) * ReachScale;
            Vector3 animatedElbow = KeepArmClear(extendedElbow, torso, 0.305f);
            Vector3 targetHand = KeepArmClear(extendedHand, torso, 0.315f);
            float armLength = upperLength + lowerLength;
            StrikeDirection = Vector3.zero;
            if (blockJabs || HasGrip || (WantsGrip && heldTime >= TapTime))
                jabTime = JabDuration;
            IsRetracting = jabTime > HoldTime && jabTime < JabDuration;
            IsReachingUp = otherGrip.HasValue && !HasGrip && !blockJabs && jabTime >= JabDuration;
            if (IsReachingUp)
            {
                minimumClimbGripHeight = otherGrip.Value.y - 0.06f;
                Vector3 reachDirection = (Vector3.up + intoLedge * 0.50f + outward * 0.11f).normalized;
                targetHand = Shoulder + reachDirection * Reach;
                animatedElbow = Shoulder + outward * (0.23f * sizeScale) + Vector3.up * (0.26f * sizeScale);
                if (WantsGrip && targeting != null &&
                    (targeting.TryFind(Shoulder, player.position.y, intoLedge, Reach, out Vector3 higher, otherGrip.Value.y + 0.10f) ||
                     targeting.TryFind(Shoulder, player.position.y, intoLedge, Reach, out higher, minimumClimbGripHeight)))
                    targetHand = higher;
            }
            else if (WantsGrip && jabTime >= JabDuration)
            {
                float pitch = (HasGrip ? 25f : 42f) * Mathf.Deg2Rad;
                Vector3 reach = player.forward * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
                targetHand = Shoulder + (reach * 0.94f + outward * 0.10f) * armLength;
                animatedElbow = Shoulder + outward * (0.25f * sizeScale) - Vector3.up * (0.22f * sizeScale);
                if (HasGrip && gripSurface.attachedRigidbody == null)
                    targetHand = GripPoint;
                else if (!HasGrip && targeting != null &&
                    targeting.TryFind(Shoulder, player.position.y, player.forward, Reach, out Vector3 nearby))
                    targetHand = nearby;
            }
            if (jabTime < JabDuration)
            {
                Vector3 aim = player.TransformDirection(localJabDirection);
                if (jabTime >= WindupTime && jabTime < HoldTime)
                    StrikeDirection = aim;
                Vector3 guard = Shoulder + aim * (armLength * 0.45f) + outward * (0.045f * sizeScale);
                Vector3 reach = Shoulder + aim * (armLength * 0.95f);
                float extension = Mathf.InverseLerp(WindupTime, ExtensionEnd, jabTime);
                float blend = jabTime < WindupTime ? Mathf.SmoothStep(0f, 1f, jabTime / WindupTime) :
                    1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoldTime, JabDuration, jabTime));
                targetHand = KeepArmClear(Vector3.Lerp(targetHand, Vector3.Lerp(guard, reach, extension), blend),
                    torso, 0.315f);
                animatedElbow = Vector3.Lerp(animatedElbow,
                    Shoulder + (outward * 0.22f - Vector3.up * 0.19f) * sizeScale, blend);
            }

            if (jabTime < JabDuration)
                jabTime += step;
            // Solve for the elbow while keeping the reach inside the two bone lengths.
            Vector3 offset = targetHand - Shoulder;
            float length = Mathf.Clamp(offset.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.01f * sizeScale,
                armLength * 0.99f);
            Vector3 axis = offset.sqrMagnitude > 0.00001f ? offset.normalized : -Vector3.up;
            Vector3 bend = Vector3.ProjectOnPlane(animatedElbow - Shoulder, axis);
            if (bend.sqrMagnitude < 0.001f)
                bend = Vector3.ProjectOnPlane(outward + player.forward * 0.35f, axis);
            float along = (upperLength * upperLength - lowerLength * lowerLength + length * length) / (2f * length);
            float distance = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Elbow = Shoulder + axis * along + bend.normalized * distance;
            Hand = Shoulder + axis * length;
        }

        private Vector3 KeepArmClear(Vector3 point, Vector3 torso, float clearance)
        {
            Vector3 relative = point - torso;
            float front = Vector3.Dot(relative, player.forward);
            if (front > 0.28f * sizeScale || Mathf.Abs(relative.y) > 0.48f * sizeScale)
                return point;

            Vector3 outward = player.right * side;
            return point + outward * Mathf.Max(0f, clearance * sizeScale - Vector3.Dot(relative, outward));
        }
    }
}
