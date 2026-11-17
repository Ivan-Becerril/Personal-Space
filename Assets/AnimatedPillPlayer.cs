using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace PersonalSpace
{
    public sealed class AnimatedPillPlayer : MonoBehaviour
    {
        [SerializeField] private GameObject motionModel = null;
        [SerializeField] private AnimationClip idle = null;
        [SerializeField] private AnimationClip walk = null;
        [SerializeField] private AnimationClip run = null;
        [SerializeField] private AnimationClip jump = null;
        [SerializeField] private Collider mapFloor = null;
        [SerializeField] private Collider[] mapObstacles = null;
        [SerializeField] private Transform mapDummySpawn = null;
        [SerializeField] private Transform mapPressSpawn = null;
        [SerializeField, Min(0f)] private float punchKnockbackSpeed = 6.5f;
        [SerializeField, Min(0f)] private float groundedPunchResistance = 7f;
        [SerializeField, Min(0.1f)] private float knockbackDeceleration = 14f;
        [SerializeField, Min(0.1f)] private float standUpTime = 0.85f;
        [SerializeField, Min(0.1f)] private float knockoutTime = 3f;
        [SerializeField, Min(0.1f)] private float externalKnockoutSpeed = 2.4f;

        private readonly FighterCondition condition = new FighterCondition();
        public FighterStatus Status => condition.Status;
        private bool IsRecovering => Status == FighterStatus.Conscious && condition.Recovery < 1f;

        private const float FighterScale = 1.5f;
        private const float WalkSpeed = 4f;
        private const float RunSpeed = 8.14f;
        private const float GroundAcceleration = 22f;
        private const float GroundBraking = 16f;
        private const float AirAcceleration = 13f;
        private const float AirSpeedLimit = 6.5f;
        private const float JumpSpeed = 6f;
        private const float StumbleHipDrop = 0.38f;
        private const float StumbleFollowSpeed = 2.6f;
        private const float StandingHeight = 1.85f * FighterScale;
        private const float CrouchSpeed = 2.4f;
        private const float CrouchHeight = StandingHeight * 0.68f;
        private const float PressDuckHeight = 0.90f;
        private CharacterController capsule;
        private RagdollTestArena arena;
        private AnimatedPillPlayer opponent;
        private bool isPracticeDummy;
        private Vector3 knockbackVelocity;
        private Vector3 previousMoveVelocity;
        private float bodyMass;
        private float bodyStagger;
        private float externalImpact;
        private float externalImpactHold;
        private float nextBodyBumpTime;
        private Camera followCamera;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float yaw;
        private float pitch = 18f;
        private float cameraDistance = 5f * FighterScale;
        private Transform player;
        private Transform motionRoot;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[4];
        private int currentClip;
        private bool jumping;
        private bool duckingUnderPress;
        private PillArmMotion leftArm;
        private PillArmMotion rightArm;
        private PillFlyingKick flyingKick;
        private BodyPart torso;
        private BodyPart leftShoulder;
        private BodyPart rightShoulder;
        private bool looseSelfCollisions;
        private readonly System.Collections.Generic.List<BodyPart> parts =
            new System.Collections.Generic.List<BodyPart>();

        private readonly System.Collections.Generic.Dictionary<FighterAnatomy.Shape, Mesh> anatomyMeshes =
            new System.Collections.Generic.Dictionary<FighterAnatomy.Shape, Mesh>();
        private readonly System.Collections.Generic.List<VisualJoin> visualJoins =
            new System.Collections.Generic.List<VisualJoin>();

        private struct VisualJoin
        {
            internal ConfigurableJoint Joint;
            internal BodyPart Child;
            internal BodyPart Parent;
            internal Transform Shape;
            internal float Width;
        }

        internal sealed class BodyPart
        {
            internal HumanBodyBones Bone;
            internal Transform Start;
            internal Rigidbody Body;
            internal BodyPart Parent;
            internal ConfigurableJoint Joint;
            internal float ImpactReaction;
            internal float NormalDepenetrationSpeed;
            internal Collider Contact;
            internal Transform End;
            internal Transform Visual;
            internal float Width;
            internal float Depth;
            internal Color Color;
            internal bool UsesAnatomyMesh;
        }

        private void Awake()
        {
            if (!isPracticeDummy)
            {
                if (mapFloor != null && mapPressSpawn != null)
                    arena = new RagdollTestArena(mapFloor, mapObstacles, mapPressSpawn);
                else
                {
                    Collider floor = RagdollTestArena.Block(transform, "Test Floor", new Vector3(0f, -0.25f, 0f),
                        new Vector3(RagdollTestArena.FloorWidth, 0.5f, RagdollTestArena.FloorWidth), Color.gray);
                    arena = new RagdollTestArena(floor);
                }
            }
            player = new GameObject("Pill Player").transform;
            player.SetParent(transform, false);
            capsule = player.gameObject.AddComponent<CharacterController>();
            capsule.skinWidth = 0.025f;
            capsule.minMoveDistance = 0f;
            capsule.excludeLayers = LayerMask.GetMask(PillMovementContact.ImpactLayer);
            player.gameObject.AddComponent<PillMovementContact>().Initialize(this);
            SetHeight(StandingHeight);
            followCamera = isPracticeDummy ? null : Camera.main;
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
            leftShoulder = parts.Find(part => part.Bone == HumanBodyBones.LeftUpperArm);
            rightShoulder = parts.Find(part => part.Bone == HumanBodyBones.RightUpperArm);
            if (!isPracticeDummy)
                SpawnPracticeDummy();
        }

        private void SpawnPracticeDummy()
        {
            var actor = new GameObject("Practice Dummy");
            actor.SetActive(false);
            actor.transform.SetParent(transform, false);
            if (mapDummySpawn != null)
                actor.transform.SetPositionAndRotation(mapDummySpawn.position, mapDummySpawn.rotation);
            else
                actor.transform.SetLocalPositionAndRotation(RagdollTestArena.DummyPosition, Quaternion.Euler(0f, 180f, 0f));

            // Configure the second instance before Awake builds its body.
            var duplicate = actor.AddComponent<AnimatedPillPlayer>();
            duplicate.motionModel = motionModel;
            duplicate.idle = idle;
            duplicate.walk = walk;
            duplicate.run = run;
            duplicate.jump = jump;
            duplicate.punchKnockbackSpeed = punchKnockbackSpeed;
            duplicate.groundedPunchResistance = groundedPunchResistance;
            duplicate.knockbackDeceleration = knockbackDeceleration;
            duplicate.standUpTime = standUpTime;
            duplicate.knockoutTime = knockoutTime;
            duplicate.externalKnockoutSpeed = externalKnockoutSpeed;
            duplicate.isPracticeDummy = true;
            duplicate.arena = arena;
            duplicate.opponent = this;
            opponent = duplicate;
            actor.SetActive(true);
            RefreshCapsuleIgnores();
            duplicate.RefreshCapsuleIgnores();
        }

        private void StartJab(PillArmMotion arm, Vector2 cursor)
        {
            if (arm == null)
                return;

            Vector3? point = null;
            if (opponent != null)
                point = FindPunchPoint(arm, cursor);
            arm.Jab(point);
        }

        private Vector3? FindPunchPoint(PillArmMotion arm, Vector2 cursor)
        {
            if (followCamera != null)
            {
                Ray ray = followCamera.ScreenPointToRay(cursor);
                float closestHit = 80f;
                Vector3? aimedPoint = null;
                foreach (BodyPart part in opponent.parts)
                {
                    if (part.Contact.Raycast(ray, out RaycastHit hit, closestHit))
                    {
                        closestHit = hit.distance;
                        aimedPoint = hit.point;
                    }
                }

                if (aimedPoint.HasValue && !arena.RaycastCamera(ray, closestHit, out _))
                    return aimedPoint;
            }

            Vector3? closestPoint = null;
            float best = float.PositiveInfinity;
            foreach (BodyPart part in opponent.parts)
            {
                Vector3 candidate = part.Contact.ClosestPoint(arm.Shoulder);
                Vector3 offset = candidate - arm.Shoulder;
                if (Vector3.Dot(offset, player.forward) <= 0f)
                    continue;

                float reach = offset.sqrMagnitude;
                if (reach >= best)
                    continue;

                best = reach;
                closestPoint = candidate;
            }
            return closestPoint;
        }

        internal void HandleBodyContact(BodyPart ownPart, PillArmMotion arm, Collision collision, bool newContact)
        {
            if (collision.contactCount == 0)
                return;

            Rigidbody otherBody = collision.rigidbody;
            if (otherBody == null)
                return;
            otherBody.TryGetComponent(out PillBodyContact otherPart);
            ContactPoint contact = collision.GetContact(0);
            if (otherPart == null)
            {
                HandleExternalContact(ownPart, otherBody, collision, contact, newContact);
                return;
            }

            if (otherPart.Fighter == this || otherPart.Fighter == null || otherPart.Part == null)
                return;

            if (newContact)
            {
                float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                float impulse = collision.impulse.magnitude;
                float limbSpeedChange = impulse / ownPart.Body.mass;
                if (normalSpeed >= 0.85f && limbSpeedChange > 0.30f)
                {
                    float localReaction = Mathf.Clamp01(limbSpeedChange / 1.65f);
                    ReactToImpact(ownPart, localReaction);

                    float bodySpeedChange = impulse / bodyMass;
                    float stagger = Mathf.InverseLerp(0.20f, 1.45f, bodySpeedChange);
                    if (ownPart.Bone == HumanBodyBones.Hips)
                        stagger = Mathf.Max(stagger, localReaction * 0.65f);
                    bodyStagger = Mathf.Max(bodyStagger, stagger);

                }
            }

            if (Status == FighterStatus.KnockedOut)
                return;

            if (IsKickContact(ownPart.Bone))
                TryKickContact(otherPart, contact.point);
            if (arm != null && arm.StrikeDirection.sqrMagnitude > 0f)
                TryPunchContact(arm, otherPart.Fighter, otherPart.Part, contact.point);
        }

        private void HandleExternalContact(BodyPart part, Rigidbody source, Collision collision,
            ContactPoint contact, bool newContact)
        {
            float incomingSpeed = Vector3.Dot(source.GetPointVelocity(contact.point), contact.normal);
            if (!newContact && incomingSpeed < 0.40f)
                return;

            float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            float impulse = collision.impulse.magnitude;
            float limbSpeedChange = impulse / part.Body.mass;
            if (Mathf.Max(normalSpeed, incomingSpeed) < 0.45f || limbSpeedChange < 0.15f)
                return;

            // The collision solver moves the body. The pose motors yield long enough
            // for that impulse to travel through the joints.
            float localReaction = Mathf.Clamp01(limbSpeedChange / 0.65f);
            ReactToImpact(part, localReaction);
            float bodySpeedChange = impulse / bodyMass;
            float wholeBodyReaction = Mathf.Max(Mathf.InverseLerp(0.06f, 0.60f, bodySpeedChange),
                localReaction * (part.Bone == HumanBodyBones.Hips ? 0.92f : 0.58f));
            externalImpact = Mathf.Max(externalImpact, wholeBodyReaction);
            externalImpactHold = Mathf.Max(externalImpactHold, wholeBodyReaction * 0.22f);

            if (bodySpeedChange >= externalKnockoutSpeed)
                KnockOut();
        }

        private void KnockOut()
        {
            if (torso == null || Status == FighterStatus.KnockedOut)
                return;

            condition.KnockOut(knockoutTime);
            flyingKick?.Cancel();
            horizontalVelocity = knockbackVelocity = Vector3.zero;
            verticalVelocity = 0f;
            jumping = false;
            SetHeight(PressDuckHeight, StandingHeight);
            capsule.enabled = false;
            SetLooseSelfCollisions(true);
            opponent?.RefreshCapsuleIgnores();
        }

        private void RunStatusTests(bool shift, Keyboard keyboard)
        {
            if (keyboard.kKey.wasPressedThisFrame)
            {
                AnimatedPillPlayer target = shift ? this : opponent;
                target?.KnockOut();
            }

        }

        private void SyncPassiveAnchor()
        {
            Vector3 hips = torso.Parent.Body.position;
            bool nearGround = arena.Raycast(new Ray(hips + Vector3.up * 0.25f, Vector3.down), 1.35f, out RaycastHit hit);
            // Only the inactive movement anchor follows the ragdoll. The limbs stay where physics left them.
            player.position = new Vector3(hips.x, nearGround ? hit.point.y : hips.y - 0.35f, hips.z);
        }

        private void StepCondition(float step)
        {
            if (torso?.Parent == null)
                return;

            bool wasUnconscious = Status == FighterStatus.KnockedOut;
            if (wasUnconscious)
                SyncPassiveAnchor();
            bool canRecover = arena.HasStandingClearance(player.position, StandingHeight + 0.19f);
            condition.Step(step, standUpTime, canRecover);
            if (wasUnconscious && Status == FighterStatus.Conscious)
            {
                capsule.enabled = true;
                RefreshCapsuleIgnores();
                opponent?.RefreshCapsuleIgnores();
            }

            if (Status == FighterStatus.Conscious && !IsRecovering)
                SetLooseSelfCollisions(false);
        }

        internal bool IsKickContact(HumanBodyBones bone)
            => bone == HumanBodyBones.RightFoot && flyingKick?.IsStriking == true;

        private void TryKickContact(PillBodyContact contact, Vector3 point)
        {
            AnimatedPillPlayer recipient = contact.Fighter;
            Vector3 direction = flyingKick.Direction;
            if (Vector3.Dot(direction, recipient.player.position - player.position) <= 0f ||
                !flyingKick.TryConsumeHit())
                return;

            BodyPart hitPart = contact.Part;
            float approachSpeed = Vector3.Dot(capsule.velocity, direction);
            float momentum = Mathf.InverseLerp(0f, RunSpeed, approachSpeed);
            float impulse = Mathf.Min(Mathf.Lerp(15f, 21f, momentum),
                hitPart.Body.mass * Mathf.Lerp(5.2f, 6.4f, momentum));
            hitPart.Body.AddForceAtPosition((direction + Vector3.up * 0.12f) * impulse, point, ForceMode.Impulse);
            ReactToImpact(hitPart, 1f);
            recipient.bodyStagger = Mathf.Max(recipient.bodyStagger, Mathf.Lerp(0.80f, 0.98f, momentum));
            recipient.AddKnockback(direction * Mathf.Lerp(2.5f, 3.6f, momentum));
        }

        private static void ReactToImpact(BodyPart part, float strength)
        {
            // Give the struck limb the most freedom, with less at each joint up the chain.
            for (int depth = 0; depth < 3 && part != null; depth++)
            {
                part.ImpactReaction = Mathf.Max(part.ImpactReaction, strength);
                part = part.Parent;
                strength *= 0.70f;
            }
        }

        internal void HandleMovementContact(ControllerColliderHit hit)
        {
            if (torso == null || Status != FighterStatus.Conscious || flyingKick?.IsActive == true || Time.time < nextBodyBumpTime ||
                !hit.collider.TryGetComponent(out PillMovementContact contact))
                return;

            AnimatedPillPlayer other = contact.Fighter;
            if (other.torso == null || Time.time < other.nextBodyBumpTime)
                return;

            // Capsules block each other before the chests touch. Use the speed actually
            // reached before the sweep so leaning on a blocked fighter does not count.
            Vector3 direction = Vector3.ProjectOnPlane(-hit.normal, Vector3.up).normalized;
            float approach = Mathf.Min(Vector3.Dot(previousMoveVelocity, direction),
                Vector3.Dot(horizontalVelocity, direction));
            if (approach < 2.5f)
                return;

            float otherSpeed = Vector3.Dot(other.capsule.velocity, direction);
            float effectiveMass = bodyMass * other.bodyMass / (bodyMass + other.bodyMass);
            float momentum = effectiveMass * (approach - otherSpeed);
            float strength = Mathf.InverseLerp(44f, 150f, momentum);
            if (strength <= 0f)
                return;

            float meetingSpeed = Mathf.Max(0f, -otherSpeed);
            float otherShare = Mathf.Lerp(0.40f, 1f, Mathf.Clamp01(approach / Mathf.Max(meetingSpeed, 0.01f)));
            float ownShare = Mathf.Lerp(0.40f, 1f, Mathf.Clamp01(meetingSpeed / approach));
            float impulse = Mathf.Min(momentum * 0.38f, 65f);
            nextBodyBumpTime = other.nextBodyBumpTime = Time.time + 0.24f;
            other.ReceiveBodyBump(direction, hit.point, impulse * otherShare, strength * otherShare);
            ReceiveBodyBump(-direction, hit.point, impulse * ownShare, strength * ownShare);
        }

        private void ReceiveBodyBump(Vector3 direction, Vector3 point, float impulse, float strength)
        {
            bodyStagger = Mathf.Max(bodyStagger, strength);
            ReactToImpact(torso, strength);
            Vector3 chestPoint = torso.Contact.ClosestPoint(point);
            torso.Body.AddForceAtPosition(direction * (impulse * 0.72f), chestPoint, ForceMode.Impulse);
            torso.Parent.Body.AddForce(direction * (impulse * 0.28f), ForceMode.Impulse);
            AddKnockback(direction * Mathf.Min(impulse / bodyMass * 2.2f, 3.5f));
        }

        private void AddKnockback(Vector3 shove)
        {
            if (Status == FighterStatus.KnockedOut)
                return;

            knockbackVelocity = Vector3.ClampMagnitude(knockbackVelocity + shove,
                Mathf.Max(knockbackVelocity.magnitude, shove.magnitude * 1.45f));
        }

        private void TryPunchContact(PillArmMotion arm, AnimatedPillPlayer recipient, BodyPart hitPart, Vector3 point)
        {
            Vector3 strike = Vector3.ProjectOnPlane(arm.StrikeDirection, Vector3.up).normalized;
            Vector3 toRecipient = Vector3.ProjectOnPlane(recipient.player.position - player.position, Vector3.up);
            if (Vector3.Dot(strike, toRecipient) <= 0f || !arm.TryConsumeHit())
                return;

            // An off-center punch can rotate the limb as well as push it.
            bool hitsCore = hitPart.Bone == HumanBodyBones.Hips;
            float impulse = Mathf.Min(hitPart.Body.mass * 4.2f, punchKnockbackSpeed * (hitsCore ? 1.6f : 1.1f));
            if (impulse > 0f)
            {
                hitPart.Body.AddForceAtPosition(arm.StrikeDirection * impulse, point, ForceMode.Impulse);
                float reaction = Mathf.Clamp01(impulse / hitPart.Body.mass);
                ReactToImpact(hitPart, reaction);
                if (hitsCore)
                    recipient.bodyStagger = Mathf.Max(recipient.bodyStagger, reaction * 0.58f);
            }

            bool planted = recipient.capsule.isGrounded && !recipient.jumping;
            float resistance = planted ? recipient.groundedPunchResistance : 0f;
            float pushSpeed = Mathf.Max(0f, punchKnockbackSpeed - resistance);
            if (pushSpeed <= 0f)
                return;

            recipient.AddKnockback(strike * pushSpeed);
        }

        private void Update()
        {
            if (arena == null || capsule == null)
                return;

            float step = Time.deltaTime;
            var keyboard = isPracticeDummy ? null : Keyboard.current;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame && gameObject.scene.name == "CityStage" &&
                    Application.CanStreamedLevelBeLoaded("MainMenu"))
                {
                    SceneManager.LoadSceneAsync("MainMenu");
                    return;
                }
                RunStatusTests(shift, keyboard);
                if (keyboard.pKey.wasPressedThisFrame)
                    arena.TogglePress();
            }

            bool recovering = IsRecovering;
            bool unconscious = Status == FighterStatus.KnockedOut;
            bool wantsCrouch = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            float requestedHeight = wantsCrouch ? CrouchHeight : StandingHeight;
            duckingUnderPress = !arena.HasStandingClearance(player.position, requestedHeight + 0.19f);
            if (!unconscious)
                SetHeight(recovering || duckingUnderPress ? PressDuckHeight : requestedHeight,
                    recovering ? StandingHeight : (float?)null);
            bool crouched = wantsCrouch || duckingUnderPress;

            var mouse = isPracticeDummy ? null : Mouse.current;
            if (mouse != null)
            {
                if (mouse.middleButton.isPressed)
                {
                    Vector2 drag = mouse.delta.ReadValue();
                    yaw += drag.x * 0.18f;
                    pitch = Mathf.Clamp(pitch - drag.y * 0.18f, 8f, 72f);
                }
                cameraDistance = Mathf.Clamp(cameraDistance - mouse.scroll.ReadValue().y * 0.006f * FighterScale, 2.5f * FighterScale, 10f * FighterScale);
                if (!unconscious && (recovering || !duckingUnderPress) && flyingKick?.IsActive != true && mouse.leftButton.wasPressedThisFrame)
                    StartJab(leftArm, mouse.position.ReadValue());
                if (!unconscious && (recovering || !duckingUnderPress) && flyingKick?.IsActive != true && mouse.rightButton.wasPressedThisFrame)
                    StartJab(rightArm, mouse.position.ReadValue());
            }

            Vector2 input = Vector2.zero;
            bool wantsJump = false;
            if (keyboard != null)
            {
                input = Vector2.ClampMagnitude(new Vector2(Axis(keyboard.dKey, keyboard.aKey),
                    Axis(keyboard.wKey, keyboard.sKey)), 1f);
                yaw += Axis(keyboard.eKey, keyboard.qKey) * 100f * step;
                wantsJump = keyboard.spaceKey.wasPressedThisFrame;
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    yaw = player.eulerAngles.y;
                    pitch = 18f;
                    cameraDistance = 5f * FighterScale;
                }
            }

            if (unconscious)
                return;

            Quaternion view = Quaternion.Euler(0f, yaw, 0f);
            Vector3 direction = view * new Vector3(input.x, 0f, input.y);
            if (flyingKick?.IsActive != true && direction.sqrMagnitude > 0.01f)
                player.rotation = Quaternion.RotateTowards(player.rotation,
                    Quaternion.LookRotation(direction), 720f * step);

            bool grounded = capsule.isGrounded && !jumping;
            float speed = crouched ? CrouchSpeed : shift ? RunSpeed : WalkSpeed;
            UpdateHorizontalMomentum(direction, speed, grounded, step);

            if (grounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                jumping = false;
                if (wantsJump && !crouched)
                {
                    verticalVelocity = JumpSpeed;
                    jumping = true;
                }
            }
            if (duckingUnderPress)
                flyingKick?.Cancel();
            bool airborne = jumping || (!capsule.isGrounded && capsule.velocity.y < -0.5f);
            if (!crouched && airborne && keyboard != null && keyboard.fKey.wasPressedThisFrame && flyingKick != null)
            {
                Vector3 kickDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : player.forward;
                flyingKick.Start(kickDirection);
                if (flyingKick.IsActive)
                    player.rotation = Quaternion.LookRotation(flyingKick.Direction);
            }

            verticalVelocity += Physics.gravity.y * step;
            previousMoveVelocity = capsule.velocity;
            Vector3 stumbleVelocity = GetStumbleVelocity();
            Vector3 requestedVelocity = horizontalVelocity + knockbackVelocity + stumbleVelocity;
            CollisionFlags hits = capsule.Move((requestedVelocity + Vector3.up * verticalVelocity) * step);
            Vector3 actualHorizontal = Vector3.ProjectOnPlane(capsule.velocity, Vector3.up);
            if ((hits & CollisionFlags.Sides) != 0)
                DropBlockedMomentum(requestedVelocity - actualHorizontal, stumbleVelocity);
            if ((hits & CollisionFlags.Below) != 0)
            {
                float braking = knockbackDeceleration * Mathf.Lerp(2f, 0.85f, bodyStagger);
                knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, braking * step);
            }
            if ((hits & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
            if ((hits & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            {
                jumping = false;
                flyingKick?.Land();
            }

            float actualSpeed = actualHorizontal.magnitude;
            int target = jumping && jump != null ? 3 :
                actualSpeed > 0.15f ? (actualSpeed > WalkSpeed + 0.35f && !crouched ? 2 : 1) : 0;
            UpdateAnimation(target, actualSpeed, step);
        }

        private void UpdateHorizontalMomentum(Vector3 direction, float speed, bool grounded, float step)
        {
            float control = Mathf.Lerp(1f, 0.70f, Mathf.Max(bodyStagger, externalImpact));
            bool hasInput = direction.sqrMagnitude > 0.01f;
            if (grounded)
            {
                float acceleration = hasInput ? GroundAcceleration : GroundBraking;
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, direction * (speed * control),
                    acceleration * step);
            }
            else if (hasInput)
            {
                // Limit speed gained from air input while carrying faster takeoff momentum.
                float airSpeed = Mathf.Max(horizontalVelocity.magnitude, Mathf.Min(speed, AirSpeedLimit) * control);
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, direction * airSpeed,
                    AirAcceleration * control * step);
            }
        }

        private float StumbleStrength => Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.28f, 0.88f, Mathf.Max(externalImpact, bodyStagger * 0.85f)));

        private Vector3 GetStumbleVelocity()
        {
            BodyPart pelvis = torso?.Parent;
            float stumble = StumbleStrength;
            if (pelvis == null || stumble <= 0f)
                return Vector3.zero;

            // Bring the movement capsule under the displaced hips during recovery.
            Vector3 drift = Vector3.ProjectOnPlane(pelvis.Body.position - pelvis.Start.position, Vector3.up);
            drift = Vector3.MoveTowards(drift, Vector3.zero, 0.16f);
            return Vector3.ClampMagnitude(drift * 4f, StumbleFollowSpeed) * stumble;
        }

        private void DropBlockedMomentum(Vector3 blockedVelocity, Vector3 stumbleVelocity)
        {
            if (blockedVelocity.sqrMagnitude < 0.01f)
                return;

            // A side collision spends the motion into the obstacle and leaves the slide along it.
            Vector3 blockedDirection = blockedVelocity.normalized;
            float inputSpeed = Mathf.Max(0f, Vector3.Dot(horizontalVelocity, blockedDirection));
            float knockbackSpeed = Mathf.Max(0f, Vector3.Dot(knockbackVelocity, blockedDirection));
            float inwardSpeed = inputSpeed + knockbackSpeed +
                Mathf.Max(0f, Vector3.Dot(stumbleVelocity, blockedDirection));
            if (inwardSpeed <= 0f)
                return;

            float blockedFraction = Mathf.Min(1f, blockedVelocity.magnitude / inwardSpeed);
            horizontalVelocity -= blockedDirection * (inputSpeed * blockedFraction);
            knockbackVelocity -= blockedDirection * (knockbackSpeed * blockedFraction);
        }

        private void FixedUpdate()
        {
            if (arena == null || capsule == null)
                return;

            float step = Time.fixedDeltaTime;
            if (!isPracticeDummy)
                arena.StepPress(step);
            StepCondition(step);
            if (flyingKick != null)
            {
                flyingKick.Step(step, !isPracticeDummy && Keyboard.current?.fKey.isPressed == true);
                motionRoot.localRotation = Quaternion.Euler(0f, -65f * flyingKick.Weight, 0f);
                flyingKick.UpdateLegs();
            }
            UpdateArmTargets(step);
            bodyStagger = Mathf.MoveTowards(bodyStagger, 0f, step * 1.65f);
            externalImpactHold = Mathf.MoveTowards(externalImpactHold, 0f, step);
            if (externalImpactHold <= 0f)
                externalImpact = Mathf.MoveTowards(externalImpact, 0f, step * 1.25f);

            if (Status == FighterStatus.KnockedOut)
                return;

            bool recovering = IsRecovering;
            float recovery = Mathf.SmoothStep(0f, 1f, condition.Recovery);
            if (recovering && torso?.Parent != null)
            {
                Rigidbody pelvis = torso.Parent.Body;
                Vector3 drift = Vector3.ProjectOnPlane(player.position - pelvis.position, Vector3.up);
                Vector3 drag = horizontalVelocity - Vector3.ProjectOnPlane(pelvis.linearVelocity, Vector3.up);
                pelvis.AddForce(Vector3.ClampMagnitude(drift * 10f + drag * 5f, 19f) * (1f - recovery),
                    ForceMode.Acceleration);
            }

            float stumble = StumbleStrength;
            float hipDrop = capsule.isGrounded ? StumbleHipDrop * stumble : 0f;
            float gravitySupport = Mathf.Lerp(0.90f, 0.10f, stumble) * recovery;
            foreach (BodyPart part in parts)
            {
                GetPose(part, out Vector3 position, out Quaternion rotation, out _);
                Rigidbody body = part.Body;

                PillArmMotion arm = ArmFor(part.Bone);
                Vector3 strike = arm?.StrikeDirection ?? Vector3.zero;
                bool drivingPunch = strike.sqrMagnitude > 0f;
                bool retractingPunch = arm?.IsRetracting == true;
                Vector3 velocityError = capsule.velocity - body.linearVelocity;
                if (drivingPunch)
                {
                    // Stabilize sideways motion while leaving the strike axis free of velocity
                    // damping. The motor keeps driving toward extension when a collider is hit.
                    velocityError = Vector3.ProjectOnPlane(velocityError, strike);
                }

                float stagger = bodyStagger * (part.Bone == HumanBodyBones.Hips ? 1f : 0.78f);
                float impact = externalImpact * (part.Bone == HumanBodyBones.Hips ? 1f : 0.92f);
                float combatReaction = drivingPunch ? stagger * 0.45f : Mathf.Max(stagger, part.ImpactReaction);
                float reaction = Mathf.Max(combatReaction, impact);
                part.ImpactReaction = Mathf.MoveTowards(part.ImpactReaction, 0f, step * 1.85f);
                float slack = stumble;
                if (capsule.isGrounded)
                {
                    // Keep the foot targets on the floor as the knees give under the hips.
                    float dropWeight = HipDropWeight(part.Bone);
                    position -= Vector3.up * (hipDrop * dropWeight);
                    slack *= Mathf.Lerp(0.55f, 1f, dropWeight);
                }
                float positionDrive = Mathf.Lerp(1f, 0.14f, reaction) * Mathf.Lerp(1f, 0.45f, slack);
                float rotationDrive = Mathf.Lerp(1f, 0.10f, reaction) * Mathf.Lerp(1f, 0.40f, slack);
                if (recovering)
                {
                    float drive = drivingPunch ? Mathf.Max(0.65f, recovery) : recovery;
                    positionDrive *= drive;
                    rotationDrive *= drive;
                }
                Vector3 positionError = position - body.position;
                float positionStrength = drivingPunch ? 300f : retractingPunch ? 200f : 145f;
                float maxAcceleration = drivingPunch ? 280f : retractingPunch ? 160f : 110f;
                Vector3 acceleration = (positionError * positionStrength
                    + velocityError * (drivingPunch ? 13f : 18f)) * positionDrive - Physics.gravity * gravitySupport;
                body.AddForce(Vector3.ClampMagnitude(acceleration, maxAcceleration), ForceMode.Acceleration);

                Quaternion delta = rotation * Quaternion.Inverse(body.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f)
                    angle -= 360f;
                Vector3 angular = (axis * (angle * Mathf.Deg2Rad * (drivingPunch ? 175f : 105f))
                    - body.angularVelocity * (drivingPunch ? 8f : 14f)) * rotationDrive;
                body.AddTorque(Vector3.ClampMagnitude(angular, drivingPunch ? 240f : 125f), ForceMode.Acceleration);
            }
        }

        private static float HipDropWeight(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 0f;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return 0.30f;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return 0.72f;
                default: return 1f;
            }
        }

        private void LateUpdate()
        {
            foreach (VisualJoin join in visualJoins)
                PlaceVisualJoin(join);

            if (followCamera == null)
                return;
            Vector3 focus = player.position + Vector3.up * Mathf.Min(capsule.height * 0.62f, 1.1f * FighterScale);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = orbit * Vector3.back;
            float distance = cameraDistance;
            if (arena.RaycastCamera(new Ray(focus, back), distance, out RaycastHit hit))
                distance = Mathf.Max(0.22f, hit.distance - 0.18f);
            followCamera.transform.SetPositionAndRotation(focus + back * distance, orbit);
        }

        private void SetHeight(float height, float? poseHeight = null)
        {
            if (!Mathf.Approximately(capsule.height, height) ||
                !Mathf.Approximately(capsule.center.y, height * 0.5f))
            {
                capsule.radius = height == PressDuckHeight ? 0.39f : 0.35f * FighterScale;
                capsule.height = height;
                capsule.center = Vector3.up * (height * 0.5f);
                capsule.stepOffset = height < StandingHeight ? 0.12f : 0.29f;
                RefreshCapsuleIgnores();
            }

            if (motionRoot != null)
                motionRoot.localScale = new Vector3(FighterScale,
                    FighterScale * (poseHeight ?? height) / StandingHeight, FighterScale);
        }

        private void RefreshCapsuleIgnores()
        {
            if (!capsule.enabled)
                return;

            foreach (BodyPart part in parts)
                Physics.IgnoreCollision(capsule, part.Contact);

            if (opponent == null)
                return;

            // Knocked-out bodies replace their movement shell with real limb contacts.
            bool ignoreOpponentLimbs = opponent.Status != FighterStatus.KnockedOut;
            foreach (BodyPart part in opponent.parts)
                Physics.IgnoreCollision(capsule, part.Contact, ignoreOpponentLimbs);
        }

        private void SetLooseSelfCollisions(bool enabled)
        {
            if (looseSelfCollisions == enabled)
                return;

            looseSelfCollisions = enabled;
            ApplySelfCollisionRules();
        }

        private void ApplySelfCollisionRules()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPart part = parts[i];
                part.Body.maxDepenetrationVelocity = looseSelfCollisions
                    ? Mathf.Min(3f, part.NormalDepenetrationSpeed) : part.NormalDepenetrationSpeed;
                for (int j = 0; j < i; j++)
                {
                    BodyPart other = parts[j];
                    bool shareJoint = part.Parent == other || other.Parent == part;
                    bool armAgainstBody = (part.Bone == HumanBodyBones.Hips && IsOuterArm(other.Bone)) ||
                        (other.Bone == HumanBodyBones.Hips && IsOuterArm(part.Bone));
                    // Adjacent shapes overlap at their joint by design.
                    bool ignore = shareJoint || (!looseSelfCollisions && !armAgainstBody);
                    Physics.IgnoreCollision(part.Contact, other.Contact, ignore);
                }
            }
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
            motionRoot.localScale = Vector3.one * FighterScale;
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
            flyingKick = new PillFlyingKick(animator);

            var teal = isPracticeDummy ? new Color(0.57f, 0.40f, 0.82f) : new Color(0.22f, 0.74f, 0.68f);
            var blue = new Color(0.2f, 0.36f, 0.53f);
            var orange = new Color(0.98f, 0.58f, 0.26f);
            var skin = new Color(0.73f, 0.49f, 0.32f);
            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.UpperChest, 0.44f, 0.255f, teal);
            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.LastBone, 0.37f, 0.285f, blue);
            AddPart(animator, HumanBodyBones.Neck, HumanBodyBones.Head, 0.145f, 0.145f, skin);
            AddPart(animator, HumanBodyBones.Head, HumanBodyBones.LastBone, 0.265f, 0.265f, skin);
            AddPart(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.215f, 0.208f, teal);
            AddPart(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.215f, 0.208f, teal);
            AddPart(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.173f, 0.166f, teal);
            AddPart(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.173f, 0.166f, teal);
            AddPart(animator, HumanBodyBones.LeftHand, HumanBodyBones.LastBone, 0.157f, 0.144f, skin);
            AddPart(animator, HumanBodyBones.RightHand, HumanBodyBones.LastBone, 0.157f, 0.144f, skin);
            AddPart(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.265f, 0.25f, teal);
            AddPart(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.265f, 0.25f, teal);
            AddPart(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.187f, 0.191f, teal);
            AddPart(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.187f, 0.191f, teal);
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
            if (first == null && start == HumanBodyBones.Neck)
                first = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            if (first == null)
                return;

            FighterAnatomy.Shape? anatomy = GetAnatomyShape(start, last != null);
            PrimitiveType shape = last != null ? PrimitiveType.Capsule : PrimitiveType.Sphere;
            Transform visual = CreateShape(start.ToString(), shape, color);
            if (anatomy.HasValue)
                visual.GetComponent<MeshFilter>().sharedMesh = GetAnatomyMesh(anatomy.Value);

            parts.Add(new BodyPart { Bone = start, Start = first, End = last, Width = width * FighterScale,
                Depth = depth * FighterScale, Visual = visual, Color = color, UsesAnatomyMesh = anatomy.HasValue });

        }

        private static FighterAnatomy.Shape? GetAnatomyShape(HumanBodyBones bone, bool hasEnd)
        {
            switch (bone)
            {
                case HumanBodyBones.Head: return null;
                case HumanBodyBones.Neck: return FighterAnatomy.Shape.Neck;
                case HumanBodyBones.Hips: return hasEnd ? FighterAnatomy.Shape.Chest : null;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm: return FighterAnatomy.Shape.UpperArm;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm: return FighterAnatomy.Shape.Forearm;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return FighterAnatomy.Shape.Hand;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return FighterAnatomy.Shape.Thigh;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return FighterAnatomy.Shape.Calf;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return FighterAnatomy.Shape.Foot;
                default: return null;
            }
        }

        private Mesh GetAnatomyMesh(FighterAnatomy.Shape shape)
        {
            if (!anatomyMeshes.TryGetValue(shape, out Mesh mesh))
            {
                mesh = FighterAnatomy.Create(shape);
                anatomyMeshes.Add(shape, mesh);
            }
            return mesh;
        }

        private void BuildPhysicalBody()
        {
            foreach (BodyPart part in parts)
            {
                GetPose(part, out Vector3 position, out Quaternion rotation, out Vector3 size);
                var root = new GameObject(part.Bone + " Physics");
                root.transform.SetParent(transform, false);
                root.transform.SetPositionAndRotation(position, rotation);

                if (part.Bone == HumanBodyBones.Head)
                {
                    var head = root.AddComponent<SphereCollider>();
                    head.radius = size.x * 0.5f;
                    part.Contact = head;
                }
                else if (part.Bone == HumanBodyBones.Hips)
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
                bodyMass += part.Body.mass;
                part.Body.interpolation = RigidbodyInterpolation.Interpolate;
                part.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                part.Body.solverIterations = 12;
                part.Body.maxAngularVelocity = 15f;
                part.NormalDepenetrationSpeed = part.Body.maxDepenetrationVelocity;
                PillArmMotion fist = part.Bone == HumanBodyBones.LeftHand || part.Bone == HumanBodyBones.RightHand
                    ? ArmFor(part.Bone) : null;
                root.AddComponent<PillBodyContact>().Initialize(this, part, fist);

                part.Visual.SetParent(root.transform, false);
                part.Visual.localPosition = Vector3.zero;
                part.Visual.localRotation = Quaternion.identity;
                part.Visual.localScale = part.End != null && !part.UsesAnatomyMesh
                    ? new Vector3(size.x, size.y * 0.5f, size.z)
                    : size;
                Physics.IgnoreCollision(capsule, part.Contact);
            }

            foreach (BodyPart part in parts)
            {
                part.Parent = FindPhysicalParent(part);
                if (part.Parent != null)
                    JoinParts(part, part.Parent);
            }
            ApplySelfCollisionRules();
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
                    BodyPart neck = parts.Find(candidate => candidate.Bone == HumanBodyBones.Neck);
                    if (neck != null)
                        return neck;
                    goto case HumanBodyBones.Neck;
                case HumanBodyBones.Neck:
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
            part.Joint = joint;
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
            CreateVisualJoin(joint, part, parent);
        }

        private void CreateVisualJoin(ConfigurableJoint joint, BodyPart child, BodyPart parent)
        {
            float width = Mathf.Min(Mathf.Min(child.Width, child.Depth), Mathf.Min(parent.Width, parent.Depth));
            var join = new VisualJoin
            {
                Joint = joint,
                Child = child,
                Parent = parent,
                Shape = CreateShape(child.Bone + " Skin Join", PrimitiveType.Capsule, child.Color),
                Width = width * 0.98f
            };
            visualJoins.Add(join);
            PlaceVisualJoin(join);
        }

        private static void PlaceVisualJoin(VisualJoin join)
        {
            Transform child = join.Child.Body.transform;
            Transform parent = join.Parent.Body.transform;
            Vector3 childPivot = child.TransformPoint(join.Joint.anchor);
            Vector3 parentPivot = parent.TransformPoint(join.Joint.connectedAnchor);
            // Start the sleeve inside both visible pieces so it stays connected as
            // those pieces rotate and the rounded ends move away from the joint.
            Vector3 start = Vector3.Lerp(parent.position, parentPivot, 0.52f);
            Vector3 end = Vector3.Lerp(child.position, childPivot, 0.52f);
            Vector3 axis = end - start;
            Quaternion rotation = axis.sqrMagnitude > 0.00001f
                ? Quaternion.FromToRotation(Vector3.up, axis)
                : child.rotation;
            join.Shape.SetPositionAndRotation((start + end) * 0.5f, rotation);
            float length = axis.magnitude + join.Width * 0.82f;
            join.Shape.localScale = new Vector3(join.Width, length * 0.5f, join.Width * 0.94f);
        }

        private void GetPose(BodyPart part, out Vector3 position, out Quaternion rotation, out Vector3 size)
        {
            Vector3 start = part.Start.position;
            Vector3 end = part.End != null ? part.End.position : start;
            PillArmMotion arm = ArmFor(part.Bone);
            if (arm != null)
            {
                Vector3 elbow = arm.Elbow;
                Vector3 hand = arm.Hand;
                if (flyingKick?.IsActive == true)
                    flyingKick.GetGuard(arm, arm == rightArm, out elbow, out hand);
                switch (part.Bone)
                {
                    case HumanBodyBones.LeftUpperArm:
                    case HumanBodyBones.RightUpperArm:
                        start = arm.Shoulder;
                        end = elbow;
                        break;
                    case HumanBodyBones.LeftLowerArm:
                    case HumanBodyBones.RightLowerArm:
                        start = elbow;
                        end = hand;
                        break;
                    default:
                        start = end = hand;
                        break;
                }
            }

            flyingKick?.ShapeLeg(part.Bone, ref start, ref end);
            Vector3 span = end - start;
            position = (start + end) * 0.5f;
            Quaternion stance = flyingKick?.IsActive == true && part.Bone != HumanBodyBones.Head
                ? motionRoot.rotation : player.rotation;
            rotation = span.sqrMagnitude > 0.00001f
                ? Quaternion.FromToRotation(player.up, span) * stance
                : stance;
            float height = Mathf.Max(span.magnitude * 1.25f, part.Width);

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

            return new PillArmMotion(player, shoulder, elbow, wrist, left ? -1f : 1f, FighterScale);
        }

        private void UpdateArmTargets(float step)
        {
            if (torso == null)
                return;

            bool recovering = IsRecovering;
            Vector3 center = recovering ? torso.Body.position : (torso.Start.position + torso.End.position) * 0.5f;
            var mouse = isPracticeDummy ? null : Mouse.current;
            bool blockJabs = Status == FighterStatus.KnockedOut || (duckingUnderPress && !recovering) || flyingKick?.IsActive == true;
            leftArm?.Update(step, center, blockJabs, mouse != null && mouse.leftButton.isPressed,
                recovering ? PhysicalShoulder(leftShoulder) : null);
            rightArm?.Update(step, center, blockJabs, mouse != null && mouse.rightButton.isPressed,
                recovering ? PhysicalShoulder(rightShoulder) : null);
        }

        private static Vector3? PhysicalShoulder(BodyPart part)
        {
            if (part?.Joint == null)
                return null;
            return part.Joint.connectedBody.transform.TransformPoint(part.Joint.connectedAnchor);
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
            shape.localPosition = Vector3.up * (StandingHeight * 0.5f);
            shape.localScale = new Vector3(0.66f, 0.925f, 0.66f) * FighterScale;
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

            clips[1].SetSpeed(Mathf.Clamp(speed / (2f * FighterScale), 0.4f, 1.45f));
            clips[2].SetSpeed(Mathf.Clamp(speed / (4f * FighterScale), 0.4f, 1.45f));
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            foreach (Mesh mesh in anatomyMeshes.Values)
                Destroy(mesh);
        }

        private static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive,
            UnityEngine.InputSystem.Controls.KeyControl negative)
        {
            return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
        }
    }
}
