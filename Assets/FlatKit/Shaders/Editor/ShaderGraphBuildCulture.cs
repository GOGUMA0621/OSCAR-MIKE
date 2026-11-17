using System.Globalization;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace FlatKit {
    // Unity's fullscreen Shader Graph generator uses ToUpper() for SHADERPASS_BLIT.
    // Turkish/Azeri casing produces a non-ASCII identifier and breaks shader compilation.
    // Keep the workaround scoped to player builds, without changing regional or UI settings.
    internal sealed class ShaderGraphBuildCulture : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
        private static Thread _buildThread;
        private static CultureInfo _previousCulture;

        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report) {
            if (_previousCulture != null || "Blit".ToUpper() == "BLIT") return;
            _buildThread = Thread.CurrentThread;
            _previousCulture = _buildThread.CurrentCulture;
            _buildThread.CurrentCulture = CultureInfo.InvariantCulture;
            EditorApplication.update += RestoreAfterFailedBuild;
            try {
                // Repair cached fullscreen shaders imported under the affected culture too.
                foreach (var guid in new[] {
                    "f968e7540d632cc40b5ca8b340a3e718", // Outline
                    "c77db07224d9f784d90d7eb0e84c57f7", // Fog
                    "6b2177cf82af36949b9a8d210c2b4962"  // Pixelation
                }) {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".shadergraph", System.StringComparison.OrdinalIgnoreCase)) {
                        AssetDatabase.ImportAsset(path,
                            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    }
                }
            } catch {
                Restore();
                throw;
            }
        }

        public void OnPostprocessBuild(BuildReport report) {
            Restore();
        }

        private static void RestoreAfterFailedBuild() {
            if (!BuildPipeline.isBuildingPlayer) Restore();
        }

        private static void Restore() {
            EditorApplication.update -= RestoreAfterFailedBuild;
            if (_previousCulture == null) return;
            _buildThread.CurrentCulture = _previousCulture;
            _previousCulture = null;
            _buildThread = null;
        }
    }
}
