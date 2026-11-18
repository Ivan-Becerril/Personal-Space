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
        private PillArenaCamera arenaCamera;
        private PillGripTargeting gripTargeting;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float requestedGripTension;
        private float gripTension;
        private float gripPullExpires;
        private bool hanging;
        private float hangWeight;
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
        private readonly PillClimbing climbing = new PillClimbing();
        private BodyPart torso;
        private BodyPart leftShoulder;
        private BodyPart rightShoulder;
        private bool looseSelfCollisions;
        private readonly System.Collections.Generic.List<BodyPart> parts =
            new System.Collections.Generic.List<BodyPart>();

        private SkinnedMeshRenderer fighterModel;
        private PhysicsMaterial footGrip;
        private RagdollFootContact leftFootContact;
        private RagdollFootContact rightFootContact;
        private float groundLoad;

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
            internal float Width;
            internal float Depth;
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
            if (followCamera != null)
                arenaCamera = new PillArenaCamera(followCamera);
            if (!isPracticeDummy)
                gripTargeting = new PillGripTargeting(this);
            BuildMotionRig();
            torso = parts.Find(part => part.Bone == HumanBodyBones.Hips && part.End != null);
            if (torso != null)
            {
                leftArm = CreateArm(true);
                rightArm = CreateArm(false);
                UpdateArmTargets(0f);
            }
            BuildPhysicalBody();
            if (parts.Count > 0)
            {
                fighterModel = FighterAnatomy.Build(player, parts, isPracticeDummy);
                if (fighterModel == null)
                    CreateFallbackPill();
            }
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
            PillBodyContact otherPart = otherBody != null ? otherBody.GetComponent<PillBodyContact>() : null;
            ContactPoint contact = collision.GetContact(0);
            if (arm?.WantsGrip == true && !climbing.IsPullingOver && Status == FighterStatus.Conscious &&
                (otherPart == null || otherPart.Fighter != this))
                arm.TryGrip(ownPart.Body, collision.collider, otherBody, contact.point, contact.normal);

            if (otherBody == null)
                return;
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
            leftArm?.ResetInput();
            rightArm?.ResetInput();
            climbing.Cancel();
            flyingKick?.Cancel();
            horizontalVelocity = knockbackVelocity = Vector3.zero;
            requestedGripTension = gripTension = 0f;
            gripPullExpires = 0f;
            hanging = false;
            hangWeight = 0f;
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
            bool canUseArms = !unconscious && flyingKick?.IsActive != true;
            bool canJab = recovering || !duckingUnderPress;
            if (canUseArms)
            {
                Vector2 cursor = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
                ReadHandButton(leftArm, mouse?.leftButton, cursor, canJab, step);
                ReadHandButton(rightArm, mouse?.rightButton, cursor, canJab, step);

            }
            else
            {
                leftArm?.ResetInput();
                rightArm?.ResetInput();
            }

            bool holding = leftArm?.GrabHeld == true || rightArm?.GrabHeld == true;
            bool hoisting = keyboard?.spaceKey.isPressed == true;


            Vector2 input = Vector2.zero;
            bool wantsJump = false;
            if (keyboard != null)
            {
                input = Vector2.ClampMagnitude(new Vector2(Axis(keyboard.dKey, keyboard.aKey),
                    Axis(keyboard.wKey, keyboard.sKey)), 1f);
                wantsJump = keyboard.spaceKey.wasPressedThisFrame;
            }

            if (unconscious)
                return;

            Quaternion view = Quaternion.Euler(0f, arenaCamera?.Yaw ?? 0f, 0f);
            Vector3 direction = view * new Vector3(input.x, 0f, input.y);
            BodyPart pelvis = torso?.Parent;
            Vector3 hipOffset = pelvis != null ? pelvis.Start.position - player.position : Vector3.up;
            Vector3 hips = pelvis != null ? pelvis.Body.position : player.position + hipOffset;
            bool wasHanging = hanging;
            bool isClimbing = climbing.TryMove(capsule, player, hipOffset, hips, SupportedFootHeight(),
                leftArm, rightArm, direction, hoisting, holding, ClimbingShoulder(), out Vector3 climbVelocity,
                out Vector3 climbHeading);
            hanging = isClimbing;
            if (wasHanging && !hanging && pelvis != null && !capsule.isGrounded)
                verticalVelocity = pelvis.Body.linearVelocity.y;
            bool latched = leftArm?.HasGrip == true || rightArm?.HasGrip == true;
            if (isClimbing)
                player.rotation = Quaternion.RotateTowards(player.rotation, Quaternion.LookRotation(climbHeading), 260f * step);
            else if (!latched && flyingKick?.IsActive != true && direction.sqrMagnitude > 0.01f)
                player.rotation = Quaternion.RotateTowards(player.rotation,
                    Quaternion.LookRotation(direction), 720f * step);

            bool grounded = capsule.isGrounded && !jumping;
            float speed = crouched ? CrouchSpeed : shift ? RunSpeed : WalkSpeed;
            var leftHeld = HeldFighterPart(leftArm);
            var rightHeld = HeldFighterPart(rightArm);
            if (leftHeld != null || rightHeld != null)
                speed = Mathf.Min(speed, 2.65f);
            UpdateHorizontalMomentum(direction, speed, grounded, step);

            if (grounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                jumping = false;
                if (wantsJump && !crouched && !isClimbing)
                {
                    verticalVelocity = JumpSpeed;
                    jumping = true;
                }
            }
            if (duckingUnderPress)
                flyingKick?.Cancel();
            bool airborne = jumping || (!capsule.isGrounded && capsule.velocity.y < -0.5f);
            if (!holding && !crouched && airborne && keyboard != null && keyboard.fKey.wasPressedThisFrame && flyingKick != null)
            {
                Vector3 kickDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : player.forward;
                flyingKick.Start(kickDirection);
                if (flyingKick.IsActive)
                    player.rotation = Quaternion.LookRotation(flyingKick.Direction);
            }

            Vector3 correction = Vector3.Lerp(GetStumbleVelocity(), GetGripFollowVelocity(), gripTension);
            if (isClimbing)
            {
                horizontalVelocity = Vector3.zero;
                verticalVelocity = 0f;
                jumping = false;
                correction = climbVelocity;
            }
            else
                verticalVelocity += Physics.gravity.y * step;


            previousMoveVelocity = capsule.velocity;
            Vector3 requestedVelocity = horizontalVelocity + knockbackVelocity + Vector3.ProjectOnPlane(correction, Vector3.up);
            Vector3 movement = requestedVelocity + Vector3.up * (verticalVelocity + correction.y);
            CollisionFlags hits = capsule.Move(movement * step);
            Vector3 actualHorizontal = Vector3.ProjectOnPlane(capsule.velocity, Vector3.up);
            if (leftHeld != null)
                PullHeldFighter(leftHeld, actualHorizontal, rightHeld != null && rightHeld.Fighter == leftHeld.Fighter);
            if (rightHeld != null && (leftHeld == null || leftHeld.Fighter != rightHeld.Fighter))
                PullHeldFighter(rightHeld, actualHorizontal, false);
            if ((hits & CollisionFlags.Sides) != 0)
                DropBlockedMomentum(requestedVelocity - actualHorizontal, correction);
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

            float gaitSpeed = isClimbing ? 0f : GetGaitSpeed(direction, actualHorizontal);
            int target = jumping && jump != null ? 3 :
                gaitSpeed > 0.15f ? (gaitSpeed > WalkSpeed + 0.35f && !crouched ? 2 : 1) : 0;
            UpdateAnimation(target, gaitSpeed, step);
        }

        private float GetGaitSpeed(Vector3 direction, Vector3 actualHorizontal)
        {
            // Only movement the fighter is trying to make should advance a walk cycle.
            return Mathf.Min(Mathf.Max(0f, Vector3.Dot(actualHorizontal, direction)), horizontalVelocity.magnitude);
        }

        private static PillBodyContact HeldFighterPart(PillArmMotion arm)
            => arm?.HasGrip == true ? arm.HeldFighterPart : null;

        private void PullHeldFighter(PillBodyContact held, Vector3 travel, bool twoHands)
        {
            AnimatedPillPlayer other = held.Fighter;
            if (other == null || other.Status == FighterStatus.KnockedOut || travel.sqrMagnitude < 0.09f)
                return;

            Vector3 separation = Vector3.ProjectOnPlane(player.position - other.player.position, Vector3.up);
            if (Vector3.Dot(travel, separation.normalized) < -0.1f)
                return;

            // Signal the pose to yield. The grip joint supplies the actual pull.
            float stretch = Mathf.InverseLerp(0.90f, 1.70f, separation.magnitude);
            other.requestedGripTension = Mathf.Lerp(twoHands ? 0.85f : 0.72f, 1f, stretch);
            other.gripPullExpires = Time.time + 0.10f;
            if (held.Part != null)
                held.Part.ImpactReaction = Mathf.Max(held.Part.ImpactReaction, 0.34f);
        }

        private Vector3 GetGripFollowVelocity()
        {
            if (gripTension <= 0f)
                return Vector3.zero;

            Vector3 drift = Vector3.zero;
            float weight = 0f;
            foreach (BodyPart part in parts)
            {
                if (part.Bone != HumanBodyBones.Hips && !IsLowerBody(part.Bone))
                    continue;

                GetPose(part, out Vector3 pose, out _, out _);
                drift += (part.Body.worldCenterOfMass - pose) * part.Body.mass;
                weight += part.Body.mass;
            }

            if (weight <= 0f)
                return Vector3.zero;
            drift = Vector3.ProjectOnPlane(drift / weight, Vector3.up);
            return Vector3.ClampMagnitude(Vector3.MoveTowards(drift, Vector3.zero, 0.08f) * 8f, 3.4f);
        }

        private void ReadHandButton(PillArmMotion arm, UnityEngine.InputSystem.Controls.ButtonControl button,
            Vector2 cursor, bool canJab, float step)
        {
            if (arm == null)
                return;
            if (button == null)
            {
                arm.ResetInput();
                return;
            }

            bool tapped = arm.UpdateButton(button.wasPressedThisFrame, button.wasReleasedThisFrame, button.isPressed, step);
            if (tapped && canJab)
                StartJab(arm, cursor);
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

            float requestedTension = Time.time < gripPullExpires ? requestedGripTension : 0f;
            gripTension = Mathf.MoveTowards(gripTension, requestedTension, step * 6f);
            hangWeight = Mathf.MoveTowards(hangWeight, hanging ? 1f : 0f, step * 6f);
            float stumble = StumbleStrength;
            bool feetSupported = leftFootContact?.HasSupport == true || rightFootContact?.HasSupport == true;
            float expectedLoad = feetSupported ? 1f : capsule.isGrounded ? 0.65f : 0f;
            groundLoad = Mathf.MoveTowards(groundLoad, expectedLoad, step * 7f);
            if (hanging)
                ApplyClimbPhysics();

            float hipDrop = capsule.isGrounded ? StumbleHipDrop * stumble : 0f;
            float gravitySupport = Mathf.Lerp(0.86f, 0.16f, groundLoad) * (1f - stumble * 0.78f) * recovery;
            gravitySupport *= Mathf.Lerp(1f, 0.28f, gripTension) * (1f - hangWeight);
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
                float limbYield = LimbYield(part.Bone, drivingPunch || arm?.HasGrip == true || arm?.IsReachingUp == true);
                bool tuckingLeg = climbing.IsPullingOver && IsLowerBody(part.Bone);
                if (tuckingLeg)
                {
                    float tuck = 1f - HipDropWeight(part.Bone);
                    position += climbing.IntoLedge * (0.47f * tuck);
                    position.y = Mathf.Lerp(position.y, Mathf.Max(position.y, climbing.Landing.y + 0.16f), tuck);
                    limbYield *= 0.58f;
                }
                positionDrive *= Mathf.Lerp(1f, 0.025f, limbYield);
                rotationDrive *= Mathf.Lerp(1f, 0.025f, limbYield);
                float weightSupport = gravitySupport * Mathf.Lerp(1f, 0.07f, limbYield);
                Vector3 positionError = position - body.position;
                float positionStrength = drivingPunch ? 300f : retractingPunch || arm?.WantsGrip == true ? 200f : 145f;
                float maxAcceleration = drivingPunch ? 280f : retractingPunch ? 160f : 110f;
                Vector3 poseAcceleration = (positionError * positionStrength
                    + velocityError * (drivingPunch ? 13f : 18f)) * positionDrive;
                if (poseAcceleration.y > 0f && !drivingPunch)
                {
                    // Let planted feet and joint constraints support the upper body.
                    float load = part.Bone == HumanBodyBones.Hips || part.Bone == HumanBodyBones.Neck ||
                        part.Bone == HumanBodyBones.Head ? groundLoad : 0f;
                    poseAcceleration.y *= Mathf.Lerp(1f, 0.48f, load);
                }
                if (arm?.HasGrip != true && arm?.IsReachingUp != true && !tuckingLeg && !drivingPunch)
                    poseAcceleration.y *= Mathf.Lerp(1f, 0.07f, hangWeight);
                Vector3 acceleration = poseAcceleration - Physics.gravity * weightSupport;
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

        private void ApplyClimbPhysics()
        {
            bool leftBraced = IsClimbingGrip(leftArm);
            bool rightBraced = IsClimbingGrip(rightArm);
            if (!leftBraced && !rightBraced)
                return;

            bool hoisting = !isPracticeDummy && Keyboard.current?.spaceKey.isPressed == true;
            bool pressing = hoisting && climbing.IsPullingOver;
            float limit = leftBraced && rightBraced ? 630f : 950f;
            if (leftBraced)
                leftArm.PullFromGrip(leftShoulder?.Joint, climbing.IntoLedge, hoisting, pressing, limit);
            if (rightBraced)
                rightArm.PullFromGrip(rightShoulder?.Joint, climbing.IntoLedge, hoisting, pressing, limit);

            torso.Body.AddForce(climbing.Swing * (bodyMass * 2.4f));
        }

        private bool IsClimbingGrip(PillArmMotion arm) => climbing.IsPullingOver
            ? PillClimbing.CanBraceWith(arm) : PillClimbing.CanHangFrom(arm, player.position);

        private Vector3? ClimbingShoulder()
        {
            bool leftBraced = PillClimbing.CanHangFrom(leftArm, player.position);
            bool rightBraced = PillClimbing.CanHangFrom(rightArm, player.position);
            if (leftBraced && (!rightBraced || leftArm.GripPoint.y >= rightArm.GripPoint.y))
                return PhysicalShoulder(leftShoulder);
            return rightBraced ? PhysicalShoulder(rightShoulder) : null;
        }

        private float SupportedFootHeight()
        {
            float left = leftFootContact?.HasSupport == true ? leftFootContact.transform.position.y : float.NegativeInfinity;
            float right = rightFootContact?.HasSupport == true ? rightFootContact.transform.position.y : float.NegativeInfinity;
            return Mathf.Max(left, right);
        }

        private float LimbYield(HumanBodyBones bone, bool activelyUsingArm)
        {
            if (activelyUsingArm)
                return 0f;
            switch (bone)
            {
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                    return Mathf.Max(Mathf.Clamp01(gripTension * 1.35f), hangWeight);
                case HumanBodyBones.Hips:
                    return Mathf.Max(gripTension * 0.78f, hangWeight * 0.42f);
                default:
                    return Mathf.Max(gripTension * 0.80f, hangWeight * 0.68f);
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


            if (arenaCamera != null)
                arenaCamera.Track(CameraFocus(), opponent != null ? opponent.CameraFocus() : (Vector3?)null,
                    arena, Time.deltaTime);
        }

        private Vector3 CameraFocus()
            => player.position + Vector3.up * Mathf.Min(capsule.height * 0.62f, 1.05f * FighterScale);

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
            if (capsule == null || !capsule.enabled)
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

        private void BuildMotionRig()
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


            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.UpperChest, 0.44f, 0.255f);
            AddPart(animator, HumanBodyBones.Hips, HumanBodyBones.LastBone, 0.37f, 0.285f);
            AddPart(animator, HumanBodyBones.Neck, HumanBodyBones.Head, 0.145f, 0.145f);
            AddPart(animator, HumanBodyBones.Head, HumanBodyBones.LastBone, 0.265f, 0.265f);
            AddPart(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.215f, 0.208f);
            AddPart(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.215f, 0.208f);
            AddPart(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.173f, 0.166f);
            AddPart(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.173f, 0.166f);
            AddPart(animator, HumanBodyBones.LeftHand, HumanBodyBones.LastBone, 0.157f, 0.144f);
            AddPart(animator, HumanBodyBones.RightHand, HumanBodyBones.LastBone, 0.157f, 0.144f);
            AddPart(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.265f, 0.25f);
            AddPart(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.265f, 0.25f);
            AddPart(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.187f, 0.191f);
            AddPart(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.187f, 0.191f);
            AddPart(animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 0.17f, 0.21f);
            AddPart(animator, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 0.17f, 0.21f);
        }

        private void AddPart(Animator animator, HumanBodyBones start, HumanBodyBones end,
            float width, float depth)
        {
            Transform first = animator.GetBoneTransform(start);
            Transform last = end == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(end);
            if (start == HumanBodyBones.Hips && end == HumanBodyBones.UpperChest && last == null)
                last = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (first == null && start == HumanBodyBones.Neck)
                first = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            if (first == null)
                return;

            parts.Add(new BodyPart { Bone = start, Start = first, End = last, Width = width * FighterScale,
                Depth = depth * FighterScale });
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
                part.Body.mass = PartMass(part);
                if (part.Bone == HumanBodyBones.LeftFoot || part.Bone == HumanBodyBones.RightFoot)
                    SetUpFoot(part, root);
                bodyMass += part.Body.mass;
                part.Body.interpolation = RigidbodyInterpolation.Interpolate;
                part.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                // Give the loaded arm chain more solver passes without changing its freedom to bend.
                bool supportsGrip = part.Bone == HumanBodyBones.Hips || ArmFor(part.Bone) != null;
                part.Body.solverIterations = supportsGrip ? 24 : 12;
                if (supportsGrip)
                    part.Body.solverVelocityIterations = 8;
                part.Body.maxAngularVelocity = 15f;
                part.NormalDepenetrationSpeed = part.Body.maxDepenetrationVelocity;
                PillArmMotion fist = part.Bone == HumanBodyBones.LeftHand || part.Bone == HumanBodyBones.RightHand
                    ? ArmFor(part.Bone) : null;
                root.AddComponent<PillBodyContact>().Initialize(this, part, fist);


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

        private static float PartMass(BodyPart part)
        {
            switch (part.Bone)
            {
                case HumanBodyBones.Hips: return part.End != null ? 8.5f : 5f;
                case HumanBodyBones.Head: return 3.3f;
                case HumanBodyBones.Neck: return 0.8f;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm: return 1.6f;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm: return 1.1f;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return 0.65f;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return 4.4f;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return 2.6f;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 1.1f;
                default: return 1.7f;
            }
        }

        private static bool IsLowerBody(HumanBodyBones bone) => HipDropWeight(bone) < 1f;

        private void SetUpFoot(BodyPart part, GameObject root)
        {
            if (footGrip == null)
                footGrip = new PhysicsMaterial("Fighter Foot Grip")
                {
                    staticFriction = 0.85f,
                    dynamicFriction = 0.60f,
                    frictionCombine = PhysicsMaterialCombine.Maximum
                };
            part.Contact.sharedMaterial = footGrip;
            var sensor = root.AddComponent<RagdollFootContact>();
            if (part.Bone == HumanBodyBones.LeftFoot)
                leftFootContact = sensor;
            else
                rightFootContact = sensor;
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
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.025f;
            joint.projectionAngle = 12f;
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
            Vector3 center = recovering || hanging ? torso.Body.position : (torso.Start.position + torso.End.position) * 0.5f;
            bool blockJabs = Status == FighterStatus.KnockedOut || (duckingUnderPress && !recovering) || flyingKick?.IsActive == true;
            bool leftBraced = hanging && IsClimbingGrip(leftArm);
            bool rightBraced = hanging && IsClimbingGrip(rightArm);
            leftArm?.Update(step, center, blockJabs,
                recovering || hanging ? PhysicalShoulder(leftShoulder) : null, gripTargeting,
                rightBraced && !leftBraced ? rightArm.GripPoint : (Vector3?)null, climbing.IntoLedge);
            rightArm?.Update(step, center, blockJabs,
                recovering || hanging ? PhysicalShoulder(rightShoulder) : null, gripTargeting,
                leftBraced && !rightBraced ? leftArm.GripPoint : (Vector3?)null, climbing.IntoLedge);
        }

        private static Vector3? PhysicalShoulder(BodyPart part)
        {
            if (part?.Joint == null)
                return null;
            Rigidbody parent = part.Joint.connectedBody;
            return parent.position + parent.rotation * part.Joint.connectedAnchor;
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

        private void OnDisable()
        {
            leftArm?.ResetInput();
            rightArm?.ResetInput();
            climbing.Cancel();
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            if (footGrip != null)
                Destroy(footGrip);
            if (fighterModel != null)
            {
                foreach (Material material in fighterModel.sharedMaterials)
                    Destroy(material);
                Destroy(fighterModel.sharedMesh);
            }
        }

        private static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive,
            UnityEngine.InputSystem.Controls.KeyControl negative)
        {
            return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
        }
    }
}
