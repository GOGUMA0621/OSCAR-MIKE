using UnityEngine;

namespace OskarMike.MapGeneration
{
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private MinimapDiscoveryController discovery;
        [SerializeField] private MinimapGraphic mapGraphic;
        [SerializeField] private RectTransform playerMarker;

        private void Start()
        {
            if (discovery == null)
                discovery = FindFirstObjectByType<MinimapDiscoveryController>();
            if (mapGraphic == null)
                mapGraphic = GetComponentInChildren<MinimapGraphic>(true);
            if (playerMarker == null && mapGraphic != null)
            {
                MinimapPlayerGraphic markerGraphic =
                    mapGraphic.GetComponentInChildren<MinimapPlayerGraphic>(true);
                if (markerGraphic != null)
                    playerMarker = markerGraphic.rectTransform;
            }

            if (discovery != null && mapGraphic != null && playerMarker != null)
                mapGraphic.Initialize(discovery);
        }

        public void Initialize(MinimapDiscoveryController source, MinimapGraphic graphic, RectTransform marker)
        {
            discovery = source;
            mapGraphic = graphic;
            playerMarker = marker;
            mapGraphic.Initialize(discovery);
        }

        private void LateUpdate()
        {
            if (discovery == null || mapGraphic == null || playerMarker == null)
                return;

            Transform target = discovery.TrackedTarget;
            playerMarker.gameObject.SetActive(target != null);
            if (target == null)
                return;

            playerMarker.anchoredPosition = mapGraphic.WorldToLocal(target.position);
            playerMarker.localRotation = Quaternion.Euler(0f, 0f, -target.eulerAngles.y);
        }
    }
}
