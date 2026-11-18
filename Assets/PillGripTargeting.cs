using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillGripTargeting
    {
        private readonly AnimatedPillPlayer owner;
        private readonly Collider[] nearby = new Collider[64];
        private readonly RaycastHit[] sightHits = new RaycastHit[32];

        internal PillGripTargeting(AnimatedPillPlayer owner) => this.owner = owner;

        internal bool TryFind(Vector3 shoulder, float feetY, Vector3 forward, float reach,
            out Vector3 target, float minimumHeight = float.NegativeInfinity)
        {
            target = Vector3.zero;
            float radius = reach + 1.35f;
            float best = radius * radius;
            bool found = false;
            int count = Physics.OverlapSphereNonAlloc(shoulder, radius, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = nearby[i];
                if (IsIgnored(candidate))
                    continue;

                Vector3 probe = shoulder;
                if (candidate.attachedRigidbody == null)
                {
                    // Aim up toward the lip of a ledge if the shoulder can reach it.
                    probe.y = Mathf.Clamp(candidate.bounds.max.y - 0.04f,
                        shoulder.y, shoulder.y + reach * 0.68f);
                }
                Vector3 point = candidate is MeshCollider mesh && !mesh.convex
                    ? candidate.bounds.ClosestPoint(probe) : candidate.ClosestPoint(probe);
                if (point.y < minimumHeight)
                    continue;

                Vector3 offset = point - shoulder;
                float distance = offset.sqrMagnitude;
                if (distance >= best || distance < 0.0025f ||
                    Vector3.Dot(offset, forward) < -0.15f ||
                    (candidate.attachedRigidbody == null && point.y < feetY + 0.25f))
                    continue;
                if (!CanSee(shoulder, point, candidate))
                    continue;

                best = distance;
                target = point;
                found = true;
            }
            return found;
        }

        private bool IsIgnored(Collider collider)
        {
            if (collider == null || collider is CharacterController)
                return true;

            Rigidbody body = collider.attachedRigidbody;
            return body != null && body.TryGetComponent(out PillBodyContact part) && part.Fighter == owner;
        }

        private bool CanSee(Vector3 origin, Vector3 point, Collider target)
        {
            Vector3 toward = point - origin;
            float distance = toward.magnitude;
            int count = Physics.RaycastNonAlloc(origin, toward / distance, sightHits, distance, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sightHits[i];
                if (hit.collider != target && hit.distance < distance - 0.035f && !IsIgnored(hit.collider))
                    return false;
            }

            return true;
        }
    }
}
