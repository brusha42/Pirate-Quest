using UnityEngine;

[DisallowMultipleComponent]
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, -10f);
    [SerializeField, Min(0f)] private float smoothTime = 0.12f;
    [SerializeField] private bool preventMovingBelowStart = true;
    [SerializeField, Min(1f)] private float gameplayHalfHeight = 6f;

    private Vector3 velocity;
    private Vector3 smoothedPosition;
    private float minimumY;
    private Camera trackedCamera;
    private Rect? worldBounds;

    private void Awake()
    {
        trackedCamera = GetComponent<Camera>();
        if (trackedCamera != null && trackedCamera.orthographic)
        {
            trackedCamera.orthographicSize = gameplayHalfHeight;
        }
        smoothedPosition = transform.position;
        minimumY = transform.position.y;

        if (target != null)
        {
            SnapToTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 targetPosition = target.position + offset;
        targetPosition.z = offset.z;

        if (preventMovingBelowStart)
        {
            targetPosition.y = Mathf.Max(targetPosition.y, minimumY);
        }
        targetPosition = ClampToWorld(targetPosition);

        smoothedPosition = smoothTime <= 0f
            ? targetPosition
            : Vector3.SmoothDamp(smoothedPosition, targetPosition, ref velocity, smoothTime);

        smoothedPosition = ClampToWorld(smoothedPosition);
        transform.position = smoothedPosition;
    }

    public void SetTarget(Transform newTarget, bool snap = true)
    {
        target = newTarget;

        if (snap && target != null)
        {
            SnapToTarget();
        }
    }

    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        smoothedPosition = ClampToWorld(target.position + offset);
        transform.position = smoothedPosition;
        velocity = Vector3.zero;
        minimumY = transform.position.y;
    }

    public void SetWorldBounds(Rect bounds)
    {
        worldBounds = bounds;
        preventMovingBelowStart = false;
        offset.y = 1.35f;
        if (target != null) SnapToTarget();
    }

    private Vector3 ClampToWorld(Vector3 position)
    {
        if (!worldBounds.HasValue || trackedCamera == null || !trackedCamera.orthographic) return position;
        Rect bounds = worldBounds.Value;
        float halfHeight = trackedCamera.orthographicSize;
        float halfWidth = halfHeight * trackedCamera.aspect;
        position.x = bounds.width > halfWidth * 2f ? Mathf.Clamp(position.x, bounds.xMin + halfWidth, bounds.xMax - halfWidth) : bounds.center.x;
        position.y = bounds.height > halfHeight * 2f ? Mathf.Clamp(position.y, bounds.yMin + halfHeight, bounds.yMax - halfHeight) : bounds.center.y;
        return position;
    }

}
