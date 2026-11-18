using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillClimbing
    {
        private Vector3 landing;
        private Vector3 landingHeading;

        internal bool IsPullingOver { get; private set; }
        internal Vector3 IntoLedge { get; private set; }
        internal Vector3 Swing { get; private set; }
        internal Vector3 Landing => landing;

        internal void Cancel()
        {
            IsPullingOver = false;
            Swing = Vector3.zero;
        }

        internal static bool CanBraceWith(PillArmMotion arm)
            => arm?.HasGrip == true && arm.GripSurface.attachedRigidbody == null;

        internal static bool CanHangFrom(PillArmMotion arm, Vector3 feet)
            => CanBraceWith(arm) && arm.GripPoint.y > feet.y + 0.48f;

        internal bool TryMove(CharacterController capsule, Transform player, Vector3 hipOffset,
            Vector3 hips, float supportedFootHeight, PillArmMotion left, PillArmMotion right,
            Vector3 input, bool hoisting, bool holding, Vector3? shoulder, out Vector3 velocity, out Vector3 heading)
        {
            velocity = Vector3.zero;
            heading = player.forward;
            if (!holding)
            {
                Cancel();
                return false;
            }

            if (!hoisting)
                IsPullingOver = false;

            bool leftHangs = IsPullingOver ? CanBraceWith(left) : CanHangFrom(left, player.position);
            bool rightHangs = IsPullingOver ? CanBraceWith(right) : CanHangFrom(right, player.position);
            if (!leftHangs && !rightHangs)
            {
                Cancel();
                return false;
            }

            PillArmMotion lead = leftHangs && (!rightHangs || left.GripPoint.y >= right.GripPoint.y) ? left : right;
            if (!IsPullingOver)
            {
                Vector3 normal = Vector3.ProjectOnPlane(lead.GripNormal, Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.1f)
                    normal = Vector3.ProjectOnPlane(player.position - lead.GripPoint, Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.1f)
                    normal = -player.forward;

                IntoLedge = -normal;
                Vector3 tangent = Vector3.Cross(Vector3.up, IntoLedge);
                float side = Vector3.Dot(input, tangent);
                Swing = tangent * side;
                float twistLimit = leftHangs && rightHangs ? 43f : 78f;
                heading = Quaternion.AngleAxis(side * twistLimit, Vector3.up) * IntoLedge;

                if (hoisting && Vector3.Dot(input, IntoLedge) > 0.35f && shoulder.HasValue &&
                    shoulder.Value.y >= lead.GripPoint.y - lead.Reach * 0.54f &&
                    TryFindLanding(capsule, lead, IntoLedge, out landing))
                {
                    IsPullingOver = true;
                    landingHeading = IntoLedge;
                }
            }

            Vector3 feetTarget = hips - hipOffset;
            if (IsPullingOver)
            {
                IntoLedge = landingHeading;
                heading = landingHeading;
                Swing = Vector3.zero;
                bool footOnTop = supportedFootHeight >= landing.y - 0.12f;
                bool hipsOverLip = Vector3.Dot(hips - landing, landingHeading) > -capsule.radius * 0.70f;
                if (footOnTop && hipsOverLip)
                {
                    feetTarget.y = Mathf.Max(feetTarget.y, landing.y + 0.045f);
                    if (player.position.y >= landing.y - 0.035f &&
                        Vector3.Dot(player.position - landing, landingHeading) > -0.12f)
                    {
                        left?.ReleaseGrip(true);
                        right?.ReleaseGrip(true);
                        Cancel();
                    }
                }
            }

            // The capsule follows the moving ragdoll. The arm, not this sweep, lifts the hips.
            velocity = Vector3.ClampMagnitude((feetTarget - player.position) * 10f, 4.2f);
            return true;
        }

        private static bool TryFindLanding(CharacterController capsule, PillArmMotion arm, Vector3 intoLedge,
            out Vector3 landingPoint)
        {
            Vector3 anchor = arm.GripPoint;
            Vector3 probe = anchor + intoLedge * (capsule.radius + 0.18f) + Vector3.up * 0.70f;
            bool found = arm.GripSurface.Raycast(new Ray(probe, Vector3.down), out RaycastHit hit, 0.98f);
            landingPoint = found ? hit.point : Vector3.zero;
            return found && hit.normal.y >= 0.65f;
        }
    }
}
