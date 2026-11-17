using UnityEngine;

namespace PersonalSpace
{
    internal static class FighterAnatomy
    {
        internal enum Shape { Neck, Chest, UpperArm, Forearm, Thigh, Calf, Foot, Hand }

        // Each ring describes height, width, and depth in normalized mesh space.
        internal static Mesh Create(Shape shape)
        {
            Vector3[] rings;
            switch (shape)
            {
                case Shape.Neck:
                    rings = Profile(0.83f, 1f, 0.94f, 0.81f, 0.77f);
                    break;
                case Shape.Chest:
                    rings = Profile(0.56f, 0.78f, 0.98f, 1f, 0.73f);
                    break;
                case Shape.UpperArm:
                    rings = Profile(0.67f, 1f, 0.89f, 0.72f, 0.56f);
                    break;
                case Shape.Forearm:
                    rings = Profile(0.66f, 1f, 0.83f, 0.57f, 0.46f);
                    break;
                case Shape.Thigh:
                    rings = Profile(0.67f, 1f, 0.91f, 0.72f, 0.59f);
                    break;
                case Shape.Calf:
                    rings = Profile(0.64f, 0.95f, 1f, 0.63f, 0.44f);
                    break;
                case Shape.Foot:
                    rings = Profile(0.65f, 0.77f, 0.85f, 1f, 0.75f);
                    break;
                default:
                    rings = Profile(0.63f, 0.93f, 1f, 0.83f, 0.62f);
                    break;
            }

            const int sides = 24;
            var vertices = new Vector3[rings.Length * sides];
            var triangles = new int[(rings.Length - 1) * sides * 6];
            int index = 0;
            for (int row = 0; row < rings.Length; row++)
            {
                Vector3 ring = rings[row];
                for (int side = 0; side < sides; side++)
                {
                    float angle = 2f * Mathf.PI * side / sides;
                    int current = row * sides + side;
                    vertices[current] = new Vector3(Mathf.Cos(angle) * ring.y * 0.5f, ring.x,
                        Mathf.Sin(angle) * ring.z * 0.5f);
                    if (row == rings.Length - 1)
                        continue;

                    int next = row * sides + (side + 1) % sides;
                    triangles[index++] = current;
                    triangles[index++] = current + sides;
                    triangles[index++] = next + sides;
                    triangles[index++] = current;
                    triangles[index++] = next + sides;
                    triangles[index++] = next;
                }
            }

            var mesh = new Mesh { name = shape + " Anatomy", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3[] Profile(float a, float b, float c, float d, float e)
        {
            return new[]
            {
                Ring(-0.5f, 0f, 0f), Ring(-0.44f, a, a), Ring(-0.28f, b, b),
                Ring(-0.04f, c, c), Ring(0.24f, d, d), Ring(0.43f, e, e), Ring(0.5f, 0f, 0f)
            };
        }

        private static Vector3 Ring(float height, float width, float depth)
        {
            return new Vector3(height, width, depth);
        }
    }
}
