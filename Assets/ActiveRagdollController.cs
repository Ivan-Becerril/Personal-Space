using UnityEngine;
using UnityEngine.InputSystem;

namespace PersonalSpace
{
    public sealed class ActiveRagdollController : MonoBehaviour
    {
        [SerializeField] private GameObject walkReferenceModel = null;
        [SerializeField] private AnimationClip walkReferenceClip = null;
        [SerializeField, Min(0f)] private float moveSpeed = 3.2f;
        [SerializeField, Min(0f)] private float balanceSpring = 430f;
        [SerializeField, Min(0f)] private float balanceDamping = 65f;
        [SerializeField, Min(0f)] private float maximumBalanceTorque = 260f;
        [SerializeField, Range(25f, 85f)] private float fallAngle = 58f;

        private struct GaitSide
        {
            public ConfigurableJoint Thigh;
            public ConfigurableJoint Shin;
            public ConfigurableJoint Foot;
            public ConfigurableJoint Arm;
        }

        private GaitSide leftGait;
        private GaitSide rightGait;
        private RagdollWalkingGait walkingGait;
        private float walkingWeight;
        private const float JumpBufferSeconds = 0.16f;
        private const float GroundGraceSeconds = 0.09f;
        private float jumpBuffer;
        private float groundGrace;
        private bool jumping;
        private float jumpTime;
        private float supportBlend = 1f;
        private Rigidbody hips;
        private Rigidbody chest;
        private Rigidbody[] parts;
        private ConfigurableJoint[] joints;
        private BoxCollider leftFoot;
        private BoxCollider rightFoot;
        private RagdollTestArena arena;
        private Vector3[] restOffsets;
        private Vector3[] recoveryStartOffsets;
        private float downedTime;
        private bool getUpQueued;
        private bool isPracticeDummy;

        private float offBalanceTime;
        private bool unconscious;
        private float recoveryTime;
        private bool recovering;
        private Vector3 recoveryOrigin;
        private Quaternion recoveryHeading;
        private PhysicsMaterial footMaterial;
        private Camera followCamera;
        private Vector2 moveInput;
        private Vector3 facing;
        private float cameraYaw;
        private float cameraPitch = 20f;
        private float cameraDistance = 5f;

        private bool needsGetUp;
        private float standingHipHeight;
        private float bodyMass;
        private float bodyScale = 1f;

        // Called by the shared rig builder after all of the joints exist.
        internal void Initialize(Transform rig, Rigidbody pelvis, Rigidbody torso, Collider ground)
        {
            arena = new RagdollTestArena(ground);
            InitializeRig(rig, pelvis, torso, ground);

            SpawnPracticeDummy(transform, arena, ground);
        }

        internal static ActiveRagdollController SpawnPracticeDummy(Transform parent, RagdollTestArena arena, Collider ground, float scale = 1f)
        {
            Transform rig = RagdollDropTest.BuildRig(parent, "Practice Dummy",
                RagdollTestArena.DummyPosition, Quaternion.Euler(0f, 180f, 0f),
                out Rigidbody hips, out Rigidbody chest, scale);
            var dummy = rig.gameObject.AddComponent<ActiveRagdollController>();
            dummy.isPracticeDummy = true;
            dummy.bodyScale = scale;
            dummy.arena = arena;
            dummy.InitializeRig(rig, hips, chest, ground);
            return dummy;
        }

        private void InitializeRig(Transform rig, Rigidbody pelvis, Rigidbody torso, Collider ground)
        {
            hips = pelvis;
            chest = torso;
            parts = rig.GetComponentsInChildren<Rigidbody>();
            joints = rig.GetComponentsInChildren<ConfigurableJoint>();
            // The floor and body were positioned in Awake. Update the physics shapes
            // before reading the floor bounds or using them to set the probe length.
            Physics.SyncTransforms();
            standingHipHeight = hips.position.y - ground.bounds.max.y;
            restOffsets = new Vector3[parts.Length];
            recoveryStartOffsets = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bodyMass += parts[i].mass;
                restOffsets[i] = rig.InverseTransformDirection(parts[i].position - hips.position);
            }
            leftFoot = rig.Find("Left Foot/Shape").GetComponent<BoxCollider>();
            rightFoot = rig.Find("Right Foot/Shape").GetComponent<BoxCollider>();
            leftGait = BindGaitSide(rig, "Left");
            rightGait = BindGaitSide(rig, "Right");
            if (!isPracticeDummy)
            {
                var leftLeg = new RagdollWalkingGait.Leg(leftGait.Thigh, leftGait.Shin, leftGait.Foot, leftGait.Arm, leftFoot);
                var rightLeg = new RagdollWalkingGait.Leg(rightGait.Thigh, rightGait.Shin, rightGait.Foot, rightGait.Arm, rightFoot);
                walkingGait = new RagdollWalkingGait(hips, arena, leftLeg, rightLeg,
                    WalkMotionReference.Create(walkReferenceModel, walkReferenceClip));
            }
            CreateBubbleVisuals();
            facing = rig.forward;
            followCamera = isPracticeDummy ? null : Camera.main;
            cameraYaw = followCamera != null ? followCamera.transform.eulerAngles.y : rig.eulerAngles.y;

            // Static friction lets the planted foot transfer joint effort into the floor.
            footMaterial = new PhysicsMaterial("Ragdoll Foot Grip")
            {
                staticFriction = 0.95f,
                dynamicFriction = 0.78f,
                frictionCombine = PhysicsMaterialCombine.Maximum
            };
            leftFoot.sharedMaterial = footMaterial;
            rightFoot.sharedMaterial = footMaterial;
            SetPoseDrives(unconscious ? 0f : 1f);
        }

        public void SetUnconscious(bool value)
        {
            if (unconscious == value)
                return;

            unconscious = value;
            EnterDownedState();
        }

        private void Update()
        {
            if (isPracticeDummy)
                return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 drag = mouse.delta.ReadValue();
                    cameraYaw = Mathf.Repeat(cameraYaw + drag.x * 0.18f, 360f);
                    cameraPitch = Mathf.Clamp(cameraPitch - drag.y * 0.18f, 8f, 72f);
                }

                cameraDistance = Mathf.Clamp(cameraDistance - mouse.scroll.ReadValue().y * 0.006f, 2.5f, 10f);
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    cameraYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                    cameraPitch = 20f;
                    cameraDistance = 5f;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                moveInput = Vector2.zero;
                return;
            }

            moveInput = Vector2.ClampMagnitude(new Vector2(
                Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
                Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed)), 1f);
            cameraYaw = Mathf.Repeat(cameraYaw +
                Axis(keyboard.eKey.isPressed, keyboard.qKey.isPressed) * 100f * Time.deltaTime, 360f);

            if (keyboard.spaceKey.wasPressedThisFrame && !unconscious)
            {
                if (needsGetUp)
                    getUpQueued = true;
                else
                    jumpBuffer = JumpBufferSeconds;
            }

            if (keyboard.pKey.wasPressedThisFrame)
                arena?.TogglePress();

        }

        private void FixedUpdate()
        {
            if (hips == null)
                return;

            if (!isPracticeDummy)
                arena.StepPress(Time.fixedDeltaTime);

            Quaternion viewHeading = Quaternion.Euler(0f, cameraYaw, 0f);

            if (unconscious)
                return;

            Vector3 movement = viewHeading * new Vector3(moveInput.x, 0f, moveInput.y);
            if (movement.sqrMagnitude > 0f)
                facing = movement.normalized;

            if (needsGetUp)
            {
                UpdateDownedState(movement);
                return;
            }

            float tilt = Mathf.Max(UprightError(hips), UprightError(chest));
            offBalanceTime = tilt > fallAngle ? offBalanceTime + Time.fixedDeltaTime : 0f;
            if (offBalanceTime >= 0.12f || tilt > fallAngle + 17f ||
                arena.PressIsLowOver(hips.position, 1.28f * bodyScale))
            {
                EnterDownedState();
                UpdateDownedState(movement);
                return;
            }

            bool footOnGround = FootNearFloor(leftFoot) || FootNearFloor(rightFoot);
            groundGrace = footOnGround && !jumping
                ? GroundGraceSeconds
                : Mathf.Max(0f, groundGrace - Time.fixedDeltaTime);

            if (jumpBuffer > 0f && !jumping && groundGrace > 0f)
            {
                jumping = true;
                jumpTime = 0f;
                jumpBuffer = 0f;
                groundGrace = 0f;
                supportBlend = 0f;
                foreach (Rigidbody part in parts)
                    part.AddForce(Vector3.up * 5.2f, ForceMode.VelocityChange);
            }
            jumpBuffer = Mathf.Max(0f, jumpBuffer - Time.fixedDeltaTime);

            if (jumping)
            {
                jumpTime += Time.fixedDeltaTime;
                bool landed = jumpTime >= 0.14f && hips.linearVelocity.y <= 0f && footOnGround;
                if (!landed)
                {
                    StopWalking();
                    ApplyBalance();
                    return;
                }

                jumping = false;
            }

            // Look for support below the hips even if a foot has briefly bounced clear.
            var groundProbe = new Ray(hips.position, Vector3.down);
            if (!arena.Raycast(groundProbe, standingHipHeight + 0.18f, out RaycastHit ground))
            {
                StopWalking();
                ApplyBalance();
                return;
            }

            bool walking = walkingGait != null && walkingGait.Step(movement, moveSpeed, Time.fixedDeltaTime);
            walkingWeight = Mathf.MoveTowards(walkingWeight, walking ? 1f : 0f, Time.fixedDeltaTime * 5f);
            supportBlend = Mathf.MoveTowards(supportBlend, 1f, Time.fixedDeltaTime * 8f);
            float heightError = standingHipHeight - walkingWeight * 0.09f - ground.distance;
            float gravity = Mathf.Abs(Physics.gravity.y);
            // Unload each part so the torso does not compress the leg joints.
            // Apply the height correction at the hips while retaining some foot load.
            float weightAssist = Mathf.Lerp(0.8f, 0.27f, walkingWeight) * gravity * supportBlend;
            foreach (Rigidbody part in parts)
                part.AddForce(Vector3.up * weightAssist, ForceMode.Acceleration);

            float heightCorrection = heightError * 75f - hips.linearVelocity.y * 14f;
            float maximumLift = Mathf.Lerp(0.65f, 0.32f, walkingWeight) * gravity;
            float supportForce = Mathf.Clamp(heightCorrection, -0.8f * gravity, maximumLift) * bodyMass;
            hips.AddForce(Vector3.up * (supportForce * supportBlend), ForceMode.Force);

            ApplyBalance();
        }

        private void DriveHorizontal(Vector3 movement, float speed, float response, float accelerationLimit)
        {
            if (movement.sqrMagnitude == 0f)
                return;

            Vector3 velocity = Vector3.ProjectOnPlane(hips.linearVelocity, Vector3.up);
            Vector3 acceleration = Vector3.ClampMagnitude(
                (movement * speed - velocity) * response, accelerationLimit);
            foreach (Rigidbody part in parts)
                part.AddForce(acceleration, ForceMode.Acceleration);
        }

        internal Vector3 PunchTarget => chest.worldCenterOfMass;

        private void EnterDownedState()
        {
            needsGetUp = true;
            recovering = false;
            offBalanceTime = 0f;
            downedTime = 0f;
            groundGrace = 0f;
            jumpBuffer = 0f;
            jumping = false;
            getUpQueued = false;
            StopWalking();

            if (joints == null)
                return;

            SetPoseDrives(unconscious ? 0f : 0.3f);
            if (!unconscious)
            {
                Brace(leftGait);
                Brace(rightGait);
            }
        }

        private static void Brace(GaitSide side)
        {
            side.Thigh.targetRotation = Quaternion.Euler(22f, 0f, 0f);
            side.Shin.targetRotation = Quaternion.Euler(-34f, 0f, 0f);
            side.Arm.targetRotation = Quaternion.Euler(38f, 0f, 0f);
        }

        private void UpdateDownedState(Vector3 movement)
        {
            if (recovering)
            {
                UpdateRecovery(movement);
                return;
            }

            var probe = new Ray(hips.position + Vector3.up * 0.15f, Vector3.down);
            bool nearGround = arena.Raycast(probe, standingHipHeight, out RaycastHit ground);
            if (!nearGround)
            {
                downedTime = 0f;
                DriveHorizontal(movement, 1.6f, 4f, 2.5f);
                return;
            }

            downedTime += Time.fixedDeltaTime;
            DriveHorizontal(movement, 1.55f, 11f, 13f);
            bool readyToRise = downedTime >= 0.24f && hips.linearVelocity.y < 0.6f &&
                arena.HasStandingClearance(ground.point, 2.1f * bodyScale);
            if (!readyToRise || (!getUpQueued && !(isPracticeDummy && downedTime >= 1.4f)))
                return;

            recovering = true;
            recoveryTime = 0f;
            recoveryOrigin = arena.GetClearRecoverySpot(ground.point, 2.1f * bodyScale);
            recoveryHeading = Quaternion.LookRotation(facing, Vector3.up);
            for (int i = 0; i < parts.Length; i++)
                recoveryStartOffsets[i] = parts[i].position - recoveryOrigin;

            SetPoseDrives(0.55f);
        }

        private void UpdateRecovery(Vector3 movement)
        {
            if (!arena.HasStandingClearance(recoveryOrigin, 2.1f * bodyScale))
            {
                EnterDownedState();
                getUpQueued = true;
                return;
            }

            float step = Time.fixedDeltaTime;
            Vector3 previousOrigin = recoveryOrigin;
            if (movement.sqrMagnitude > 0f)
            {
                Vector3 candidate = recoveryOrigin + movement * (1.2f * step);
                Vector3 offset = Vector3.ProjectOnPlane(candidate - hips.position, Vector3.up);
                if (offset.sqrMagnitude < 0.49f &&
                    arena.Raycast(new Ray(candidate + Vector3.up * 0.45f, Vector3.down), 0.95f, out RaycastHit ground) &&
                    arena.IsRecoverySpotClear(ground.point, 2.1f * bodyScale))
                    recoveryOrigin = ground.point;

                recoveryHeading = Quaternion.RotateTowards(recoveryHeading,
                    Quaternion.LookRotation(facing, Vector3.up), 240f * step);
            }

            recoveryTime += step;
            float rise = Mathf.SmoothStep(0f, 1f, recoveryTime / 1.05f);
            Vector3 pelvisTarget = recoveryOrigin + Vector3.up * standingHipHeight;
            Vector3 targetVelocity = Vector3.ClampMagnitude((recoveryOrigin - previousOrigin) / step, 1.2f);
            float gravityAssist = Mathf.Lerp(0.2f, 1f, rise);

            for (int i = 0; i < parts.Length; i++)
            {
                Rigidbody part = parts[i];
                Vector3 standingOffset = Vector3.up * standingHipHeight + recoveryHeading * restOffsets[i];
                Vector3 target = recoveryOrigin + Vector3.Lerp(recoveryStartOffsets[i], standingOffset, rise);
                Vector3 acceleration = (target - part.position) * 38f -
                    (part.linearVelocity - targetVelocity) * 9f - Physics.gravity * gravityAssist;
                part.AddForce(Vector3.ClampMagnitude(acceleration, 23f), ForceMode.Acceleration);
            }
            AlignForRecovery(hips, rise);
            AlignForRecovery(chest, rise);

            bool upright = UprightError(hips) < 20f && UprightError(chest) < 24f;
            if (rise >= 1f && upright && (hips.position - pelvisTarget).sqrMagnitude < 0.09f &&
                Mathf.Abs(hips.linearVelocity.y) < 0.85f)
            {
                needsGetUp = false;
                recovering = false;
                offBalanceTime = 0f;
                supportBlend = 0.8f;
                SetPoseDrives(1f);
            }
        }

        private void AlignForRecovery(Rigidbody part, float rise)
        {
            Quaternion error = recoveryHeading * Quaternion.Inverse(part.rotation);
            error.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f)
                degrees -= 360f;
            Vector3 acceleration = axis * (degrees * Mathf.Deg2Rad * 23f * rise) - part.angularVelocity * 7f;
            part.AddTorque(Vector3.ClampMagnitude(acceleration, 29f), ForceMode.Acceleration);
        }

        private static float UprightError(Rigidbody part)
        {
            return Vector3.Angle(part.rotation * Vector3.up, Vector3.up);
        }

        private bool FootNearFloor(BoxCollider foot)
        {
            Bounds bounds = foot.bounds;
            return arena.Raycast(new Ray(bounds.center, Vector3.down), bounds.extents.y + 0.045f, out _);
        }

        private static GaitSide BindGaitSide(Transform rig, string side)
        {
            return new GaitSide
            {
                Thigh = rig.Find(side + " Thigh").GetComponent<ConfigurableJoint>(),
                Shin = rig.Find(side + " Shin").GetComponent<ConfigurableJoint>(),
                Foot = rig.Find(side + " Foot").GetComponent<ConfigurableJoint>(),
                Arm = rig.Find(side + " Upper Arm").GetComponent<ConfigurableJoint>()
            };
        }

        private void StopWalking()
        {
            walkingGait?.Stop();
            walkingWeight = 0f;
        }

        private void CreateBubbleVisuals()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.SetActive(false);
            Mesh mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);

            var bodyColor = isPracticeDummy ? new Color(0.65f, 0.42f, 0.86f) : new Color(0.22f, 0.74f, 0.68f);
            var accentColor = isPracticeDummy ? new Color(0.98f, 0.8f, 0.28f) : new Color(0.98f, 0.58f, 0.26f);
            foreach (Rigidbody part in parts)
            {
                Transform shape = part.transform.Find("Shape");
                var originalRenderer = shape.GetComponent<MeshRenderer>();
                originalRenderer.enabled = false;
                Material material = originalRenderer.sharedMaterial;
                Vector3 size = shape.localScale;
                Color color = bodyColor;

                if (bodyScale > 1f)
                {
                    if (part.name.EndsWith("Hand", System.StringComparison.Ordinal) ||
                        part.name.EndsWith("Foot", System.StringComparison.Ordinal))
                        color = accentColor;
                    else if (part == hips)
                        color = new Color(0.2f, 0.36f, 0.53f);
                    size *= 1.05f;
                }
                else if (part.name == "Head")
                    size = new Vector3(0.47f, 0.47f, 0.43f);
                else if (part == chest)
                    size = new Vector3(0.64f, 0.65f, 0.41f);
                else if (part == hips)
                {
                    size = new Vector3(0.53f, 0.38f, 0.34f);
                    color = new Color(0.2f, 0.36f, 0.53f);
                }
                else if (part.name.EndsWith("Foot", System.StringComparison.Ordinal))
                    color = accentColor;
                else
                    size = Vector3.Scale(size, new Vector3(1.12f, 1.22f, 1.12f));

                AddBubble(part.transform, mesh, material, part.name == "Head" ? Vector3.up * 0.025f : Vector3.zero, size, color);
                if (bodyScale == 1f && part.name.EndsWith("Forearm", System.StringComparison.Ordinal))
                    AddBubble(part.transform, mesh, material, Vector3.down * 0.135f,
                        new Vector3(0.19f, 0.19f, 0.17f), accentColor);

                if (part.name == "Head")
                {
                    var eyeSize = new Vector3(0.06f, 0.079f, 0.036f);
                    float faceScale = bodyScale > 1f ? bodyScale * 0.63f : 1f;
                    eyeSize *= faceScale;
                    var eyeColor = new Color(0.05f, 0.11f, 0.16f);
                    AddBubble(part.transform, mesh, material, new Vector3(-0.086f, 0.06f, 0.197f) * faceScale, eyeSize, eyeColor);
                    AddBubble(part.transform, mesh, material, new Vector3(0.086f, 0.06f, 0.197f) * faceScale, eyeSize, eyeColor);
                }
            }
        }

        private static void AddBubble(Transform parent, Mesh mesh, Material material,
            Vector3 position, Vector3 size, Color color)
        {
            var visual = new GameObject("Bubble Visual");
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = size;
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
        }

        private void LateUpdate()
        {
            if (hips == null || followCamera == null)
                return;

            Vector3 position = hips.transform.position;
            Vector3 focus = new Vector3(position.x, Mathf.Max(0.65f, position.y + 0.1f), position.z);
            Quaternion orbit = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            Vector3 offset = orbit * Vector3.back * cameraDistance;
            float distance = cameraDistance;
            if (arena.RaycastCamera(new Ray(focus, offset.normalized), distance, out RaycastHit obstruction))
                distance = Mathf.Max(0.12f, obstruction.distance - 0.22f);

            followCamera.transform.SetPositionAndRotation(focus + offset.normalized * distance, orbit);
        }

        private void ApplyBalance()
        {
            Vector3 up = hips.rotation * Vector3.up;
            Vector3 tiltVelocity = Vector3.ProjectOnPlane(hips.angularVelocity, Vector3.up);
            float lean = jumping || needsGetUp ? 0f : walkingWeight * 0.09f;
            Vector3 desiredUp = (Vector3.up + facing * lean).normalized;
            Vector3 tiltTorque = Vector3.Cross(up, desiredUp) * balanceSpring -
                tiltVelocity * balanceDamping;
            float inertiaScale = bodyScale * bodyScale;
            hips.AddTorque(Vector3.ClampMagnitude(tiltTorque, maximumBalanceTorque) * inertiaScale);

            Vector3 forward = Vector3.ProjectOnPlane(hips.rotation * Vector3.forward, Vector3.up);
            float yawError = Vector3.SignedAngle(forward, facing, Vector3.up) * Mathf.Deg2Rad;
            float yawTorque = Mathf.Clamp(yawError * 95f - hips.angularVelocity.y * 17f, -110f, 110f);
            hips.AddTorque(Vector3.up * (yawTorque * inertiaScale));
        }

        private void SetPoseDrives(float strength)
        {
            foreach (ConfigurableJoint joint in joints)
            {
                float mass = joint.GetComponent<Rigidbody>().mass * bodyScale * bodyScale;
                var drive = new JointDrive
                {
                    positionSpring = 105f * mass * strength,
                    positionDamper = 9f * mass * strength,
                    maximumForce = 36f * mass * strength
                };

                // The identity target holds the pose in which the joints were assembled.
                joint.rotationDriveMode = RotationDriveMode.XYAndZ;
                joint.targetRotation = Quaternion.identity;
                joint.angularXDrive = drive;
                joint.angularYZDrive = drive;
            }
        }

        private void OnDestroy()
        {
            if (footMaterial != null)
                Destroy(footMaterial);
        }

        private static float Axis(bool positive, bool negative)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }
    }
}
