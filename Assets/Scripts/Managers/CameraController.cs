using UnityEngine;

public class CameraController : MonoBehaviour
{

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

    private float rotationX = 0f;
    private float rotationY = 0f;


    private bool isTransitioning = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 transitionVelocity = Vector3.zero;
    private float transitionElapsed = 0f;
    private const float MaxTransitionTime = 5f;

    void Start()
    {

        Vector3 currentEuler = transform.eulerAngles;
        rotationX = currentEuler.y;
        rotationY = currentEuler.x;


        if (rotationY > 180f)
            rotationY -= 360f;


        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    void Update()
    {

        if (isTransitioning)
        {
            HandleTransition();
            return;
        }

        HandleMovement();
        HandleZoom();
        HandleLook();
    }

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

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            transform.position += transform.forward * scroll * zoomSpeed;
        }
    }

    private void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        rotationX += mouseX;
        rotationY -= mouseY;
        rotationY = Mathf.Clamp(rotationY, -89f, 89f);

        transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
    }

    public void FocusOnRegion(Vector3 centroid, float extent)
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;


        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float requiredDistance = extent / Mathf.Tan(fovRad / 2f);
        requiredDistance *= framingPadding;
        requiredDistance = Mathf.Max(requiredDistance, minFocusDistance);

        Vector3 toCentroid = centroid - transform.position;
        Vector3 viewDirection;
        if (toCentroid.sqrMagnitude < 0.001f)
            viewDirection = transform.forward;
        else
            viewDirection = toCentroid.normalized;


        targetPosition = centroid - viewDirection * requiredDistance;


        targetRotation = Quaternion.LookRotation(viewDirection);


        transitionVelocity = Vector3.zero;
        transitionElapsed = 0f;
        isTransitioning = true;

        Debug.Log($"[CameraController] 🎥 Starting transition to frame region " +
                  $"(centroid={centroid}, extent={extent:F2}, distance={requiredDistance:F2}).");
    }

    private void HandleTransition()
    {
        transitionElapsed += Time.deltaTime;


        if (transitionElapsed >= MaxTransitionTime)
        {
            FinishTransition();
            Debug.Log("[CameraController] ⚠️ Transition timed out — snapped to target.");
            return;
        }


        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref transitionVelocity,
            transitionSmoothTime);


        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            transitionRotationSpeed);


        float positionError = Vector3.Distance(transform.position, targetPosition);
        float rotationError = Quaternion.Angle(transform.rotation, targetRotation);

        if (positionError < 0.01f && rotationError < 0.5f)
        {
            FinishTransition();
            Debug.Log("[CameraController] ✅ Transition complete — user control restored.");
        }
    }

    public void CancelTransition()
    {
        if (!isTransitioning) return;

        FinishTransition();
        Debug.Log("[CameraController] ❌ Transition cancelled — user control restored.");
    }

    private void FinishTransition()
    {


        Vector3 finalEuler = transform.eulerAngles;
        rotationX = finalEuler.y;
        rotationY = finalEuler.x;
        if (rotationY > 180f)
            rotationY -= 360f;

        isTransitioning = false;
    }
}
