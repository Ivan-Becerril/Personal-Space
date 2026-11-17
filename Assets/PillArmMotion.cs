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

        internal Vector3 Shoulder { get; private set; }
        internal Vector3 Elbow { get; private set; }
        internal Vector3 Hand { get; private set; }
        internal Vector3 StrikeDirection { get; private set; }
        internal bool IsRetracting { get; private set; }

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

        internal void Update(float step, Vector3 torso, bool duckingUnderPress, bool held, Vector3? physicalShoulder = null)
        {
            Vector3 outward = player.right * side;
            Shoulder = physicalShoulder ?? (shoulderBone.position + outward * (0.055f * sizeScale));
            Vector3 extendedElbow = Shoulder + (elbowBone.position - shoulderBone.position) * ReachScale;
            Vector3 extendedHand = extendedElbow + (handBone.position - elbowBone.position) * ReachScale;
            Vector3 animatedElbow = KeepArmClear(extendedElbow, torso, 0.305f);
            Vector3 targetHand = KeepArmClear(extendedHand, torso, 0.315f);
            float armLength = upperLength + lowerLength;
            StrikeDirection = Vector3.zero;
            if (duckingUnderPress)
                jabTime = JabDuration;
            IsRetracting = jabTime > HoldTime && jabTime < JabDuration;
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
                jabTime = held && jabTime <= HoldTime ? Mathf.Min(jabTime + step, HoldTime) : jabTime + step;
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
