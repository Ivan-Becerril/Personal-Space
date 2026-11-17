using UnityEngine;

namespace PersonalSpace
{
    internal sealed class HydraulicTestPress
    {
        private const float RaisedHeight = 4.75f;
        private const float LoweredHeight = 1.12f;
        private const float PlateThickness = 0.34f;
        private const float PlateDiameter = 3.7f;
        private const float BarrelBottom = 5.16f;

        private readonly Transform frame;
        private readonly Rigidbody head;
        private readonly Transform rod;
        private readonly float rodMeshHeight;
        private float height = RaisedHeight;
        private bool lowered;

        public Collider Plate { get; }

        public HydraulicTestPress(Transform parent, Vector3 position)
        {
            frame = new GameObject("Hydraulic Test Press").transform;
            frame.SetParent(parent, false);
            frame.localPosition = position;

            var steel = new Color(0.31f, 0.36f, 0.43f);
            var hazard = new Color(0.96f, 0.57f, 0.17f);
            RagdollTestArena.Block(frame, "Left Press Column", new Vector3(-2.35f, 3.43f, 0f),
                new Vector3(0.45f, 6.86f, 0.55f), steel);
            RagdollTestArena.Block(frame, "Right Press Column", new Vector3(2.35f, 3.43f, 0f),
                new Vector3(0.45f, 6.86f, 0.55f), steel);
            RagdollTestArena.Block(frame, "Press Crossbeam", new Vector3(0f, 6.84f, 0f),
                new Vector3(5.35f, 0.5f, 0.85f), hazard);

            CreateCylinder(frame, "Hydraulic Barrel", new Vector3(0f, 6.01f, 0f), 1.12f, 1.7f, steel);
            rod = CreateCylinder(frame, "Hydraulic Rod", Vector3.zero, 0.48f, 1f,
                new Color(0.63f, 0.69f, 0.72f));
            rodMeshHeight = rod.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;

            var piston = new GameObject("Moving Press Plate");
            piston.transform.SetParent(frame, false);
            piston.transform.localPosition = Vector3.up * height;
            Transform plateShape = CreateCylinder(piston.transform, "Press Face", Vector3.zero,
                PlateDiameter, PlateThickness, hazard);
            int impactLayer = LayerMask.NameToLayer(PillMovementContact.ImpactLayer);
            if (impactLayer >= 0)
                plateShape.gameObject.layer = impactLayer;
            var hull = plateShape.gameObject.AddComponent<MeshCollider>();
            hull.sharedMesh = plateShape.GetComponent<MeshFilter>().sharedMesh;
            hull.convex = true;
            Plate = hull;

            head = piston.AddComponent<Rigidbody>();
            head.isKinematic = true;
            head.useGravity = false;
            head.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            head.interpolation = RigidbodyInterpolation.Interpolate;
            SetRodLength(height);
        }

        public void Toggle() => lowered = !lowered;

        public void Step(float step)
        {
            float target = lowered ? LoweredHeight : RaisedHeight;
            float next = Mathf.MoveTowards(height, target, 1.35f * step);
            if (next == height)
                return;

            height = next;
            head.MovePosition(frame.TransformPoint(Vector3.up * height));
            SetRodLength(height);
        }

        public bool Covers(Vector3 point, float minimumCeilingY)
        {
            Bounds bounds = Plate.bounds;
            if (bounds.min.y >= minimumCeilingY || point.y >= bounds.max.y - 0.02f)
                return false;

            Vector3 offset = Vector3.ProjectOnPlane(point - bounds.center, Vector3.up);
            float radiusWithBodyClearance = bounds.extents.x + 0.25f;
            return offset.sqrMagnitude < radiusWithBodyClearance * radiusWithBodyClearance;
        }

        private void SetRodLength(float plateHeight)
        {
            float plateTop = plateHeight + PlateThickness * 0.5f;
            float length = BarrelBottom - plateTop;
            rod.localPosition = Vector3.up * (plateTop + length * 0.5f);
            Vector3 scale = rod.localScale;
            scale.y = length / rodMeshHeight;
            rod.localScale = scale;
        }

        private static Transform CreateCylinder(Transform parent, string name, Vector3 position,
            float diameter, float height, Color color)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = position;
            Vector3 meshSize = cylinder.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            cylinder.transform.localScale = new Vector3(diameter / meshSize.x, height / meshSize.y, diameter / meshSize.z);

            Collider primitiveCollider = cylinder.GetComponent<Collider>();
            primitiveCollider.enabled = false;
            Object.Destroy(primitiveCollider);
            RagdollTestArena.Tint(cylinder.GetComponent<MeshRenderer>(), color);
            return cylinder.transform;
        }
    }
}
