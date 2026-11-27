using System.Collections.Generic;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class WindowCracks : MonoBehaviour
    {
        const int Width = 512,
            Height = 256;
        MeshFilter window;
        Mesh overlayMesh;
        Material material;
        Texture2D texture;
        Color32[] pixels;
        int drawn;
        static readonly Color32 LineColor = new Color32(211, 228, 236, 218);
        static readonly Color32 JunctionColor = new Color32(235, 245, 252, 245);

        internal static WindowCracks Create(MeshFilter window)
        {
            var cracks = window.gameObject.AddComponent<WindowCracks>();
            cracks.window = window;
            return cracks;
        }

        bool Prepare()
        {
            if (texture)
                return true;
            var shader = Shader.Find("Sprites/Default");
            if (!shader || !window || !window.sharedMesh)
                return false;
            pixels = new Color32[Width * Height];
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = "Shield window crack mask",
                wrapMode = TextureWrapMode.Clamp,
            };
            material = new Material(shader) { mainTexture = texture, renderQueue = 3010 };
            var mesh = window.sharedMesh;
            var bounds = mesh.bounds;
            var vertices = mesh.vertices;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                uv[i] = new Vector2(
                    Mathf.InverseLerp(bounds.min.x, bounds.max.x, vertices[i].x),
                    Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y)
                );

            // The clear pane does not write depth. One double-sided mesh at
            // its midpoint is visible from either side without stacking cracks.
            var face = new Vector3[vertices.Length];
            var tint = new Color[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                face[i] = new Vector3(vertices[i].x, vertices[i].y, bounds.center.z);
                tint[i] = Color.white;
            }
            var triangles = mesh.triangles;
            var front = new List<int>();
            var back = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i],
                    b = triangles[i + 1],
                    c = triangles[i + 2];
                float normal = Vector3.Cross(face[b] - face[a], face[c] - face[a]).z;
                if (Mathf.Abs(normal) < 1e-9f)
                    continue;
                var side = normal > 0 ? front : back;
                side.Add(a);
                side.Add(b);
                side.Add(c);
            }
            overlayMesh = new Mesh
            {
                name = "Shield pane crack overlay",
                vertices = face,
                uv = uv,
                colors = tint,
                triangles = (front.Count > 0 ? front : back).ToArray(),
            };
            overlayMesh.RecalculateBounds();
            var node = new GameObject("Shield window cracks") { layer = gameObject.layer };
            node.transform.SetParent(transform, false);
            node.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        internal void Refresh(List<Vector2> impacts)
        {
            if (drawn == impacts.Count || !Prepare())
                return;
            if (drawn > impacts.Count)
            {
                System.Array.Clear(pixels, 0, pixels.Length);
                drawn = 0;
            }
            for (; drawn < impacts.Count; drawn++)
                Draw(impacts[drawn], drawn);
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        void Draw(Vector2 impact, int index)
        {
            var center = new Vector2(impact.x * (Width - 1), impact.y * (Height - 1));
            var random = new System.Random(7283 + index * 197);
            for (int spoke = 0; spoke < 8; spoke++)
            {
                float angle = (spoke + (float)random.NextDouble() * .65f) * Mathf.PI * .25f;
                float reach = 20f + (float)random.NextDouble() * 36f;
                var middle = center + Direction(angle) * reach * .46f;
                var end =
                    middle
                    + Direction(angle + ((float)random.NextDouble() - .5f) * .55f) * reach * .54f;
                PlotLine(center, middle);
                PlotLine(middle, end);
                PlotLine(middle, middle + Direction(angle + .5f) * reach * .34f);
            }
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
                if (x * x + y * y <= 4)
                    SetPixel(
                        Mathf.RoundToInt(center.x) + x,
                        Mathf.RoundToInt(center.y) + y,
                        JunctionColor
                    );
        }

        static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        void PlotLine(Vector2 start, Vector2 end)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((end - start).magnitude * 1.5f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(start, end, i / (float)steps);
                SetPixel(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), LineColor);
            }
        }

        void SetPixel(int x, int y, Color32 color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return;
            int i = y * Width + x;
            if (pixels[i].a < color.a)
                pixels[i] = color;
        }

        void OnDestroy()
        {
            if (overlayMesh)
                Destroy(overlayMesh);
            if (material)
                Destroy(material);
            if (texture)
                Destroy(texture);
        }
    }
}
