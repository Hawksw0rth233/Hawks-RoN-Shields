using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class VantBuild
{
    const string Folder = "Assets/Vant/";

    static Texture2D Map(string family, string suffix, bool normal = false, bool linear = false)
    {
        string path = Folder + "Unity/M_Vant_" + family + "_" + suffix + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = !normal && !linear;
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void Run()
    {
        var names = new[] { "VantLMC", "VantLMO", "VantVM" };
        foreach (var family in new[] { "LM", "VM" })
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = Map(family, "AlbedoTransparency");
            mat.SetTexture("_MetallicGlossMap", Map(family, "MetallicSmoothness", false, true));
            mat.SetTexture("_BumpMap", Map(family, "Normal", true));
            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.EnableKeyword("_NORMALMAP");
            AssetDatabase.CreateAsset(mat, Folder + family + ".mat");
            var glass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shields/HRTGlass.mat");
            foreach (var name in names)
                if (name.StartsWith("Vant" + family))
                    ShieldBuild.MakePrefab(Folder, name, mat, glass, "VantViewport");
        }
        string dest = Path.GetFullPath("../Build/Vant");
        Directory.CreateDirectory(dest);
        var keys = Array.ConvertAll(names, n => Folder + n + ".prefab");
        if (
            !BuildPipeline.BuildAssetBundles(
                dest,
                new[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = "hawks.ron.vant.bundle",
                        assetNames = keys,
                    },
                },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64
            )
        )
            throw new Exception("Vant bundle failed");
        var bank = AssetBundle.LoadFromFile(Path.Combine(dest, "hawks.ron.vant.bundle"));
        foreach (var name in names)
        {
            var root = bank.LoadAsset<GameObject>(name);
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (
                renderers.Length != (name == "VantVM" ? 2 : 1)
                || !renderers[0].sharedMaterial.mainTexture
            )
                throw new Exception("Incomplete " + name);
        }
        bank.Unload(true);
    }
}
