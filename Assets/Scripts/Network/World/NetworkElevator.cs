using System.Collections.Generic;
using OskarMike.Network.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OskarMike.Network.World
{
    /// <summary>
    /// 서버가 이동을 결정하는 2층 엘리베이터.
    /// 플레이어를 자식으로 만들지 않고 탑승 영역 안의 CharacterController에
    /// 엘리베이터의 프레임 이동량을 더한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class NetworkElevator : NetworkBehaviour
    {
        [Header("Floor Offsets (Local Space)")]
        [Tooltip("씬에 배치한 위치를 기준으로 한 시작 층 오프셋")]
        [SerializeField] private Vector3 startFloorOffset = Vector3.zero;
        [Tooltip("씬에 배치한 위치를 기준으로 한 끝 층 오프셋")]
        [SerializeField] private Vector3 endFloorOffset = new Vector3(0f, 10f, 0f);

        [Header("Movement")]
        [Min(0.01f)]
        [SerializeField] private float moveSpeed = 2.5f;
        [Min(0f)]
        [SerializeField] private float waitAtFloor = 1f;
        [SerializeField] private bool startAtEndFloor;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool loop = true;

        [Header("Passenger Area (Local Space)")]
        [Tooltip("캐릭터를 함께 운반할 박스 영역의 중심")]
        [SerializeField] private Vector3 passengerAreaCenter = new Vector3(0f, 1.25f, 0f);
        [Tooltip("캐릭터를 함께 운반할 박스 영역의 크기")]
        [SerializeField] private Vector3 passengerAreaSize = new Vector3(3f, 2.5f, 3f);
        [SerializeField] private LayerMask passengerLayers = ~0;

        private readonly Collider[] passengerHits = new Collider[16];
        private readonly HashSet<PlayerNetworkController> movedPassengers = new();

        private Vector3 placementPosition;
        private Quaternion placementRotation;
        private bool movingToEnd;
        private bool isMoving;
        private float waitTimer;

        public Vector3 StartFloorPosition => placementPosition + placementRotation * startFloorOffset;
        public Vector3 EndFloorPosition => placementPosition + placementRotation * endFloorOffset;
        public bool IsMoving => isMoving;
        public bool IsAtEndFloor => Vector3.SqrMagnitude(transform.position - EndFloorPosition) < 0.0001f;

        private void Awake()
        {
            placementPosition = transform.position;
            placementRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsServer)
                return;

            movingToEnd = !startAtEndFloor;
            transform.position = startAtEndFloor ? EndFloorPosition : StartFloorPosition;
            isMoving = autoStart;
            waitTimer = 0f;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !isMoving)
                return;

            if (waitTimer > 0f)
            {
                waitTimer -= Time.deltaTime;
                return;
            }

            Vector3 target = movingToEnd ? EndFloorPosition : StartFloorPosition;
            Vector3 previousPosition = transform.position;
            Vector3 nextPosition = Vector3.MoveTowards(previousPosition, target, moveSpeed * Time.deltaTime);
            Vector3 platformDelta = nextPosition - previousPosition;

            transform.position = nextPosition;
            CarryPassengers(platformDelta);

            if (nextPosition != target)
                return;

            if (!loop)
            {
                isMoving = false;
                return;
            }

            movingToEnd = !movingToEnd;
            waitTimer = waitAtFloor;
        }

        /// <summary>서버에서 엘리베이터를 시작 층으로 보낸다.</summary>
        public void MoveToStartFloor()
        {
            SetDestination(false);
        }

        /// <summary>서버에서 엘리베이터를 끝 층으로 보낸다.</summary>
        public void MoveToEndFloor()
        {
            SetDestination(true);
        }

        /// <summary>서버에서 현재 목적지의 반대편 층으로 보낸다.</summary>
        public void MoveToOtherFloor()
        {
            if (!IsServer)
                return;

            float startDistance = Vector3.SqrMagnitude(transform.position - StartFloorPosition);
            float endDistance = Vector3.SqrMagnitude(transform.position - EndFloorPosition);
            SetDestination(startDistance <= endDistance);
        }

        /// <summary>서버에서 이동을 일시 정지하거나 재개한다.</summary>
        public void SetPaused(bool paused)
        {
            if (!IsServer)
                return;

            isMoving = !paused;
        }

        private void SetDestination(bool toEndFloor)
        {
            if (!IsServer)
                return;

            movingToEnd = toEndFloor;
            waitTimer = 0f;
            isMoving = true;
        }

        private void CarryPassengers(Vector3 platformDelta)
        {
            if (platformDelta.sqrMagnitude <= Mathf.Epsilon)
                return;

            Vector3 scale = transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(
                passengerAreaSize * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = transform.TransformPoint(passengerAreaCenter);

            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                passengerHits,
                transform.rotation,
                passengerLayers,
                QueryTriggerInteraction.Ignore);

            movedPassengers.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                PlayerNetworkController player =
                    passengerHits[i].GetComponentInParent<PlayerNetworkController>();

                if (player == null || !movedPassengers.Add(player))
                    continue;

                player.ApplyServerPlatformMotion(platformDelta);
            }
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.01f, moveSpeed);
            waitAtFloor = Mathf.Max(0f, waitAtFloor);
            passengerAreaSize.x = Mathf.Max(0.01f, passengerAreaSize.x);
            passengerAreaSize.y = Mathf.Max(0.01f, passengerAreaSize.y);
            passengerAreaSize.z = Mathf.Max(0.01f, passengerAreaSize.z);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 basePosition = Application.isPlaying ? placementPosition : transform.position;
            Quaternion baseRotation = Application.isPlaying ? placementRotation : transform.rotation;
            Vector3 start = basePosition + baseRotation * startFloorOffset;
            Vector3 end = basePosition + baseRotation * endFloorOffset;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(start, 0.2f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(end, 0.2f);
            Gizmos.DrawLine(start, end);

            Gizmos.color = new Color(1f, 0.75f, 0f, 1f);
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawWireCube(passengerAreaCenter, passengerAreaSize);
            Gizmos.matrix = oldMatrix;
        }
    }
}
