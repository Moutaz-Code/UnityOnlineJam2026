using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumber.POC
{
    /// <summary>
    /// Galaxy-style walker on a polyhedral planet. All state lives in the die's local space, so the
    /// player rides along for free when the die tumbles. Gravity is the inverse of the face normal,
    /// blended across edges, not "towards the centre" (which would make every face feel like a slope).
    /// Controls: WASD move, hold Space to charge, release to jump, Space or left mouse in the air to slam.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PlanetWalker : MonoBehaviour
    {
        public DiePlanet die;
        public PlanetCamera cameraRig;

        [Header("Movement")]
        public float moveSpeed = 6f;
        public float turnSharpness = 12f;
        [Tooltip("How sharply the up vector follows the surface. Lower = softer edge crossings.")]
        public float upSharpness = 10f;
        [Tooltip("Width of the edge blend, in dot-product units. 0 = hard snap at edges.")]
        public float edgeBlend = 0.04f;

        [Header("Jump and slam")]
        public float gravity = 30f;
        public float jumpSpeed = 8f;
        public float chargedJumpSpeed = 14f;
        public float chargeTime = 0.8f;
        public float slamSpeed = 35f;
        public float slamForceMin = 250f;
        public float slamForceMax = 450f;
        [Tooltip("Share of the slam pushed along your facing, so a slam dead centre on a face still tips the die.")]
        [Range(0f, 1f)] public float forwardShare = 0.35f;

        public bool slamEnabled = true;

        public event Action<Vector3, float> Slammed; // world point, force

        public float Charge01 { get; private set; }
        public bool Grounded { get; private set; } = true;
        public bool Slamming { get; private set; }
        public Vector3 LocalPosition => _localPos;
        public int CurrentFace => die.FaceAtLocalDirection(_localPos.normalized);

        Vector3 _localPos, _localUp, _localForward;
        float _verticalSpeed, _jumpCharge;

        void Start()
        {
            // Start in the middle of the face pointing at the sun.
            int face = die.FaceFacingWorld(die.SunDirection);
            _localUp = die.LocalNormal(face);
            _localPos = die.LocalCentre(face);
            _localForward = Vector3.ProjectOnPlane(Vector3.forward, _localUp).normalized;
            if (_localForward.sqrMagnitude < 0.01f) _localForward = Vector3.ProjectOnPlane(Vector3.right, _localUp).normalized;
            ApplyTransform();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool dieIdle = die.State == DiePlanet.RollState.Idle;

            // ---- input ----
            Vector2 move = Vector2.zero;
            bool jumpHeld = false, jumpPressed = false, jumpReleased = false, slamPressed = false;
            if (kb != null && dieIdle)
            {
                if (kb.wKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed) move.x -= 1;
                jumpHeld = kb.spaceKey.isPressed;
                jumpPressed = kb.spaceKey.wasPressedThisFrame;
                jumpReleased = kb.spaceKey.wasReleasedThisFrame;
            }
            if (mouse != null && dieIdle) slamPressed = mouse.leftButton.wasPressedThisFrame;
            move = Vector2.ClampMagnitude(move, 1f);

            // ---- planar movement, camera relative, in die space ----
            if (move.sqrMagnitude > 0f && cameraRig != null)
            {
                Vector3 camFwd = die.transform.InverseTransformDirection(cameraRig.PlanarForward);
                camFwd = Vector3.ProjectOnPlane(camFwd, _localUp).normalized;
                Vector3 camRight = Vector3.Cross(_localUp, camFwd);
                Vector3 wish = (camFwd * move.y + camRight * move.x);
                _localPos += wish * (moveSpeed * dt);
                if (wish.sqrMagnitude > 1e-4f)
                    _localForward = Vector3.Slerp(_localForward, wish.normalized, 1f - Mathf.Exp(-turnSharpness * dt));
            }

            // ---- charge, jump, slam ----
            if (Grounded)
            {
                if (jumpHeld && slamEnabled) Charge01 = Mathf.Clamp01(Charge01 + dt / chargeTime);
                if (jumpReleased || (!slamEnabled && jumpPressed))
                {
                    _jumpCharge = Charge01;
                    _verticalSpeed = Mathf.Lerp(jumpSpeed, chargedJumpSpeed, Charge01);
                    Grounded = false;
                    Charge01 = 0f;
                }
            }
            else if (!Slamming && slamEnabled && (jumpPressed || slamPressed))
            {
                Slamming = true;
                _verticalSpeed = -slamSpeed;
            }

            // ---- gravity and ground ----
            if (!Grounded)
            {
                _verticalSpeed -= gravity * dt;
                _localPos += _localUp * (_verticalSpeed * dt);
            }
            int face = die.FaceAtLocalDirection(_localPos.normalized);
            Vector3 n = die.LocalNormal(face);
            float height = Vector3.Dot(_localPos, n) - die.Inradius;
            if (Grounded || (height <= 0f && _verticalSpeed <= 0f))
            {
                _localPos -= n * height; // stand exactly on the face plane
                if (!Grounded) Land();
            }

            // ---- up vector: face normal, blended near edges ----
            Vector3 targetUp = die.BlendedLocalUp(_localPos.normalized, edgeBlend);
            _localUp = Vector3.Slerp(_localUp, targetUp, 1f - Mathf.Exp(-upSharpness * dt)).normalized;
            _localForward = Vector3.ProjectOnPlane(_localForward, _localUp).normalized;
            if (_localForward.sqrMagnitude < 1e-4f) _localForward = Vector3.ProjectOnPlane(die.LocalCentre(face), _localUp).normalized;

            ApplyTransform();
        }

        void Land()
        {
            Grounded = true;
            _verticalSpeed = 0f;
            if (!Slamming) return;
            Slamming = false;

            float force = Mathf.Lerp(slamForceMin, slamForceMax, _jumpCharge);
            Vector3 up = transform.up, fwd = transform.forward;
            ApplyTransform();
            Vector3 point = transform.position;
            Vector3 impulse = -up * force + fwd * (force * forwardShare);
            die.ApplyImpulse(point, impulse);
            Slammed?.Invoke(point, force);
        }

        void ApplyTransform()
        {
            Transform d = die.transform;
            transform.position = d.TransformPoint(_localPos);
            transform.rotation = Quaternion.LookRotation(d.TransformDirection(_localForward), d.TransformDirection(_localUp));
        }
    }
}
