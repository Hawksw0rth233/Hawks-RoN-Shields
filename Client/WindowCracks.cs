using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Hawks.RonShields
{
    public sealed class WindowCracks : MonoBehaviour
    {
        static readonly FieldInfo NativeMaterial=typeof(VisorEffect).GetField("_glassDamageMaterial",BindingFlags.NonPublic|BindingFlags.Instance);
        VisorEffect painter;
        MeshFilter window;
        RenderTexture target;
        Mesh overlayMesh;
        Material overlay,brush;
        int drawn;
        float retry;
        internal static WindowCracks Create(MeshFilter window)
        {
            var result=window.gameObject.AddComponent<WindowCracks>();result.window=window;return result;
        }
        bool Prepare()
        {
            if(target)return true;
            if(Time.unscaledTime<retry)return false;
            retry=Time.unscaledTime+1;
            var camera=EFT.CameraControl.CameraManager.Instance?.Camera;
            var visor=camera?camera.GetComponentInChildren<VisorEffect>(true):null;
            if(!visor)visor=Object.FindObjectOfType<VisorEffect>();
            var source=visor?NativeMaterial.GetValue(visor) as Material:null;
            var shader=Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
            if(!source || !shader)return false;
            var helper=new GameObject("Shield visor crack painter");helper.SetActive(false);helper.transform.SetParent(transform,false);
            painter=helper.AddComponent<VisorEffect>();brush=new Material(source);NativeMaterial.SetValue(painter,brush);
            target=new RenderTexture(512,256,0,RenderTextureFormat.ARGB32);target.Create();
            var previous=RenderTexture.active;
            try{RenderTexture.active=target;GL.Clear(false,true,Color.clear);}finally{RenderTexture.active=previous;}
            var node=new GameObject("Shield window cracks");node.layer=gameObject.layer;node.transform.SetParent(transform,false);
            overlayMesh=Instantiate(window.sharedMesh);var bounds=overlayMesh.bounds;var vertices=overlayMesh.vertices;var uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++)uv[i]=new Vector2(Mathf.InverseLerp(bounds.min.x,bounds.max.x,vertices[i].x),Mathf.InverseLerp(bounds.min.y,bounds.max.y,vertices[i].y));
            overlayMesh.uv=uv;node.AddComponent<MeshFilter>().sharedMesh=overlayMesh;
            overlay=new Material(shader){mainTexture=target,renderQueue=3010};node.AddComponent<MeshRenderer>().sharedMaterial=overlay;
            return true;
        }
        internal void Refresh(List<Vector2> impacts)
        {
            if(drawn>impacts.Count && target){
                var old=RenderTexture.active;
                try {RenderTexture.active=target;GL.Clear(false,true,Color.clear);drawn=0;}
                finally {RenderTexture.active=old;}
            }
            if(drawn>=impacts.Count || !Prepare())return;
            var previous=RenderTexture.active;GL.PushMatrix();
            try{
                RenderTexture.active=target;GL.LoadIdentity();GL.LoadProjectionMatrix(Matrix4x4.identity);
                for(;drawn<impacts.Count;drawn++){
                    Vector2 p=impacts[drawn];
                    painter.Draw(new Vector3(2*p.x-1,1-2*p.y,0),new Vector3(.36f,.70f,0),(drawn*137)%360);
                }
            }finally{GL.PopMatrix();RenderTexture.active=previous;}
        }
        void OnDestroy()
        {
            if(target){target.Release();Destroy(target);}if(overlayMesh)Destroy(overlayMesh);
            if(overlay)Destroy(overlay);if(brush)Destroy(brush);
        }
    }
}
