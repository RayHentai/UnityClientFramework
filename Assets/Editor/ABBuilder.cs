using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AB 包打包脚本(测试用)。放在 Assets/Editor/ 下。
/// 菜单:Tools/AB/Build (PC)
/// 先打到 工程根目录/AssetBundles/PC,再把包文件拷进 Assets/StreamingAssets。
/// 主包的文件名 = 输出文件夹名,所以输出文件夹叫 PC,主包就叫 PC,和 ABManager 的 MainBundleName 对上。
/// </summary>
public static class ABBuilder
{
    private const string OutputDirectory = "AssetBundles/PC";

    [MenuItem("Tools/AB/Build (PC)")]
    public static void BuildPC()
    {
        Directory.CreateDirectory(OutputDirectory);
        BuildPipeline.BuildAssetBundles(OutputDirectory, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

        string targetDirectory = Application.streamingAssetsPath;
        Directory.CreateDirectory(targetDirectory);
        foreach (string file in Directory.GetFiles(OutputDirectory))
        {
            // .manifest 是给人看的文本文件,运行时用不到,不拷
            if (file.EndsWith(".manifest"))
                continue;
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), true);
        }

        AssetDatabase.Refresh();
        Debug.Log($"AB 打包完成,已拷进 {targetDirectory}");
    }
}