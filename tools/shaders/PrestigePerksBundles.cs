using System.IO;
using UnityEditor;
using UnityEngine;

// 배치 모드: Unity.exe -batchmode -quit -nographics -projectPath <셰이더 프로젝트> -executeMethod PrestigePerksBundles.Build -logFile build.log
// Assets/PrestigePerksShaders 의 셰이더 두 개를 prestigeperks_shaders.bundle 하나로 묶는다 (Windows, DX11). 사용법은 같은 폴더 README.md
public static class PrestigePerksBundles
{
    public static void Build()
    {
        const string outDir = "Build";
        Directory.CreateDirectory(outDir);
        var build = new AssetBundleBuild
        {
            assetBundleName = "prestigeperks_shaders.bundle",
            assetNames = new[] { "Assets/PrestigePerksShaders/SonarEcho.shader", "Assets/PrestigePerksShaders/SonarRing.shader" },
        };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log(manifest == null ? "PRESTIGEPERKS BUNDLE BUILD FAILED" : "PRESTIGEPERKS BUNDLE BUILD OK");
        if (manifest == null)
        {
            EditorApplication.Exit(1);
        }
    }
}
