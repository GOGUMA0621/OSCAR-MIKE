using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OskarMike.MapGeneration
{
    public static class ProceduralMinimapBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInProceduralTest()
        {
            if (SceneManager.GetActiveScene().name != "ProceduralTest"
                || Object.FindFirstObjectByType<MinimapView>() != null)
                return;

            ProceduralMapGenerator generator = Object.FindFirstObjectByType<ProceduralMapGenerator>();
            if (generator == null)
                return;

            MinimapDiscoveryController discovery = generator.GetComponent<MinimapDiscoveryController>();
            if (discovery == null)
                discovery = generator.gameObject.AddComponent<MinimapDiscoveryController>();

            GameObject canvasObject = new("ProceduralMinimapCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new("MinimapPanel", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(1f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-28f, -28f);
            panel.sizeDelta = new Vector2(280f, 280f);

            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.025f, 0.04f, 0.055f, 0.88f);
            background.raycastTarget = false;

            GameObject mapObject = new("DiscoveredMap", typeof(RectTransform), typeof(MinimapGraphic));
            mapObject.transform.SetParent(panel, false);
            RectTransform mapRect = mapObject.GetComponent<RectTransform>();
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = new Vector2(12f, 12f);
            mapRect.offsetMax = new Vector2(-12f, -12f);
            MinimapGraphic graphic = mapObject.GetComponent<MinimapGraphic>();
            graphic.raycastTarget = false;

            GameObject markerObject = new("LocalPlayerMarker", typeof(RectTransform), typeof(MinimapPlayerGraphic));
            markerObject.transform.SetParent(mapRect, false);
            RectTransform marker = markerObject.GetComponent<RectTransform>();
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(13f, 18f);
            MinimapPlayerGraphic markerGraphic = markerObject.GetComponent<MinimapPlayerGraphic>();
            markerGraphic.color = new Color(1f, 0.84f, 0.22f, 1f);
            markerGraphic.raycastTarget = false;

            MinimapView view = panelObject.AddComponent<MinimapView>();
            view.Initialize(discovery, graphic, marker);
        }
    }
}
