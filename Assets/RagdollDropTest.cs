using UnityEngine;

namespace PersonalSpace
{
    public sealed class RagdollDropTest : MonoBehaviour
    {
        private void Awake()
        {
            var controller = GetComponent<ActiveRagdollController>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Test Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            float floorWidth = controller != null ? RagdollTestArena.FloorWidth : 12f;
            floor.transform.localScale = new Vector3(floorWidth, 0.5f, floorWidth);

            bool passive = controller == null;
            Vector3 bodyPosition = passive ? Vector3.up * 0.55f : Vector3.zero;
            Quaternion bodyRotation = passive ? Quaternion.Euler(0f, 0f, -12f) : Quaternion.identity;
            Transform body = BuildRig(transform, "Test Ragdoll", bodyPosition, bodyRotation,
                out Rigidbody hips, out Rigidbody chest);

            if (controller != null)
                controller.Initialize(body, hips, chest, floor.GetComponent<Collider>());
        }

        internal static Transform BuildRig(Transform parent, string name, Vector3 position, Quaternion rotation,
            out Rigidbody hips, out Rigidbody chest)
        {
            var body = new GameObject(name).transform;
            body.SetParent(parent, false);
            body.SetLocalPositionAndRotation(position, rotation);

            hips = AddPart(body, "Hips", new Vector3(0f, 1.07f, 0f),
                new Vector3(0.48f, 0.26f, 0.28f), 8f);
            chest = AddPart(body, "Chest", new Vector3(0f, 1.46f, 0f),
                new Vector3(0.54f, 0.5f, 0.3f), 12f);
            var head = AddPart(body, "Head", new Vector3(0f, 1.9f, 0f),
                new Vector3(0.28f, 0.32f, 0.28f), 3f);

            Connect(body, chest, hips, new Vector3(0f, 1.21f, 0f), -25f, 25f, 20f, 20f);
            Connect(body, head, chest, new Vector3(0f, 1.73f, 0f), -30f, 30f, 30f, 20f);

            AddLimbs(body, hips, chest, "Left", -1f);
            AddLimbs(body, hips, chest, "Right", 1f);
            return body;
        }

        private static void AddLimbs(Transform body, Rigidbody hips, Rigidbody chest,
            string side, float sign)
        {
            float armX = sign * 0.37f;
            float legX = sign * 0.145f;
            var upperArm = AddPart(body, side + " Upper Arm", new Vector3(armX, 1.42f, 0f),
                new Vector3(0.16f, 0.3f, 0.16f), 2f);
            var forearm = AddPart(body, side + " Forearm", new Vector3(armX, 1.11f, 0.035f),
                new Vector3(0.14f, 0.28f, 0.14f), 1.5f);
            var thigh = AddPart(body, side + " Thigh", new Vector3(legX, 0.79f, 0f),
                new Vector3(0.23f, 0.42f, 0.24f), 4f);
            var shin = AddPart(body, side + " Shin", new Vector3(legX, 0.35f, 0f),
                new Vector3(0.19f, 0.42f, 0.2f), 3f);
            var foot = AddPart(body, side + " Foot", new Vector3(legX, 0.08f, 0.075f),
                new Vector3(0.19f, 0.14f, 0.33f), 1.5f);

            Connect(body, upperArm, chest, new Vector3(sign * 0.29f, 1.56f, 0f),
                -65f, 65f, 45f, 50f);
            Connect(body, forearm, upperArm, new Vector3(armX, 1.27f, 0f),
                -120f, 10f, 0f, 0f);
            Connect(body, thigh, hips, new Vector3(legX, 0.99f, 0f),
                -55f, 45f, 30f, 25f);
            Connect(body, shin, thigh, new Vector3(legX, 0.57f, 0f),
                -5f, 115f, 0f, 0f);
            Connect(body, foot, shin, new Vector3(legX, 0.15f, 0f),
                -25f, 35f, 15f, 15f);
        }

        private static Rigidbody AddPart(Transform parent, string name, Vector3 position,
            Vector3 size, float mass)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;

            var shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.name = "Shape";
            shape.transform.SetParent(part.transform, false);
            shape.transform.localScale = size;

            var rigidbody = part.AddComponent<Rigidbody>();
            rigidbody.mass = mass;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.solverIterations = 12;
            rigidbody.solverVelocityIterations = 4;
            return rigidbody;
        }

        private static void Connect(Transform rig, Rigidbody child, Rigidbody parent,
            Vector3 pivot, float xMin, float xMax, float yLimit, float zLimit)
        {
            var joint = child.gameObject.AddComponent<ConfigurableJoint>();
            Vector3 worldPivot = rig.TransformPoint(pivot);
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = child.transform.InverseTransformPoint(worldPivot);
            joint.connectedAnchor = parent.transform.InverseTransformPoint(worldPivot);
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = yLimit == 0f ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.angularZMotion = zLimit == 0f ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = xMin };
            joint.highAngularXLimit = new SoftJointLimit { limit = xMax };
            joint.angularYLimit = new SoftJointLimit { limit = yLimit };
            joint.angularZLimit = new SoftJointLimit { limit = zLimit };
            joint.enableCollision = false;
        }
    }
}
