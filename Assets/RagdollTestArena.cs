using UnityEngine;

namespace PersonalSpace
{
    internal sealed class RagdollTestArena
    {
        public const float FloorWidth = 72f;
        private const float RailDistance = 34f;
        public static readonly Vector3 DummyPosition = new Vector3(-5f, 0f, 5f);

        private readonly Collider floor;
        private readonly HydraulicTestPress press;
        private readonly Collider[] obstacles;
        private readonly Collider[] rails;

        public RagdollTestArena(Collider floor)
        {
            this.floor = floor;
            Transform parent = floor.transform.parent;
            var slate = new Color(0.24f, 0.31f, 0.41f);
            var coral = new Color(0.91f, 0.36f, 0.3f);
            var yellow = new Color(0.98f, 0.73f, 0.24f);
            var blue = new Color(0.38f, 0.62f, 0.93f);
            var violet = new Color(0.55f, 0.45f, 0.73f);
            Tint(floor.GetComponent<MeshRenderer>(), slate);

            // Keep the center open for sprinting. The step and jump lanes branch off from spawn.
            obstacles = new[]
            {
                Platform(parent, "Coral Threshold", -12f, 4.5f, 3f, 0.08f, 0.55f, coral),
                Platform(parent, "Yellow Step", -12f, 7f, 3f, 0.15f, 1f, yellow),
                Platform(parent, "Blue Step", -12f, 10f, 3f, 0.22f, 1.6f, blue),
                Platform(parent, "Balance Strip", -12f, 15f, 0.8f, 0.25f, 4f, violet),
                Platform(parent, "Low Hurdle", 8f, 5f, 3.7f, 0.32f, 0.55f, coral),
                Platform(parent, "Tall Hurdle", 8f, 12.5f, 3.7f, 0.68f, 0.55f, yellow),
                Platform(parent, "Takeoff Platform", 8f, 19f, 4f, 0.36f, 4f, blue),
                Platform(parent, "Landing Platform", 8f, 27.2f, 4.8f, 0.52f, 7.8f, violet)
            };

            var lane = new Color(0.35f, 0.46f, 0.56f);
            var takeoff = new Color(0.95f, 0.79f, 0.39f);
            for (int z = -28; z <= 28; z += 4)
            {
                Paint(parent, "Runway Marker", new Vector3(-1.9f, 0f, z), new Vector2(0.08f, 1.7f), lane);
                Paint(parent, "Runway Marker", new Vector3(1.9f, 0f, z), new Vector2(0.08f, 1.7f), lane);
            }
            Paint(parent, "Jump Lane Marker", new Vector3(8f, 0f, 2.4f), new Vector2(3.7f, 0.24f), takeoff);
            Paint(parent, "Jump Lane Marker", new Vector3(8f, 0f, 9.5f), new Vector2(3.7f, 0.24f), takeoff);
            Paint(parent, "Practice Dummy Pad", DummyPosition, new Vector2(3f, 3f), new Color(0.43f, 0.36f, 0.62f));

            var rail = new Color(0.44f, 0.52f, 0.65f);
            const float railHeight = 1.8f;
            float edgeLength = 2f * RailDistance;
            rails = new[]
            {
                Block(parent, "North Rail", new Vector3(0f, railHeight / 2f, RailDistance),
                    new Vector3(edgeLength + 0.3f, railHeight, 0.3f), rail),
                Block(parent, "South Rail", new Vector3(0f, railHeight / 2f, -RailDistance),
                    new Vector3(edgeLength + 0.3f, railHeight, 0.3f), rail),
                Block(parent, "East Rail", new Vector3(RailDistance, railHeight / 2f, 0f),
                    new Vector3(0.3f, railHeight, edgeLength), rail),
                Block(parent, "West Rail", new Vector3(-RailDistance, railHeight / 2f, 0f),
                    new Vector3(0.3f, railHeight, edgeLength), rail)
            };

            var pressPosition = new Vector3(8f, 0f, -6.5f);
            Paint(parent, "Press Test Pad", pressPosition, new Vector2(5f, 4.8f), new Color(0.72f, 0.32f, 0.24f));
            press = new HydraulicTestPress(parent, pressPosition);
        }

        public RagdollTestArena(Collider floor, Collider[] obstacles, Transform pressSpawn)
        {
            this.floor = floor;
            this.obstacles = obstacles ?? System.Array.Empty<Collider>();
            rails = System.Array.Empty<Collider>();
            press = new HydraulicTestPress(pressSpawn, Vector3.zero);
        }

        public void TogglePress() => press.Toggle();
        public void StepPress(float step) => press.Step(step);
        public bool HasStandingClearance(Vector3 ground, float clearance = 2.1f) => !press.Covers(ground, ground.y + clearance);
        public bool PressIsLowOver(Vector3 position, float clearance = 1.28f) => press.Covers(position, floor.bounds.max.y + clearance);

        public bool IsRecoverySpotClear(Vector3 point, float clearance = 2.1f)
        {
            return InsideRails(point) && ClearOfObstacles(point) && HasStandingClearance(point, clearance);
        }

        public bool Raycast(Ray ray, float distance, out RaycastHit nearest)
        {
            bool found = floor.Raycast(ray, out nearest, distance);
            foreach (Collider obstacle in obstacles)
            {
                if (obstacle.Raycast(ray, out RaycastHit hit, found ? nearest.distance : distance))
                {
                    nearest = hit;
                    found = true;
                }
            }
            if (press.Plate.Raycast(ray, out RaycastHit plate, found ? nearest.distance : distance))
            {
                nearest = plate;
                found = true;
            }
            return found;
        }

        public bool RaycastCamera(Ray ray, float distance, out RaycastHit nearest)
        {
            bool found = Raycast(ray, distance, out nearest);
            foreach (Collider rail in rails)
            {
                if (rail.Raycast(ray, out RaycastHit hit, found ? nearest.distance : distance))
                {
                    nearest = hit;
                    found = true;
                }
            }
            return found;
        }

        public Vector3 GetClearRecoverySpot(Vector3 ground, float clearance = 2.1f)
        {
            // Prefer room to get up beside a step or rail instead of through it.
            if (IsRecoverySpotClear(ground, clearance))
                return ground;

            for (float radius = 0.45f; radius <= 2.7f; radius += 0.45f)
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 offset = Quaternion.Euler(0f, direction * 45f, 0f) * Vector3.forward * radius;
                    Vector3 candidate = ground + offset;
                    if (Raycast(new Ray(candidate + Vector3.up, Vector3.down), 2f, out RaycastHit hit) &&
                        IsRecoverySpotClear(hit.point, clearance))
                        return hit.point;
                }
            }
            return ground;
        }

        private static bool InsideRails(Vector3 point)
        {
            return Mathf.Abs(point.x) < RailDistance - 0.65f && Mathf.Abs(point.z) < RailDistance - 0.65f;
        }

        private bool ClearOfObstacles(Vector3 point)
        {
            foreach (Collider obstacle in obstacles)
            {
                Bounds bounds = obstacle.bounds;
                bool standsOnTop = Mathf.Abs(point.y - bounds.max.y) < 0.025f &&
                    point.x > bounds.min.x + 0.5f && point.x < bounds.max.x - 0.5f &&
                    point.z > bounds.min.z + 0.45f && point.z < bounds.max.z - 0.45f;
                if (standsOnTop)
                    continue;

                bounds.Expand(new Vector3(1.05f, 0f, 1.05f));
                if (point.x >= bounds.min.x && point.x <= bounds.max.x &&
                    point.z >= bounds.min.z && point.z <= bounds.max.z)
                    return false;
            }
            return true;
        }

        private static Collider Platform(Transform parent, string name, float x, float z,
            float width, float height, float depth, Color color)
        {
            return Block(parent, name, new Vector3(x, height / 2f, z), new Vector3(width, height, depth), color);
        }

        private static void Paint(Transform parent, string name, Vector3 position, Vector2 size, Color color)
        {
            Collider stripe = Block(parent, name, position + Vector3.up * 0.004f,
                new Vector3(size.x, 0.008f, size.y), color);
            stripe.enabled = false;
            Object.Destroy(stripe);
        }

        internal static Collider Block(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            Tint(block.GetComponent<MeshRenderer>(), color);
            return block.GetComponent<Collider>();
        }

        internal static void Tint(MeshRenderer renderer, Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
