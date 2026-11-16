using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillArmMotion
    {
        private const float JabDuration = 0.34f;
        private readonly Transform shoulderBone;
        private readonly Transform elbowBone;
        private readonly Transform handBone;
        private readonly Transform player;
        private readonly float side;
        private readonly float upperLength;
        private readonly float lowerLength;
        private float jabTime = JabDuration;
        private Vector3 localJabDirection;

        internal Vector3 Shoulder { get; private set; }
        internal Vector3 Elbow { get; private set; }
        internal Vector3 Hand { get; private set; }

        internal PillArmMotion(Transform player, Transform shoulder, Transform elbow, Transform hand, float side)
        {
            this.player = player;
            shoulderBone = shoulder;
            elbowBone = elbow;
            handBone = hand;
            this.side = side;
            upperLength = Vector3.Distance(shoulder.position, elbow.position);
            lowerLength = Vector3.Distance(elbow.position, hand.position);
        }

        internal void Jab(Vector3? target)
        {
            if (jabTime < JabDuration)
                return;

            Vector3 world = target.HasValue ? target.Value - shoulderBone.position : player.forward;
            Vector3 local = player.InverseTransformDirection(world);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -55f, 55f);
            float pitch = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) *
                Mathf.Rad2Deg, -24f, 24f);
            localJabDirection = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            jabTime = 0f;
        }

        internal void Update(float step, Vector3 torso, bool crouched)
        {
            Vector3 outward = player.right * side;
            Shoulder = shoulderBone.position + outward * 0.025f;
            Vector3 animatedElbow = KeepArmClear(elbowBone.position, torso, 0.28f);
            Vector3 targetHand = KeepArmClear(handBone.position, torso, 0.285f);
            float armLength = upperLength + lowerLength;
            if (jabTime < JabDuration && !crouched)
            {
                float phase = jabTime / JabDuration;
                Vector3 aim = player.TransformDirection(localJabDirection);
                Vector3 guard = Shoulder + aim * (armLength * 0.45f) + outward * 0.045f;
                Vector3 reach = Shoulder + aim * (armLength * 0.95f);
                float extension = phase < 0.17f ? 0f :
                    phase < 0.48f ? Mathf.SmoothStep(0f, 1f, (phase - 0.17f) / 0.31f) : 1f;
                float blend = phase < 0.17f ? Mathf.SmoothStep(0f, 1f, phase / 0.17f) :
                    phase < 0.58f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (phase - 0.58f) / 0.42f);
                targetHand = KeepArmClear(Vector3.Lerp(targetHand, Vector3.Lerp(guard, reach, extension), blend),
                    torso, 0.285f);
                animatedElbow = Vector3.Lerp(animatedElbow,
                    Shoulder + outward * 0.22f - Vector3.up * 0.19f, blend);
            }

            jabTime += step;
            // Solve for the elbow while keeping the reach inside the two bone lengths.
            Vector3 offset = targetHand - Shoulder;
            float length = Mathf.Clamp(offset.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.01f,
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
            if (front > 0.24f || Mathf.Abs(relative.y) > 0.48f)
                return point;

            Vector3 outward = player.right * side;
            return point + outward * Mathf.Max(0f, clearance - Vector3.Dot(relative, outward));
        }
    }
}
