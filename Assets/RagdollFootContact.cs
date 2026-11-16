using UnityEngine;

namespace PersonalSpace
{
    internal sealed class RagdollFootContact : MonoBehaviour
    {
        private float lastSupportTime = float.NegativeInfinity;

        internal bool HasSupport => Time.fixedTime - lastSupportTime <= Time.fixedDeltaTime * 1.5f;

        private void OnCollisionEnter(Collision collision) => RecordSupport(collision);
        private void OnCollisionStay(Collision collision) => RecordSupport(collision);

        private void RecordSupport(Collision collision)
        {
            Rigidbody other = collision.rigidbody;
            if (other != null && other.transform.parent == transform.parent)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.5f)
                {
                    lastSupportTime = Time.fixedTime;
                    return;
                }
            }
        }
    }
}
