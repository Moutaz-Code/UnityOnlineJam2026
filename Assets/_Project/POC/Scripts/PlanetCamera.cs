using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumber.POC
{
    /// <summary>
    /// Follow camera for a polyhedral planet. Keeps a forward vector tangent to the surface and
    /// re-projects it onto the player's tangent plane every frame (a cheap parallel transport), so
    /// crossing an edge never flips the view. While the die rolls it pulls out to a wide shot.
    /// Hold right mouse (or Q/E) to orbit.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class PlanetCamera : MonoBehaviour
    {
        public PlanetWalker target;
        public DiePlanet die;

        [Header("Follow")]
        public float distance = 9f;
        public float height = 5f;
        public float lookHeight = 1f;
        public float orbitSpeed = 0.2f;     // degrees per pixel of mouse delta
        public float keyOrbitSpeed = 120f;  // degrees per second
        public float positionSharpness = 8f;
        public float rotationSharpness = 10f;

        [Header("Roll shot")]
        public Vector3 rollViewDirection = new Vector3(0.35f, 0.55f, -1f);
        public float rollViewDistance = 42f;

        public Vector3 PlanarForward { get; private set; } = Vector3.forward;

        Vector3 _up = Vector3.up;

        void Start()
        {
            if (target != null)
            {
                _up = target.transform.up;
                PlanarForward = target.transform.forward;
                transform.position = DesiredFollowPosition();
                transform.rotation = Quaternion.LookRotation(target.transform.position + _up * lookHeight - transform.position, _up);
            }
        }

        void LateUpdate()
        {
            if (target == null || die == null) return;
            float dt = Time.deltaTime;

            // Orbit input
            float yaw = 0f;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null && mouse.rightButton.isPressed) yaw += mouse.delta.ReadValue().x * orbitSpeed;
            if (kb != null)
            {
                if (kb.qKey.isPressed) yaw -= keyOrbitSpeed * dt;
                if (kb.eKey.isPressed) yaw += keyOrbitSpeed * dt;
            }

            Vector3 desiredPos, lookAt;
            bool rolling = die.State != DiePlanet.RollState.Idle;

            _up = Vector3.Slerp(_up, rolling ? Vector3.up : target.transform.up, 1f - Mathf.Exp(-rotationSharpness * dt));

            // Parallel transport the planar forward onto the current tangent plane.
            Vector3 fwd = Vector3.ProjectOnPlane(PlanarForward, target.transform.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = target.transform.forward;
            fwd = Quaternion.AngleAxis(yaw, target.transform.up) * fwd.normalized;
            PlanarForward = fwd;

            if (rolling)
            {
                desiredPos = die.transform.position + rollViewDirection.normalized * rollViewDistance;
                lookAt = die.transform.position;
            }
            else
            {
                desiredPos = DesiredFollowPosition();
                lookAt = target.transform.position + target.transform.up * lookHeight;
            }

            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-positionSharpness * dt));
            Quaternion want = Quaternion.LookRotation(lookAt - transform.position, _up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-rotationSharpness * dt));
        }

        Vector3 DesiredFollowPosition() =>
            target.transform.position + target.transform.up * height - PlanarForward * distance;
    }
}
