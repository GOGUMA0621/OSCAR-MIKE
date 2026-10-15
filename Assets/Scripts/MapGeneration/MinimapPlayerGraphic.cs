using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.MapGeneration
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapPlayerGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            Color32 tint = color;
            vh.AddVert(new Vector2(rect.center.x, rect.yMax), tint, Vector2.up);
            vh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
            vh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.right);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
