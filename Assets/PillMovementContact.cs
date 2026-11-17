using UnityEngine;

namespace PersonalSpace
{
    internal sealed class PillMovementContact : MonoBehaviour
    {
        internal const string ImpactLayer = "Fighter Impacts";
        internal AnimatedPillPlayer Fighter { get; private set; }

        internal void Initialize(AnimatedPillPlayer fighter)
        {
            Fighter = fighter;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Fighter.HandleMovementContact(hit);
        }
    }
}
