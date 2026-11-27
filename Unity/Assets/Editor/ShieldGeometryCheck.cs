using System;
using UnityEditor;
using UnityEngine;

public static class ShieldGeometryCheck
{
    public static void Run()
    {
        foreach (string name in new[] { "Patrol", "HRT" })
        {
            var obj = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Shields/" + name + ".prefab")
            );
            foreach (var part in obj.GetComponentsInChildren<MeshFilter>())
                part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.sharedMesh;
            foreach (int side in new[] { -1, 1 })
            {
                obj.transform.rotation = Quaternion.Euler(0, side == 1 ? 0 : 180, 0);
                Physics.SyncTransforms();
                foreach (
                    var pt in new[]
                    {
                        new Vector2(0, .25f),
                        new Vector2(0, -.24f),
                        new Vector2(.8f, 0),
                        new Vector2(0, -1f),
                    }
                )
                {
                    bool hit = Physics.Raycast(
                        new Vector3(pt.x, pt.y, -2),
                        Vector3.forward,
                        out var info,
                        4
                    );
                    string result = hit ? info.collider.name : "open";
                    string expected =
                        pt.x != 0 || pt.y < -.5f ? "open"
                        : pt.y > 0 ? "Window"
                        : "Body";
                    Debug.Log(name + " side " + side + " at " + pt + " = " + result);
                    if (result != expected)
                        throw new Exception("Expected " + expected + " got " + result);
                }
            }
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
