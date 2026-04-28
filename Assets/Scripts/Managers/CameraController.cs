using UnityEngine;

/// <summary>
/// Simple camera controller for navigating the 3D visualization.
///
/// Controls:
///   Arrow keys — move forward/back/left/right
///   Q / E      — move down / up
///   Scroll     — zoom in / out (move forward/back)
///   Mouse      — look around (always active, cursor confined)
///
/// Also supports smooth auto-transitions via FocusOnRegion().
/// During a transition, user input is suppressed until the camera
/// reaches its target. After arriving, control is fully restored.
///
/// Attach to the Main Camera.
/// </summary>
public class CameraController : MonoBehaviour
{
    // -----------------------------------------------------------
    // Inspector settings
    // -----------------------------------------------------------

    [Header("Movement")]
    [Tooltip("Base movement speed (units per second).")]
    public float moveSpeed = 10f;

    [Tooltip("Movement speed multiplier when holding Shift.")]
    public float sprintMultiplier = 3f;

    [Header("Look / Orbit")]
    [Tooltip("Mouse sensitivity for looking around.")]
    public float lookSensitivity = 2f;

    [Header("Zoom")]
    [Tooltip("Zoom speed (scroll wheel).")]
    public float zoomSpeed = 20f;

    [Header("Auto-Transition")]
    [Tooltip("Smooth time for camera position transition (seconds).")]
    public float transitionSmoothTime = 0.8f;

    [Tooltip("Speed of rotation interpolation during transition (0–1 per frame, higher = faster).")]
    [Range(0.01f, 0.3f)]
    public float transitionRotationSpeed = 0.08f;

    [Tooltip("Distance padding multiplier for framing (>1 = more breathing room).")]
    public float framingPadding = 1.5f;

    [Tooltip("Minimum camera distance to prevent clipping inside small clusters.")]
    public float minFocusDistance = 1.5f;

    // -----------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------

    private float rotationX = 0f;
    private float rotationY = 0f;

    // --- Transition state ---
    private bool isTransitioning = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 transitionVelocity = Vector3.zero;
    private float transitionElapsed = 0f;
    private const float MaxTransitionTime = 5f; // Safety timeout

    void Start()
    {
        // Initialize rotation from current camera orientation
        Vector3 currentEuler = transform.eulerAngles;
        rotationX = currentEuler.y;
        rotationY = currentEuler.x;

        // Normalize Y rotation to avoid sudden jump
        if (rotationY > 180f)
            rotationY -= 360f;

        // Keep cursor visible and confined to game window for click interaction
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    void Update()
    {
        // During a transition, only run the smooth move — suppress all user input
        if (isTransitioning)
        {
            HandleTransition();
            return;
        }

        HandleMovement();
        HandleZoom();
        HandleLook();
    }

    // -----------------------------------------------------------
    // Movement (Arrow keys + Q/E)
    // -----------------------------------------------------------

    private void HandleMovement()
    {
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            speed *= sprintMultiplier;

        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.UpArrow)) move += transform.forward;
        if (Input.GetKey(KeyCode.DownArrow)) move -= transform.forward;
        if (Input.GetKey(KeyCode.LeftArrow)) move -= transform.right;
        if (Input.GetKey(KeyCode.RightArrow)) move += transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

        if (move.sqrMagnitude > 0)
        {
            transform.position += move.normalized * speed * Time.deltaTime;
        }
    }

    // -----------------------------------------------------------
    // Zoom (scroll wheel)
    // -----------------------------------------------------------

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            transform.position += transform.forward * scroll * zoomSpeed;
        }
    }

    // -----------------------------------------------------------
    // Look (mouse free-look, always active)
    // -----------------------------------------------------------

    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        rotationX += mouseX;
        rotationY -= mouseY;
        rotationY = Mathf.Clamp(rotationY, -89f, 89f);

        transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
    }

    // -----------------------------------------------------------
    // Auto-transition (smooth focus on expand)
    // -----------------------------------------------------------

    /// <summary>
    /// Initiates a smooth camera transition to frame a region defined by
    /// a centroid point and a maximum extent (radius from centroid to farthest child).
    /// The camera preserves its current viewing direction and moves to a distance
    /// where the entire region fits comfortably in view.
    /// Called by VisualizationManager after expanding a node.
    /// </summary>
    public void FocusOnRegion(Vector3 centroid, float extent)
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        // Compute required distance to frame the extent using FOV-based frustum math
        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float requiredDistance = extent / Mathf.Tan(fovRad / 2f);
        requiredDistance *= framingPadding;
        requiredDistance = Mathf.Max(requiredDistance, minFocusDistance);

        // Compute view direction from current camera position toward centroid.
        // Guard: if camera is already at or very near the centroid, use the
        // camera's current forward direction instead to avoid a zero-length vector.
        Vector3 toCentroid = centroid - transform.position;
        Vector3 viewDirection;
        if (toCentroid.sqrMagnitude < 0.001f)
            viewDirection = transform.forward;
        else
            viewDirection = toCentroid.normalized;

        // Target position: place camera at requiredDistance along the backward direction
        targetPosition = centroid - viewDirection * requiredDistance;

        // Target rotation: look directly at the centroid
        targetRotation = Quaternion.LookRotation(viewDirection);

        // Reset velocity and timer for the new transition
        transitionVelocity = Vector3.zero;
        transitionElapsed = 0f;
        isTransitioning = true;

        Debug.Log($"[CameraController] 🎥 Starting transition to frame region " +
                  $"(centroid={centroid}, extent={extent:F2}, distance={requiredDistance:F2}).");
    }

    /// <summary>
    /// Runs each frame during an active transition.
    /// Smoothly moves position via SmoothDamp and rotation via Slerp.
    /// When close enough to target, snaps to final pose and restores user control.
    /// </summary>
    private void HandleTransition()
    {
        transitionElapsed += Time.deltaTime;

        // Safety timeout: if transition takes too long, snap to target
        if (transitionElapsed >= MaxTransitionTime)
        {
            FinishTransition();
            Debug.Log("[CameraController] ⚠️ Transition timed out — snapped to target.");
            return;
        }

        // Smooth position
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref transitionVelocity,
            transitionSmoothTime);

        // Smooth rotation
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            transitionRotationSpeed);

        // Check if close enough to snap and finish
        float positionError = Vector3.Distance(transform.position, targetPosition);
        float rotationError = Quaternion.Angle(transform.rotation, targetRotation);

        if (positionError < 0.01f && rotationError < 0.5f)
        {
            FinishTransition();
            Debug.Log("[CameraController] ✅ Transition complete — user control restored.");
        }
    }

    /// <summary>
    /// Cancels an in-progress camera transition immediately.
    /// The camera stays at its current position/rotation and user control resumes.
    /// Safe to call when no transition is active (no-op).
    /// Called by VisualizationManager on collapse (Escape).
    /// </summary>
    public void CancelTransition()
    {
        if (!isTransitioning) return;

        FinishTransition();
        Debug.Log("[CameraController] ❌ Transition cancelled — user control restored.");
    }

    /// <summary>
    /// Shared completion logic: snaps to current pose, syncs rotation
    /// variables, and sets isTransitioning = false.
    /// </summary>
    private void FinishTransition()
    {
        // Sync internal rotation variables from the current rotation
        // so there is no snap when user resumes mouse control
        Vector3 finalEuler = transform.eulerAngles;
        rotationX = finalEuler.y;
        rotationY = finalEuler.x;
        if (rotationY > 180f)
            rotationY -= 360f;

        isTransitioning = false;
    }
}
