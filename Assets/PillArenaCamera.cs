using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillArenaCamera
    {
        private readonly Camera camera;
        private Vector3 focus;
        private float distance;
        private float pitch = 42f;
        private bool initialized;

        internal float Yaw { get; }

        internal PillArenaCamera(Camera camera)
        {
            this.camera = camera;
            Yaw = camera.transform.eulerAngles.y - 20f;
            camera.fieldOfView = 54f;
        }

        internal void Track(Vector3 primary, Vector3? secondary, RagdollTestArena arena, float step)
        {
            Vector3 separation = secondary.GetValueOrDefault(primary) - primary;
            float weight = 1f - Mathf.InverseLerp(8f, 16f, Vector3.ProjectOnPlane(separation, Vector3.up).magnitude);
            Vector3 desiredFocus = primary + separation * (weight * 0.5f);
            float spread = separation.magnitude * weight;
            float desiredDistance = Mathf.Clamp(9.7f + spread * 0.58f, 9.7f, 15f);
            float blend = initialized ? 1f - Mathf.Exp(-step * 6f) : 1f;
            initialized = true;
            focus = Vector3.Lerp(focus, desiredFocus, blend);
            distance = Mathf.Lerp(distance, desiredDistance, blend);

            // Use a higher view inside the loft when a wall would force a close-up.
            float low = ClearDistance(arena, 42f, distance);
            float high = ClearDistance(arena, 61f, distance);
            float desiredPitch = low < distance - 0.5f && high > low + 0.65f ? 61f : 42f;
            pitch = Mathf.Lerp(pitch, desiredPitch, blend);

            Quaternion view = Quaternion.Euler(pitch, Yaw, 0f);
            float clear = ClearDistance(arena, pitch, distance);
            camera.transform.SetPositionAndRotation(focus + view * Vector3.back * clear, view);
        }

        private float ClearDistance(RagdollTestArena arena, float tilt, float wanted)
        {
            Vector3 back = Quaternion.Euler(tilt, Yaw, 0f) * Vector3.back;
            return arena.RaycastCamera(new Ray(focus, back), wanted, out RaycastHit hit)
                ? Mathf.Max(0.25f, hit.distance - 0.22f) : wanted;
        }
    }
}
