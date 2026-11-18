using System.Collections.Generic;
using UnityEngine;
using Part = PersonalSpace.AnimatedPillPlayer.BodyPart;

namespace PersonalSpace
{
    internal sealed class FighterAnatomy
    {
        private enum Finish { Jersey, DarkPanel, Accent, Skin, Trousers, Shoe, Sole }
        private const int Sides = 16;
        private readonly Transform root;
        private readonly List<Part> parts;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<BoneWeight> weights = new List<BoneWeight>();
        private readonly List<int>[] triangles = new List<int>[7];
        private readonly List<Transform> bones = new List<Transform>();

        private struct Ring
        {
            internal Vector3 Center;
            internal float Width;
            internal float Depth;
            internal BoneWeight Weight;
            internal Finish Finish;
        }

        private FighterAnatomy(Transform root, List<Part> parts)
        {
            this.root = root;
            this.parts = parts;
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new List<int>();
        }

        internal static SkinnedMeshRenderer Build(Transform player, List<Part> parts, bool dummy)
        {
            var chest = parts.Find(part => part.Bone == HumanBodyBones.Hips && part.End != null);
            if (chest?.Parent == null)
                return null;

            var shell = new GameObject("Fighter Model");
            shell.transform.SetParent(player, false);
            var model = new FighterAnatomy(shell.transform, parts);
            model.Torso(chest, chest.Parent);
            model.Limb(true, true);
            model.Limb(true, false);
            model.Limb(false, true);
            model.Limb(false, false);
            model.Head();
            return model.CreateRenderer(dummy);
        }

        private void Torso(Part chest, Part hips)
        {
            Vector3 basePoint = hips.Body.position;
            Vector3 collar = chest.End.position;
            Vector3 neckAxis = (collar - basePoint).normalized;
            Part leftShoulder = Find(HumanBodyBones.LeftUpperArm);
            Part rightShoulder = Find(HumanBodyBones.RightUpperArm);
            if (leftShoulder != null && rightShoulder != null)
            {
                Vector3 shoulders = (Anchor(leftShoulder) + Anchor(rightShoulder)) * .5f;
                float shoulderRise = Vector3.Dot(shoulders - collar, neckAxis);
                collar += neckAxis * Mathf.Max(0f, shoulderRise - .045f);
            }
            float w = chest.Width;
            float d = chest.Depth;
            Part neck = Find(HumanBodyBones.Neck);
            Part head = Find(HumanBodyBones.Head);
            Vector3 throat = head != null ? head.Body.position : collar + neckAxis * 0.28f;

            Vector3 Waist(float t) => Vector3.Lerp(basePoint, collar, t);
            var rings = new[]
            {
                Profile(basePoint - neckAxis * 0.15f, hips.Width * .86f, hips.Depth * .78f, hips, Finish.Trousers),
                Profile(Waist(-.10f), hips.Width * 1.02f, hips.Depth * .93f, hips, Finish.Trousers),
                Profile(Waist(.04f), w * .81f, d * .92f, hips, Finish.DarkPanel, chest, .16f),
                Profile(Waist(.10f), w * .79f, d * .88f, hips, Finish.Accent, chest, .26f),
                Profile(Waist(.16f), w * .77f, d * .85f, hips, Finish.Jersey, chest, .39f),
                Profile(Waist(.40f), w * .85f, d * .88f, chest, Finish.Jersey, hips, .26f),
                Profile(Waist(.66f), w * .98f, d, chest, Finish.Jersey),
                Profile(Waist(.84f), w * 1.05f, d * 1.04f, chest, Finish.Jersey),
                Profile(Waist(.98f), w * .94f, d * .82f, chest, Finish.Jersey, neck, .12f),
                Profile(Vector3.Lerp(collar, throat, .21f), w * .44f, d * .56f, chest, Finish.DarkPanel, neck, .45f),
                Profile(Vector3.Lerp(collar, throat, .38f), w * .33f, d * .51f, neck ?? chest, Finish.Skin),
                Profile(Vector3.Lerp(collar, throat, .77f), w * .29f, d * .48f, neck ?? chest, Finish.Skin, head, .30f),
                Profile(Vector3.Lerp(collar, throat, .94f), w * .25f, d * .44f, head ?? neck ?? chest, Finish.Skin)
            };

            Tube(rings, root.right, torsoPanels: true);
        }

        private void Limb(bool arm, bool left)
        {
            Part upper = Find(arm
                ? (left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm)
                : (left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg));
            Part lower = Find(arm
                ? (left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm)
                : (left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg));
            Part tip = Find(arm
                ? (left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)
                : (left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot));
            if (upper == null || lower == null || tip == null)
                return;

            Vector3 origin = Anchor(upper);
            Vector3 joint = Anchor(lower);
            Vector3 wrist = Anchor(tip);
            Vector3 endDirection = (wrist - joint).normalized;
            Vector3 inset = arm && upper.Parent != null
                ? Vector3.Lerp(upper.Parent.Body.position, origin, .42f) : origin + Vector3.up * .085f;
            Vector3 Upper(float t) => Vector3.Lerp(origin, joint, t);
            Vector3 Lower(float t) => Vector3.Lerp(joint, wrist, t);
            Finish coat = arm ? Finish.Jersey : Finish.Trousers;
            Finish cuff = arm ? Finish.Accent : Finish.DarkPanel;
            float width = upper.Width;
            float calf = lower.Width;
            var rings = new List<Ring>
            {
                // Start each sleeve or leg inside its parent and give it a shared patch of skin.
                Profile(inset, width * .66f, upper.Depth * .68f, upper.Parent ?? upper, coat),
                Profile(Vector3.Lerp(inset, origin, .62f), width * .88f, upper.Depth * .86f, upper, coat, upper.Parent, .78f),
                Profile(Upper(.04f), width * .96f, upper.Depth * .94f, upper, coat, upper.Parent, .44f),
                Profile(Upper(.24f), width * 1.04f, upper.Depth * .95f, upper, coat),
                Profile(Upper(.53f), width * .94f, upper.Depth * .92f, upper, coat),
                Profile(Upper(.81f), width * .74f, upper.Depth * .78f, upper, coat, lower, .14f),
                Profile(joint, calf * .86f, lower.Depth * .86f, upper, coat, lower, .50f),
                Profile(Lower(.19f), calf * .99f, lower.Depth * .94f, lower, coat, upper, .16f),
                Profile(Lower(.45f), calf * (arm ? .92f : 1.02f), lower.Depth * .91f, lower, coat),
                Profile(Lower(.73f), calf * .73f, lower.Depth * .76f, lower, coat),
                Profile(Lower(.88f), calf * .64f, lower.Depth * .72f, lower, cuff, tip, .12f),
                Profile(wrist, calf * .61f, lower.Depth * .69f, lower, cuff, tip, .50f)
            };

            if (arm)
            {
                float hand = tip.Width;
                rings.Add(Profile(wrist + endDirection * (hand * .15f), hand * .86f, tip.Depth * .90f, tip, Finish.DarkPanel, lower, .19f));
                rings.Add(Profile(wrist + endDirection * (hand * .32f), hand * .92f, tip.Depth * .94f, tip, Finish.DarkPanel));
                rings.Add(Profile(wrist + endDirection * (hand * .47f), hand * .73f, tip.Depth * .78f, tip, Finish.DarkPanel));
                rings.Add(Profile(wrist + endDirection * (hand * .53f), hand * .42f, tip.Depth * .44f, tip, Finish.DarkPanel));
            }

            if (!arm)
                rings.Add(Profile(wrist - root.up * 0.055f, calf * .58f, lower.Depth * .63f, tip, Finish.Shoe));
            Tube(rings, root.forward);
            if (!arm)
                Shoe(tip, wrist);
        }

        private void Shoe(Part foot, Vector3 ankle)
        {
            Vector3 toe = foot.End != null ? foot.End.position : ankle + root.forward * .2f;
            Vector3 heading = Vector3.ProjectOnPlane(toe - ankle, root.up).normalized;
            if (heading.sqrMagnitude < .01f)
                heading = root.forward;
            float length = Mathf.Max(.19f, Vector3.Dot(toe - ankle, heading));
            float forefoot = foot.Width;
            Vector3 Center(float t) => ankle + heading * (t * length) - root.up * .065f;

            var rings = new[]
            {
                Profile(Center(-.46f), forefoot * .66f, foot.Depth * .55f, foot, Finish.Shoe),
                Profile(Center(-.27f), forefoot * .93f, foot.Depth * .82f, foot, Finish.Shoe),
                Profile(Center(.27f), forefoot * .95f, foot.Depth * .74f, foot, Finish.Shoe),
                Profile(Center(.87f), forefoot, foot.Depth * .59f, foot, Finish.Shoe),
                Profile(Center(1.16f), forefoot * .80f, foot.Depth * .50f, foot, Finish.Shoe),
                Profile(Center(1.25f), forefoot * .47f, foot.Depth * .32f, foot, Finish.Shoe)
            };
            Tube(rings, Vector3.Cross(root.up, heading), shoeSole: true);
        }

        private void Head()
        {
            Part head = Find(HumanBodyBones.Head);
            if (head == null)
                return;

            float diameter = head.Width;
            var rings = new Ring[11];
            for (int i = 0; i < rings.Length; i++)
            {
                float latitude = Mathf.Lerp(-1.44f, 1.44f, i / (float)(rings.Length - 1));
                Vector3 center = head.Body.position + root.up * (Mathf.Sin(latitude) * diameter * .5f);
                float width = Mathf.Cos(latitude) * diameter;
                rings[i] = Profile(center, width, width, head, Finish.Skin);
            }
            Tube(rings, root.right);
        }

        private Part Find(HumanBodyBones bone) => parts.Find(part => part.Bone == bone);
        private static Vector3 Anchor(Part part) => part.Joint != null
            ? part.Body.transform.TransformPoint(part.Joint.anchor) : part.Body.position;

        private Ring Profile(Vector3 center, float width, float depth, Part primary, Finish finish,
            Part secondary = null, float secondaryWeight = 0f)
        {
            int primaryIndex = Bone(primary);
            int secondaryIndex = secondary != null ? Bone(secondary) : primaryIndex;
            float blend = secondaryIndex != primaryIndex ? secondaryWeight : 0f;
            var weight = blend > .5f
                ? new BoneWeight { boneIndex0 = secondaryIndex, boneIndex1 = primaryIndex, weight0 = blend, weight1 = 1f - blend }
                : new BoneWeight { boneIndex0 = primaryIndex, boneIndex1 = secondaryIndex, weight0 = 1f - blend, weight1 = blend };
            return new Ring { Center = center, Width = width, Depth = depth, Weight = weight, Finish = finish };
        }

        private int Bone(Part part)
        {
            Transform bone = part.Body.transform;
            int index = bones.IndexOf(bone);
            if (index >= 0)
                return index;
            bones.Add(bone);
            return bones.Count - 1;
        }

        private void Tube(IReadOnlyList<Ring> rings, Vector3 side, bool torsoPanels = false, bool shoeSole = false)
        {
            int start = vertices.Count;
            for (int row = 0; row < rings.Count; row++)
            {
                Ring ring = rings[row];
                Vector3 from = rings[Mathf.Max(0, row - 1)].Center;
                Vector3 to = rings[Mathf.Min(rings.Count - 1, row + 1)].Center;
                Vector3 axis = (to - from).normalized;
                if (axis.sqrMagnitude < .0001f)
                    axis = root.up;
                side = Vector3.ProjectOnPlane(side, axis).normalized;
                if (side.sqrMagnitude < .01f)
                    side = Vector3.ProjectOnPlane(root.right, axis).normalized;
                if (side.sqrMagnitude < .01f)
                    side = Vector3.ProjectOnPlane(root.forward, axis).normalized;
                Vector3 depth = Vector3.Cross(side, axis);
                for (int column = 0; column < Sides; column++)
                {
                    float angle = column * (2f * Mathf.PI / Sides);
                    Vector3 point = ring.Center + side * (ring.Width * .5f * Mathf.Cos(angle))
                        + depth * (ring.Depth * .5f * Mathf.Sin(angle));
                    vertices.Add(root.InverseTransformPoint(point));
                    weights.Add(ring.Weight);
                    if (row >= rings.Count - 1)
                        continue;

                    int current = start + row * Sides + column;
                    int next = start + row * Sides + (column + 1) % Sides;
                    float tileAngle = (column + .5f) * (2f * Mathf.PI / Sides);
                    Finish finish = Surface(ring.Finish, tileAngle, torsoPanels, shoeSole);
                    Triangle(finish, current, current + Sides, next + Sides);
                    Triangle(finish, current, next + Sides, next);
                }
            }
            Cap(rings[0], start, false);
            Cap(rings[rings.Count - 1], start + (rings.Count - 1) * Sides, true);
        }

        private static Finish Surface(Finish finish, float angle, bool torsoPanels, bool shoeSole)
        {
            if (shoeSole && Mathf.Sin(angle) > .51f)
                return Finish.Sole;
            if (torsoPanels && finish == Finish.Jersey && Mathf.Abs(Mathf.Cos(angle)) > .79f)
                return Finish.DarkPanel;
            return finish;
        }

        private void Cap(Ring ring, int start, bool end)
        {
            int center = vertices.Count;
            vertices.Add(root.InverseTransformPoint(ring.Center));
            weights.Add(ring.Weight);
            for (int i = 0; i < Sides; i++)
            {
                int current = start + i;
                int next = start + (i + 1) % Sides;
                Triangle(ring.Finish, center, end ? next : current, end ? current : next);
            }
        }

        private void Triangle(Finish finish, int a, int b, int c)
        {
            var list = triangles[(int)finish];
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        private SkinnedMeshRenderer CreateRenderer(bool dummy)
        {
            var mesh = new Mesh { name = "Personal Space Fighter", subMeshCount = triangles.Length };
            mesh.SetVertices(vertices);
            mesh.boneWeights = weights.ToArray();
            var bindPoses = new Matrix4x4[bones.Count];
            for (int i = 0; i < bones.Count; i++)
                bindPoses[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bindPoses;
            for (int i = 0; i < triangles.Length; i++)
                mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var renderer = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = bones.ToArray();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = MakeMaterials(dummy);
            renderer.localBounds = new Bounds(Vector3.up * 1.25f, Vector3.one * 7f);
            return renderer;
        }

        private static Material[] MakeMaterials(bool dummy)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Color32[] palette =
            {
                dummy ? new Color32(146, 96, 207, 255) : new Color32(36, 155, 144, 255),
                dummy ? new Color32(72, 47, 100, 255) : new Color32(28, 71, 76, 255),
                dummy ? new Color32(241, 180, 93, 255) : new Color32(243, 125, 83, 255),
                new Color32(191, 128, 83, 255),
                new Color32(44, 54, 70, 255),
                new Color32(34, 45, 60, 255),
                new Color32(229, 218, 184, 255)
            };
            var materials = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                var material = new Material(shader) { name = ((Finish)i).ToString(), color = palette[i] };
                float shine = i == (int)Finish.Skin ? .30f : .19f;
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", shine);
                else if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", shine);
                materials[i] = material;
            }
            return materials;
        }
    }
}
