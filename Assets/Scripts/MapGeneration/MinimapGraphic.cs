using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.MapGeneration
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapGraphic : MaskableGraphic
    {
        [SerializeField] private Color roomColor = new(0.24f, 0.82f, 0.72f, 0.95f);
        [SerializeField] private Color corridorColor = new(0.18f, 0.57f, 0.55f, 0.9f);
        [SerializeField, Min(0f)] private float worldPadding = 3f;
        [SerializeField, Min(0.01f)] private float fitSmoothTime = 0.18f;

        private MinimapDiscoveryController discovery;
        private Vector2 currentCenter;
        private Vector2 currentSize = Vector2.one;
        private Vector2 centerVelocity;
        private Vector2 sizeVelocity;
        private bool hasFit;

        public void Initialize(MinimapDiscoveryController source)
        {
            if (discovery != null)
                discovery.DiscoveryChanged -= HandleDiscoveryChanged;

            discovery = source;
            if (discovery != null)
                discovery.DiscoveryChanged += HandleDiscoveryChanged;
            SnapToRevealedBounds();
            SetVerticesDirty();
        }

        protected override void OnDestroy()
        {
            if (discovery != null)
                discovery.DiscoveryChanged -= HandleDiscoveryChanged;
            base.OnDestroy();
        }

        private void Update()
        {
            if (discovery == null || !TryGetRevealedBounds(out Bounds bounds))
                return;

            Vector2 targetCenter = new(bounds.center.x, bounds.center.z);
            Vector2 targetSize = new(
                Mathf.Max(1f, bounds.size.x + worldPadding * 2f),
                Mathf.Max(1f, bounds.size.z + worldPadding * 2f));
            Vector2 oldCenter = currentCenter;
            Vector2 oldSize = currentSize;
            currentCenter = Vector2.SmoothDamp(currentCenter, targetCenter, ref centerVelocity, fitSmoothTime);
            currentSize = Vector2.SmoothDamp(currentSize, targetSize, ref sizeVelocity, fitSmoothTime);

            if ((currentCenter - oldCenter).sqrMagnitude > 0.000001f
                || (currentSize - oldSize).sqrMagnitude > 0.000001f)
                SetVerticesDirty();
        }

        public Vector2 WorldToLocal(Vector3 worldPosition)
        {
            Rect rect = rectTransform.rect;
            float scale = GetScale(rect);
            Vector2 delta = new(worldPosition.x - currentCenter.x, worldPosition.z - currentCenter.y);
            return rect.center + delta * scale;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (discovery == null || discovery.MapGenerator == null || !hasFit)
                return;

            IReadOnlyList<ProceduralMapGenerator.CorridorData> corridors = discovery.MapGenerator.Corridors;
            for (int i = 0; i < corridors.Count; i++)
            {
                if (!discovery.IsCorridorRevealed(i))
                    continue;

                ProceduralMapGenerator.CorridorData corridor = corridors[i];
                AddCorridorStrip(vh, corridor, corridorColor);
            }

            IReadOnlyList<ProceduralMapGenerator.PlacedRoomData> rooms = discovery.MapGenerator.PlacedRooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (!discovery.IsRoomRevealed(i))
                    continue;

                Bounds bounds = discovery.MapGenerator.GetRoomBounds(i);
                AddRect(vh, bounds, roomColor);
            }
        }

        private void AddRect(VertexHelper vh, Bounds bounds, Color32 tint)
        {
            Vector2 min = WorldToLocal(new Vector3(bounds.min.x, 0f, bounds.min.z));
            Vector2 max = WorldToLocal(new Vector3(bounds.max.x, 0f, bounds.max.z));
            AddQuad(vh,
                new Vector2(min.x, min.y), new Vector2(min.x, max.y),
                new Vector2(max.x, max.y), new Vector2(max.x, min.y), tint);
        }

        private void AddCorridorStrip(VertexHelper vh,
            ProceduralMapGenerator.CorridorData corridor, Color32 tint)
        {
            ProceduralMapGenerator.BuildCorridorOutline(
                corridor, corridor.width * 0.5f, out List<Vector3> left, out List<Vector3> right);
            if (left.Count < 2 || right.Count != left.Count)
                return;

            for (int i = 0; i < left.Count - 1; i++)
            {
                if (Vector3.Distance(left[i], left[i + 1]) < 0.01f
                    || Vector3.Distance(right[i], right[i + 1]) < 0.01f)
                    continue;

                AddQuad(vh,
                    WorldToLocal(left[i]),
                    WorldToLocal(right[i]),
                    WorldToLocal(right[i + 1]),
                    WorldToLocal(left[i + 1]),
                    tint);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.up);
            vh.AddVert(c, tint, Vector2.one);
            vh.AddVert(d, tint, Vector2.right);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private float GetScale(Rect rect)
        {
            return Mathf.Min(rect.width / Mathf.Max(0.01f, currentSize.x),
                rect.height / Mathf.Max(0.01f, currentSize.y));
        }

        private void HandleDiscoveryChanged()
        {
            if (!hasFit)
                SnapToRevealedBounds();
            SetVerticesDirty();
        }

        private void SnapToRevealedBounds()
        {
            if (!TryGetRevealedBounds(out Bounds bounds))
                return;

            currentCenter = new Vector2(bounds.center.x, bounds.center.z);
            currentSize = new Vector2(
                Mathf.Max(1f, bounds.size.x + worldPadding * 2f),
                Mathf.Max(1f, bounds.size.z + worldPadding * 2f));
            hasFit = true;
        }

        private bool TryGetRevealedBounds(out Bounds result)
        {
            result = default;
            if (discovery == null || discovery.MapGenerator == null)
                return false;

            bool found = false;
            foreach (int roomIndex in discovery.RevealedRooms)
            {
                Bounds room = discovery.MapGenerator.GetRoomBounds(roomIndex);
                if (!found)
                {
                    result = room;
                    found = true;
                }
                else
                    result.Encapsulate(room);
            }

            foreach (int corridorIndex in discovery.RevealedCorridors)
            {
                ProceduralMapGenerator.CorridorData corridor = discovery.MapGenerator.Corridors[corridorIndex];
                float halfWidth = corridor.width * 0.5f;
                EncapsulatePoint(ref result, ref found, corridor.start, halfWidth);
                EncapsulatePoint(ref result, ref found, corridor.extendStart, halfWidth);
                EncapsulatePoint(ref result, ref found, corridor.corner, halfWidth);
                EncapsulatePoint(ref result, ref found, corridor.extendEnd, halfWidth);
                EncapsulatePoint(ref result, ref found, corridor.end, halfWidth);
            }

            return found;
        }

        private static void EncapsulatePoint(ref Bounds bounds, ref bool found, Vector3 point, float radius)
        {
            var pointBounds = new Bounds(point, new Vector3(radius * 2f, 0f, radius * 2f));
            if (!found)
            {
                bounds = pointBounds;
                found = true;
            }
            else
                bounds.Encapsulate(pointBounds);
        }
    }
}
