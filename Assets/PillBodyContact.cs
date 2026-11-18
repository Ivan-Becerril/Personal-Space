using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillBodyContact : MonoBehaviour
    {
        internal AnimatedPillPlayer Fighter { get; private set; }
        internal AnimatedPillPlayer.BodyPart Part { get; private set; }
        private PillArmMotion fist;

        internal void Initialize(AnimatedPillPlayer fighter, AnimatedPillPlayer.BodyPart part, PillArmMotion fist)
        {
            Fighter = fighter;
            Part = part;
            this.fist = fist;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Fighter != null && Part != null)
                Fighter.HandleBodyContact(Part, fist, collision, true);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (Fighter != null && Part != null && (collision.rigidbody != null || fist?.WantsGrip == true))
                Fighter.HandleBodyContact(Part, fist, collision, false);
        }
    }
}
