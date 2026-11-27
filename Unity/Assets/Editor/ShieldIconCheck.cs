using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ShieldIconCheck
{
    public static void Run()
    {
        var bundle = AssetBundle.LoadFromFile(
            Path.GetFullPath("../Build/hawks.ron.shields.bundle")
        );
        foreach (var name in new[] { "Patrol", "HRT" })
        {
            var obj = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(name));
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -2);
            cam.orthographic = true;
            cam.orthographicSize = .57f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.gray;
            obj.transform.rotation = Quaternion.LookRotation(
                -cam.transform.forward,
                cam.transform.up
            );
            var meshes = obj.GetComponentsInChildren<MeshRenderer>();
            if (meshes.Length != 3)
                throw new Exception("Unexpected preview parts");
            var bounds = meshes[0].bounds;
            foreach (var part in meshes)
                bounds.Encapsulate(part.bounds);
            obj.transform.position -= bounds.center;
            var lamp = new GameObject("Light").AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.transform.rotation = Quaternion.Euler(20, -15, 0);
            lamp.intensity = 1.4f;
            var rt = new RenderTexture(240, 400, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(240, 400);
            tex.ReadPixels(new Rect(0, 0, 240, 400), 0, 0);
            tex.Apply();
            File.WriteAllBytes(
                Path.GetFullPath("../Build/" + name + "-inspection-check.png"),
                tex.EncodeToPNG()
            );
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(lamp.gameObject);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(obj);
        }
        bundle.Unload(true);
    }
}
