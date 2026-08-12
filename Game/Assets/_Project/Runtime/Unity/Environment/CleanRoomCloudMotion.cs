using UnityEngine;

namespace CML.Unity.World
{
    /// <summary>
    /// Slow deterministic cloud translation. The visual material remains crisp;
    /// movement is applied to the geometric layer instead of blurring the skybox.
    /// </summary>
    public sealed class CleanRoomCloudMotion : MonoBehaviour
    {
        [SerializeField] private Vector3 direction = new(1f, 0f, -0.18f);
        [SerializeField, Min(0f)] private float speed = 1.8f;
        [SerializeField, Min(1f)] private float wrapDistance = 1800f;
        [SerializeField, Min(0f)] private float verticalBob = 3.5f;
        [SerializeField, Min(0f)] private float bobSpeed = 0.035f;

        private Vector3 _origin;

        private void Awake()
        {
            _origin = transform.position;
        }

        private void OnEnable()
        {
            _origin = transform.position;
        }

        private void Update()
        {
            var planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            var distance = Mathf.Repeat(Time.time * speed, wrapDistance) - wrapDistance * 0.5f;
            var bob = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * verticalBob;
            transform.position = _origin + planarDirection * distance + Vector3.up * bob;
        }
    }
}
