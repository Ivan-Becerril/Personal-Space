using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PersonalSpace
{
    internal sealed class WalkMotionReference
    {
        private const int Samples = 48;
        private readonly Vector2[] swing;
        internal float StepDuration { get; }

        private WalkMotionReference(Vector2[] swing, float stepDuration)
        {
            this.swing = swing;
            StepDuration = stepDuration;
        }

        internal Vector2 Sample(float progress)
        {
            float index = Mathf.Clamp01(progress) * (swing.Length - 1);
            int low = Mathf.FloorToInt(index);
            return Vector2.Lerp(swing[low], swing[Mathf.Min(low + 1, swing.Length - 1)], index - low);
        }

        internal static WalkMotionReference Create(GameObject model, AnimationClip clip)
        {
            if (model == null || clip == null || clip.length <= 0f)
                return null;

            GameObject reference = Object.Instantiate(model, new Vector3(0f, -80f, 0f), Quaternion.identity);
            reference.name = "Walk Motion Reference";
            foreach (Renderer renderer in reference.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;

            var animator = reference.GetComponent<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            {
                Debug.LogWarning("The walk reference needs a valid Humanoid avatar.");
                Object.Destroy(reference);
                return null;
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Sample Human Basic Motions Walk");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                var output = AnimationPlayableOutput.Create(graph, "Walk", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0f);
                Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (foot == null)
                    return null;

                var frames = new Vector2[Samples];
                int peak = 0;
                for (int i = 0; i < Samples; i++)
                {
                    playable.SetTime(clip.length * i / Samples);
                    graph.Evaluate(0f);
                    Vector3 position = reference.transform.InverseTransformPoint(foot.position);
                    frames[i] = new Vector2(position.z, position.y);
                    if (frames[i].y > frames[peak].y)
                        peak = i;
                }
                return ExtractSwing(frames, peak, clip.length);
            }
            finally
            {
                graph.Destroy();
                Object.Destroy(reference);
            }
        }

        private static WalkMotionReference ExtractSwing(Vector2[] frames, int peak, float cycleDuration)
        {
            int halfSwing = Mathf.RoundToInt(Samples * 0.22f);
            int first = (peak - halfSwing + Samples) % Samples;
            int last = (peak + halfSwing) % Samples;
            float forwardTravel = frames[last].x - frames[first].x;
            float ground = Mathf.Max(frames[first].y, frames[last].y);
            float lift = frames[peak].y - ground;

            if (lift < 0.01f)
            {
                Debug.LogWarning("No usable foot lift was found in the selected walk clip.");
                return null;
            }

            var swing = new Vector2[halfSwing * 2 + 1];
            float sweep = 0f;
            for (int i = 0; i < swing.Length; i++)
            {
                Vector2 sample = frames[(first + i) % Samples];
                float progress = (float)i / (swing.Length - 1);
                float clipSweep = Mathf.Abs(forwardTravel) >= 0.05f
                    ? Mathf.Clamp01((sample.x - frames[first].x) / forwardTravel)
                    : Mathf.SmoothStep(0f, 1f, progress);
                sweep = Mathf.Max(sweep, clipSweep);
                swing[i] = new Vector2(sweep, Mathf.Clamp01((sample.y - ground) / lift));
            }
            swing[0] = Vector2.zero;
            swing[swing.Length - 1] = Vector2.right;
            return new WalkMotionReference(swing, Mathf.Clamp(cycleDuration * 0.5f, 0.35f, 0.65f));
        }
    }
}
