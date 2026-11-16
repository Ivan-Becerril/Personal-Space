using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace PersonalSpace
{
    public sealed class AnimatedPillPlayer : MonoBehaviour
    {
        [SerializeField] private GameObject motionModel = null;
        [SerializeField] private AnimationClip idle = null;
        [SerializeField] private AnimationClip walk = null;
        [SerializeField] private AnimationClip run = null;
        [SerializeField] private AnimationClip jump = null;

        private const float StandingHeight = 1.85f;
        private const float CrouchHeight = 0.49f;
        private CharacterController capsule;
        private RagdollTestArena arena;
        private ActiveRagdollController dummy;
        private Camera followCamera;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float yaw;
        private float pitch = 18f;
        private float cameraDistance = 5f;
        private Transform player;
        private Transform motionRoot;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[4];
        private int currentClip;
        private bool jumping;
        private bool crouched;
        private PillArmMotion leftArm;
        private PillArmMotion rightArm;
        private BodyPart torso;
        private readonly System.Collections.Generic.List<BodyPart> parts =
            new System.Collections.Generic.List<BodyPart>();

        private sealed class BodyPart
        {
            internal HumanBodyBones Bone;
            internal Transform Start;
            internal Rigidbody Body;
            internal Collider Contact;
            internal Transform End;
            internal Transform Visual;
            internal float Width;
            internal float Depth;
        }

        private void Awake()
        {
            Collider floor = RagdollTestArena.Block(transform, "Test Floor", new Vector3(0f, -0.25f, 0f),
                new Vector3(RagdollTestArena.FloorWidth, 0.5f, RagdollTestArena.FloorWidth), Color.gray);
            arena = new RagdollTestArena(floor);
            dummy = ActiveRagdollController.SpawnPracticeDummy(transform, arena, floor);
            player = new GameObject("Pill Player").transform;
            player.SetParent(transform, false);
            capsule = player.gameObject.AddComponent<CharacterController>();
            capsule.skinWidth = 0.025f;
            capsule.minMoveDistance = 0f;
            SetHeight(StandingHeight);
            followCamera = Camera.main;
            yaw = followCamera != null ? followCamera.transform.eulerAngles.y : 0f;
            BuildAppearance();
            torso = parts.Find(part => part.Bone == HumanBodyBones.Hips && part.End != null);
            if (torso != null)
            {
                leftArm = CreateArm(true);
                rightArm = CreateArm(false);
                UpdateArmTargets(0f);
            }
            BuildPhysicalBody();
        }

        private void Update()
        {
            float step = Time.deltaTime;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.middleButton.isPressed)
                {
                    Vector2 drag = mouse.delta.ReadValue();
                    yaw += drag.x * 0.18f;
                    pitch = Mathf.Clamp(pitch - drag.y * 0.18f, 8f, 72f);
                }
                cameraDistance = Mathf.Clamp(cameraDistance - mouse.scroll.ReadValue().y * 0.006f, 2.5f, 10f);
                Vector3? punchTarget = dummy != null ? dummy.PunchTarget : null;
                if (!crouched && mouse.leftButton.wasPressedThisFrame)
                    leftArm?.Jab(punchTarget);
                if (!crouched && mouse.rightButton.wasPressedThisFrame)
                    rightArm?.Jab(punchTarget);
            }

            var keyboard = Keyboard.current;
            Vector2 input = Vector2.zero;
            bool running = false;
            bool wantsJump = false;
            if (keyboard != null)
            {
                input = Vector2.ClampMagnitude(new Vector2(Axis(keyboard.dKey, keyboard.aKey),
                    Axis(keyboard.wKey, keyboard.sKey)), 1f);
                yaw += Axis(keyboard.eKey, keyboard.qKey) * 100f * step;
                running = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                wantsJump = keyboard.spaceKey.wasPressedThisFrame;
                if (keyboard.pKey.wasPressedThisFrame)
                    arena.TogglePress();
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    yaw = player.eulerAngles.y;
                    pitch = 18f;
                    cameraDistance = 5f;
                }
            }

            crouched = !arena.HasStandingClearance(player.position);
            SetHeight(crouched ? CrouchHeight : StandingHeight);
            Quaternion view = Quaternion.Euler(0f, yaw, 0f);
            Vector3 direction = view * new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 0.01f)
                player.rotation = Quaternion.RotateTowards(player.rotation,
                    Quaternion.LookRotation(direction), 720f * step);

            float speed = crouched ? 1.2f : running ? 4f : 2f;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, direction * speed, 24f * step);

            if (capsule.isGrounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                jumping = false;
                if (wantsJump && !crouched)
                {
                    verticalVelocity = 5.2f;
                    jumping = true;
                }
            }
            verticalVelocity += Physics.gravity.y * step;
            CollisionFlags hits = capsule.Move((horizontalVelocity + Vector3.up * verticalVelocity) * step);
            if ((hits & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
            if ((hits & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                jumping = false;

            float actualSpeed = Vector3.ProjectOnPlane(capsule.velocity, Vector3.up).magnitude;
            int target = jumping && jump != null ? 3 : actualSpeed > 0.15f ? (running && !crouched ? 2 : 1) : 0;
            UpdateAnimation(target, actualSpeed, step);
        }

        private void FixedUpdate()
        {
            arena.StepPress(Time.fixedDeltaTime);
            UpdateArmTargets(Time.fixedDeltaTime);

            foreach (BodyPart part in parts)
            {
                GetPose(part, out Vector3 position, out Quaternion rotation, out _);
                Rigidbody body = part.Body;

                // Each motor has limited authority so contact forces can move the limb away
                // from its animation target before the spring brings it back.
                Vector3 acceleration = (position - body.position) * 145f
                    + (capsule.velocity - body.linearVelocity) * 18f - Physics.gravity * 0.9f;
                body.AddForce(Vector3.ClampMagnitude(acceleration, 110f), ForceMode.Acceleration);

                Quaternion delta = rotation * Quaternion.Inverse(body.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f)
                    angle -= 360f;
                Vector3 angular = axis * (angle * Mathf.Deg2Rad * 105f) - body.angularVelocity * 14f;
                body.AddTorque(Vector3.ClampMagnitude(angular, 125f), ForceMode.Acceleration);
            }
        }

        private void LateUpdate()
        {
            if (followCamera == null)
                return;
            Vector3 focus = player.position + Vector3.up * Mathf.Min(capsule.height * 0.62f, 1.1f);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = orbit * Vector3.back;
            float distance = cameraDistance;
            if (arena.RaycastCamera(new Ray(focus, back), distance, out RaycastHit hit))
                distance = Mathf.Max(0.22f, hit.distance - 0.18f);
            followCamera.transform.SetPositionAndRotation(focus + back * distance, orbit);
        }

        private void SetHeight(float height)
        {
            if (Mathf.Approximately(capsule.height, height) &&
                Mathf.Approximately(capsule.center.y, height * 0.5f))
                return;

            capsule.radius = height < StandingHeight ? 0.22f : 0.33f;
            capsule.height = height;
            capsule.center = Vector3.up * (height * 0.5f);
            capsule.stepOffset = height < StandingHeight ? 0.12f : 0.29f;
            foreach (BodyPart part in parts)
                Physics.IgnoreCollision(capsule, part.Contact);
            if (motionRoot != null)
                motionRoot.localScale = new Vector3(1f, height / StandingHeight, 1f);
        }

        private void BuildAppearance()
        {
            if (motionModel == null || idle == null || walk == null || run == null)
            {
                Debug.LogError("Assign the Human Basic Motions model and Idle, Walk, and Run clips.", this);
                CreateFallbackPill();
                return;
            }

            motionRoot = new GameObject("Humanoid Pose").transform;
            motionRoot.SetParent(player, false);
            GameObject model = Instantiate(motionModel, motionRoot, false);
            var animator = model.GetComponent<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            {
                Debug.LogError("The motion pack's model needs a valid Humanoid avatar.", this);
                model.SetActive(false);
                CreateFallbackPill();
                return;
            }

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Pill Player Motions");
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            var output = AnimationPlayableOutput.Create(graph, "Humanoid Motion", animator);
            output.SetSourcePlayable(mixer);
            AnimationClip[] source = { idle, walk, run, jump };
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                    continue;
                clips[i] = AnimationClipPlayable.Create(graph, source[i]);
                graph.Connect(clips[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
            }
            graph.Play();
            graph.Evaluate(0f);

            var teal = new Color(0.22f, 0.74f, 0.68f);
            var blue = new Color(0.2f, 0.36f, 0.53f);
            var orange = new Color(0.98f, 0.58f, 0.26f);
            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.UpperChest, 0.48f, 0.33f, teal);
            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.LastBone, 0.36f, 0.28f, blue);
            AddPart(animator, HumanBodyBones.Head, HumanBodyBones.LastBone, 0.355f, 0.33f, teal);
            AddPart(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.16f, 0.16f, teal);
            AddPart(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.16f, 0.16f, teal);
            AddPart(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.15f, 0.15f, teal);
            AddPart(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.15f, 0.15f, teal);
            AddPart(animator, HumanBodyBones.LeftHand, HumanBodyBones.LastBone, 0.17f, 0.17f, orange);
            AddPart(animator, HumanBodyBones.RightHand, HumanBodyBones.LastBone, 0.17f, 0.17f, orange);
            AddPart(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.205f, 0.20f, teal);
            AddPart(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.205f, 0.20f, teal);
            AddPart(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.18f, 0.19f, teal);
            AddPart(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.18f, 0.19f, teal);
            AddPart(animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 0.17f, 0.21f, orange);
            AddPart(animator, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 0.17f, 0.21f, orange);
        }

        private void AddPart(Animator animator, HumanBodyBones start, HumanBodyBones end,
            float width, float depth, Color color)
        {
            Transform first = animator.GetBoneTransform(start);
            Transform last = end == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(end);
            if (start == HumanBodyBones.Hips && end == HumanBodyBones.UpperChest && last == null)
                last = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (first == null)
                return;
            PrimitiveType shape = last != null ? PrimitiveType.Capsule : PrimitiveType.Sphere;
            Transform visual = CreateShape(start.ToString(), shape, color);
            parts.Add(new BodyPart { Bone = start, Start = first, End = last, Width = width, Depth = depth, Visual = visual });
            if (start == HumanBodyBones.Head)
            {
                var ink = new Color(0.05f, 0.11f, 0.16f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Transform eye = CreateShape("Eye", PrimitiveType.Sphere, ink);
                    eye.SetParent(visual, false);
                    eye.localPosition = new Vector3(side * 0.18f, 0.06f, 0.46f);
                    eye.localScale = new Vector3(0.14f, 0.19f, 0.1f);
                }
            }
        }

        private void BuildPhysicalBody()
        {
            foreach (BodyPart part in parts)
            {
                GetPose(part, out Vector3 position, out Quaternion rotation, out Vector3 size);
                var root = new GameObject(part.Bone + " Physics");
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(position, rotation);

                if (part.Bone == HumanBodyBones.Hips)
                {
                    var core = root.AddComponent<BoxCollider>();
                    core.size = size * 0.9f;
                    part.Contact = core;
                }
                else
                {
                    var rounded = root.AddComponent<CapsuleCollider>();
                    rounded.radius = Mathf.Min(size.x, size.z) * 0.45f;
                    rounded.height = Mathf.Max(size.y * 0.92f, rounded.radius * 2f);
                    part.Contact = rounded;
                }
                part.Body = root.AddComponent<Rigidbody>();
                part.Body.mass = part.Bone == HumanBodyBones.Hips ? 8f :
                    part.Bone == HumanBodyBones.Head ? 2.6f : 1.7f;
                part.Body.interpolation = RigidbodyInterpolation.Interpolate;
                part.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                part.Body.solverIterations = 12;
                part.Body.maxAngularVelocity = 15f;

                part.Visual.SetParent(root.transform, false);
                part.Visual.localPosition = Vector3.zero;
                part.Visual.localRotation = Quaternion.identity;
                part.Visual.localScale = part.End != null ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
                Physics.IgnoreCollision(capsule, part.Contact);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                BodyPart part = parts[i];
                for (int j = 0; j < i; j++)
                {
                    BodyPart other = parts[j];
                    bool armAgainstBody = (part.Bone == HumanBodyBones.Hips && IsOuterArm(other.Bone)) ||
                        (other.Bone == HumanBodyBones.Hips && IsOuterArm(part.Bone));
                    if (!armAgainstBody)
                        Physics.IgnoreCollision(part.Contact, other.Contact);
                }

                BodyPart parent = FindPhysicalParent(part);
                if (parent != null)
                    JoinParts(part, parent);
            }
        }

        private BodyPart FindPhysicalParent(BodyPart part)
        {
            HumanBodyBones parent;
            bool torso = false;
            switch (part.Bone)
            {
                case HumanBodyBones.Hips:
                    if (part.End == null)
                        return null;
                    parent = HumanBodyBones.Hips;
                    break;
                case HumanBodyBones.Head:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    parent = HumanBodyBones.Hips;
                    torso = true;
                    break;
                case HumanBodyBones.LeftUpperLeg: parent = HumanBodyBones.Hips; break;
                case HumanBodyBones.RightUpperLeg: parent = HumanBodyBones.Hips; break;
                case HumanBodyBones.LeftLowerLeg: parent = HumanBodyBones.LeftUpperLeg; break;
                case HumanBodyBones.RightLowerLeg: parent = HumanBodyBones.RightUpperLeg; break;
                case HumanBodyBones.LeftFoot: parent = HumanBodyBones.LeftLowerLeg; break;
                case HumanBodyBones.RightFoot: parent = HumanBodyBones.RightLowerLeg; break;
                case HumanBodyBones.LeftLowerArm: parent = HumanBodyBones.LeftUpperArm; break;
                case HumanBodyBones.RightLowerArm: parent = HumanBodyBones.RightUpperArm; break;
                case HumanBodyBones.LeftHand: parent = HumanBodyBones.LeftLowerArm; break;
                case HumanBodyBones.RightHand: parent = HumanBodyBones.RightLowerArm; break;
                default: return null;
            }

            return parts.Find(candidate => candidate.Bone == parent &&
                (parent != HumanBodyBones.Hips || (candidate.End != null) == torso));
        }

        private void JoinParts(BodyPart part, BodyPart parent)
        {
            Vector3 pivot = part.Start.position;
            PillArmMotion arm = ArmFor(part.Bone);
            if (arm != null)
            {
                if (part.Bone == HumanBodyBones.LeftUpperArm || part.Bone == HumanBodyBones.RightUpperArm)
                    pivot = arm.Shoulder;
                else if (IsOuterArm(part.Bone) && part.End != null)
                    pivot = arm.Elbow;
                else
                    pivot = arm.Hand;
            }
            var joint = part.Body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = part.Body.transform.InverseTransformPoint(pivot);
            joint.connectedAnchor = parent.Body.transform.InverseTransformPoint(pivot);
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            // The compound parts stay joined at the bone while retaining rotation.
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
        }

        private void GetPose(BodyPart part, out Vector3 position, out Quaternion rotation, out Vector3 size)
        {
            Vector3 start = part.Start.position;
            Vector3 end = part.End != null ? part.End.position : start;
            PillArmMotion arm = ArmFor(part.Bone);
            if (arm != null)
            {
                switch (part.Bone)
                {
                    case HumanBodyBones.LeftUpperArm:
                    case HumanBodyBones.RightUpperArm:
                        start = arm.Shoulder;
                        end = arm.Elbow;
                        break;
                    case HumanBodyBones.LeftLowerArm:
                    case HumanBodyBones.RightLowerArm:
                        start = arm.Elbow;
                        end = arm.Hand;
                        break;
                    default:
                        start = end = arm.Hand;
                        break;
                }
            }

            Vector3 span = end - start;
            position = (start + end) * 0.5f;
            rotation = span.sqrMagnitude > 0.00001f
                ? Quaternion.FromToRotation(player.up, span) * player.rotation
                : player.rotation;
            float height = Mathf.Max(span.magnitude * 1.25f, part.Width);
            if (part.Bone == HumanBodyBones.Head)
                height = part.Width * 1.11f;
            size = new Vector3(part.Width, height, part.Depth);
        }

        private PillArmMotion CreateArm(bool left)
        {
            HumanBodyBones upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones lower = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Transform shoulder = parts.Find(part => part.Bone == upper)?.Start;
            Transform elbow = parts.Find(part => part.Bone == lower)?.Start;
            Transform wrist = parts.Find(part => part.Bone == hand)?.Start;
            if (shoulder == null || elbow == null || wrist == null)
                return null;

            return new PillArmMotion(player, shoulder, elbow, wrist, left ? -1f : 1f);
        }

        private void UpdateArmTargets(float step)
        {
            if (torso == null)
                return;

            Vector3 center = (torso.Start.position + torso.End.position) * 0.5f;
            leftArm?.Update(step, center, crouched);
            rightArm?.Update(step, center, crouched);
        }

        private PillArmMotion ArmFor(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.LeftHand: return leftArm;
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.RightHand: return rightArm;
                default: return null;
            }
        }

        private static bool IsOuterArm(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftLowerArm || bone == HumanBodyBones.RightLowerArm ||
                bone == HumanBodyBones.LeftHand || bone == HumanBodyBones.RightHand;
        }

        private void CreateFallbackPill()
        {
            Transform shape = CreateShape("Pill Body", PrimitiveType.Capsule, new Color(0.22f, 0.74f, 0.68f));
            shape.localPosition = Vector3.up * 0.925f;
            shape.localScale = new Vector3(0.66f, 0.925f, 0.66f);
        }

        private Transform CreateShape(string name, PrimitiveType type, Color color)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(player, false);
            Collider visualCollider = shape.GetComponent<Collider>();
            visualCollider.enabled = false;
            Destroy(visualCollider);
            RagdollTestArena.Tint(shape.GetComponent<MeshRenderer>(), color);
            return shape.transform;
        }

        private void UpdateAnimation(int target, float speed, float step)
        {
            if (!graph.IsValid())
                return;

            if (target != currentClip && target == 3)
                clips[3].SetTime(0f);
            currentClip = target;
            for (int i = 0; i < clips.Length; i++)
            {
                float weight = mixer.GetInputWeight(i);
                mixer.SetInputWeight(i, Mathf.Lerp(weight, i == target ? 1f : 0f,
                    1f - Mathf.Exp(-step * 16f)));
            }

            clips[1].SetSpeed(Mathf.Clamp(speed / 2f, 0.4f, 1.2f));
            clips[2].SetSpeed(Mathf.Clamp(speed / 4f, 0.4f, 1.2f));
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        private static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive,
            UnityEngine.InputSystem.Controls.KeyControl negative)
        {
            return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
        }
    }
}
