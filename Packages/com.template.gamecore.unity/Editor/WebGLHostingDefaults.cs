using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GameCore.Unity.Editor
{
    /// <summary>
    /// Makes every WebGL build load on any static host (itch.io, GitHub Pages, a plain file server)
    /// without server-side Content-Encoding headers: Gzip plus the JS decompression fallback.
    /// Brotli and headerless Gzip both fail to start on hosts that do not send those headers.
    /// </summary>
    public sealed class WebGLHostingDefaults : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
        }
    }
}
