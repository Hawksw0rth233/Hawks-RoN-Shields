using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class ClearViewportBuild {
 public static void Run() {
  var dir=Path.GetFullPath("../Build/Viewport"); Directory.CreateDirectory(dir);
  var result=BuildPipeline.BuildAssetBundles(dir,new[]{new AssetBundleBuild{
   assetBundleName="hawks.ron.viewport.bundle",assetNames=new[]{"Assets/Shields/ClearViewport.shader"}}},
   BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
  if(!result)throw new Exception("Viewport build failed");
  var bundle=AssetBundle.LoadFromFile(Path.Combine(dir,"hawks.ron.viewport.bundle"));
  var shader=bundle.LoadAsset<Shader>("ClearViewport");
  if(!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))throw new Exception("Viewport shader unavailable");
  // Draw an opaque quad AFTER the glass, behind it. The result must agree with an empty aperture.
  var camera=new GameObject("Check camera").AddComponent<Camera>();
  camera.transform.position=new Vector3(0,0,-3);camera.clearFlags=CameraClearFlags.SolidColor;
  camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=1;
  camera.cullingMask=1<<28;
  var target=new RenderTexture(64,64,24);camera.targetTexture=target;
  var pane=GameObject.CreatePrimitive(PrimitiveType.Quad);pane.layer=28;
  var material=new Material(shader);pane.GetComponent<Renderer>().sharedMaterial=material;
  var marker=GameObject.CreatePrimitive(PrimitiveType.Quad);marker.layer=28;
  marker.transform.position=Vector3.forward;
  var green=new Material(Shader.Find("Unlit/Color"));green.color=Color.green;green.renderQueue=3001;
  marker.GetComponent<Renderer>().sharedMaterial=green;
  var read=new Texture2D(64,64);
  Color Sample(){camera.Render();RenderTexture.active=target;read.ReadPixels(new Rect(0,0,64,64),0,0);read.Apply();return read.GetPixel(32,32);}
  var old=RenderTexture.active;
  try {
   var behind=Sample(); if(behind.g<.9f || behind.r>.1f)throw new Exception("Window blocked background depth "+behind);
   marker.transform.position+=Vector3.right*2; var after=Sample();
   if(after.g>.20f)throw new Exception("Previous frame remained "+after);
   pane.SetActive(false);
   camera.orthographicSize=.035f;
   foreach(var name in new[]{"Patrol","HRT"}){
    var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Shields/"+name+".prefab"));
    var window=Array.Find(model.GetComponentsInChildren<MeshFilter>(),m=>m.name=="Window" && m.sharedMesh.bounds.size.x>.25f && m.sharedMesh.bounds.center.y>.15f && m.sharedMesh.bounds.center.y<.35f);
    var surface=window.GetComponent<Renderer>();surface.sharedMaterial=material;window.gameObject.layer=28;
    foreach(float side in new[]{-1f,1f}){
     var centre=surface.bounds.center;
     camera.transform.position=centre+Vector3.forward*side;
     camera.transform.LookAt(centre);camera.backgroundColor=Color.white;
     surface.enabled=false;var bare=Sample();
     surface.enabled=true;var glass=Sample();
     if(bare.r-glass.r<.045f || glass.r<.60f || glass.g<=glass.r)throw new Exception(name+" pane visibility "+side+" "+bare+" "+glass);
     Debug.Log("PANE PASS "+name+" side "+side+" aperture="+bare+" glass="+glass);
    }
    UnityEngine.Object.DestroyImmediate(model);
   }
   if(material.passCount!=1)throw new Exception("Unexpected extra pass");
   Debug.Log("VIEWPORT PASS "+SystemInfo.graphicsDeviceType+" behind="+behind+" after="+after);
  }finally{RenderTexture.active=old;camera.targetTexture=null;target.Release();}
 }
}
