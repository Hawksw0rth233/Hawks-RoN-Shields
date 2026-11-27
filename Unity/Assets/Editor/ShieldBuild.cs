using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ShieldBuild
{
    const string Folder = "Assets/Shields/";

    static Texture2D Texture(string name, bool normal = false, bool linear = false)
    {
        var path = Folder + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !linear && !normal;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void Run()
    {
        foreach (var name in new[] { "Patrol", "HRT" })
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = Texture(name + "Color");
            mat.SetTexture("_BumpMap", Texture(name + "Normal", true));
            mat.EnableKeyword("_NORMALMAP");
            var mask = Texture(name + "Mask", false, true);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.SetTexture("_OcclusionMap", mask);
            var glass = new Material(mat.shader);
            glass.color = new Color(.28f, .36f, .37f, .25f);
            glass.SetFloat("_Mode", 3);
            glass.SetInt("_SrcBlend", 1);
            glass.SetInt("_DstBlend", 10);
            glass.SetInt("_ZWrite", 0);
            glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.renderQueue = 3000;
            AssetDatabase.CreateAsset(mat, Folder + name + ".mat");
            AssetDatabase.CreateAsset(glass, Folder + name + "Glass.mat");
            var root = new GameObject(name);
            using (var f = new BinaryReader(File.OpenRead(Folder + name + ".bytes")))
            {
                int parts = f.ReadInt32();
                for (int part = 0; part < parts; part++)
                {
                    bool window = f.ReadInt32() != 0;
                    int n = f.ReadInt32(),
                        count = f.ReadInt32();
                    var v = new Vector3[n];
                    var normals = new Vector3[n];
                    var uv = new Vector2[n];
                    var tri = new int[count];
                    for (int i = 0; i < n; i++)
                    {
                        v[i] = new Vector3(f.ReadSingle(), f.ReadSingle(), f.ReadSingle());
                        normals[i] = new Vector3(f.ReadSingle(), f.ReadSingle(), f.ReadSingle());
                        uv[i] = new Vector2(f.ReadSingle(), f.ReadSingle());
                    }
                    for (int i = 0; i < count; i++)
                        tri[i] = f.ReadInt32();
                    var mesh = new Mesh
                    {
                        name = name + part,
                        vertices = v,
                        normals = normals,
                        uv = uv,
                        triangles = tri,
                    };
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    AssetDatabase.CreateAsset(mesh, Folder + mesh.name + ".asset");
                    var child = new GameObject(window ? "Window" : "Body");
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = window ? glass : mat;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }
        var dest = Path.GetFullPath("../Build");
        var manifest = BuildPipeline.BuildAssetBundles(
            dest,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = "hawks.ron.shields.bundle",
                    assetNames = new[] { Folder + "Patrol.prefab", Folder + "HRT.prefab" },
                },
            },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest)
            throw new Exception("bundle build failed");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(dest, "hawks.ron.shields.bundle"));
        foreach (var name in new[] { "Patrol", "HRT" })
        {
            var p = bundle.LoadAsset<GameObject>(name);
            if (
                !p
                || !p.GetComponentsInChildren<MeshRenderer>().Any(x => x.sharedMaterial.mainTexture)
            )
                throw new Exception("missing mesh or texture " + name);
            Debug.Log("Verified shield " + name);
        }
        bundle.Unload(true);
    }
}
