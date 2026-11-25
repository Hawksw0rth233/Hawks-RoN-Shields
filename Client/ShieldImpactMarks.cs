using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using EFT;
using Comfort.Common;
using HarmonyLib;
using UnityEngine;

namespace Hawks.RonShields
{
    internal readonly struct ShieldImpactMark
    {
        internal readonly int Id;
        internal readonly Vector3 Point;
        internal readonly Vector3 Normal;
        internal ShieldImpactMark(int id, Vector3 point, Vector3 normal)
        {
            Id=id; Point=point; Normal=normal;
        }
    }

    internal sealed class ShieldImpactMarks : MonoBehaviour
    {
        internal sealed class Marks
        {
            internal readonly List<ShieldImpactMark> Impacts=new();
            internal int Revision;
        }

        static readonly ConditionalWeakTable<GameWorld,Dictionary<string,Marks>> raidMarks=new();

        internal static Marks For(Item shield)
        {
            if(shield==null || !Singleton<GameWorld>.Instantiated)return null;
            var world=Singleton<GameWorld>.Instance;
            if(!world)return null;
            var items=raidMarks.GetValue(world,_=>new Dictionary<string,Marks>());
            var id=shield.Id.ToString();
            if(!items.TryGetValue(id,out var marks))items.Add(id,marks=new Marks());
            return marks;
        }

        static Mesh quad;
        static Material material;
        readonly Dictionary<int,GameObject> visuals = new();
        readonly List<int> expired = new();
        int revision=-1;

        Item shieldItem;

        internal static void Bind(GameObject visual,Item shield)
        {
            var display=visual.GetComponent<ShieldImpactMarks>() ?? visual.AddComponent<ShieldImpactMarks>();
            display.shieldItem=shield;
        }

        internal static bool IsImpactMesh(Mesh mesh) => quad && mesh==quad;

        void LateUpdate()=>Refresh(For(shieldItem));

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ArmorRepairStrategy),nameof(ArmorRepairStrategy.RepairItem)),
                postfix:new HarmonyMethod(typeof(ShieldImpactMarks),nameof(AfterRepair)));
        }

        static void AfterRepair(Item ____rootItem,ref Task<IResult> __result)
        {
            if(ShieldItemPreview.IsShield(____rootItem))
                __result=ClearAfterSuccess(__result,For(____rootItem));
        }

        static async Task<IResult> ClearAfterSuccess(Task<IResult> operation,Marks state)
        {
            var result=await operation;
            if(result.Succeed && state!=null && state.Impacts.Count>0)
            {
                state.Impacts.Clear();
                state.Revision++;
            }
            return result;
        }

        void Refresh(Marks state)
        {
            if(state==null || revision==state.Revision)return;
            if(state.Impacts.Count>0 && !PrepareMaterial())return;
            revision=state.Revision;
            expired.Clear();
            foreach(var id in visuals.Keys)
                if(!state.Impacts.Exists(mark=>mark.Id==id))expired.Add(id);
            foreach(var id in expired)
            {
                visuals[id].SetActive(false);
                Destroy(visuals[id]);
                visuals.Remove(id);
            }
            foreach(var mark in state.Impacts)
            {
                if(visuals.ContainsKey(mark.Id))continue;
                var node=new GameObject("Shield bullet impact"){layer=gameObject.layer};
                node.transform.SetParent(transform,false);
                var upright=Mathf.Abs(Vector3.Dot(mark.Normal,Vector3.up))>.95f?Vector3.forward:Vector3.up;
                node.transform.SetLocalPositionAndRotation(mark.Point+mark.Normal*.00065f,
                    Quaternion.LookRotation(mark.Normal,upright)*Quaternion.Euler(0,0,mark.Id*137f));
                node.transform.localScale=Vector3.one*.028f;
                node.AddComponent<MeshFilter>().sharedMesh=quad;
                var renderer=node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows=false;
                visuals.Add(mark.Id,node);
            }
        }

        static bool PrepareMaterial()
        {
            if(material)return true;
            var shader=Shader.Find("Sprites/Default");
            if(!shader)return false;
            const int size=64;
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                var point=new Vector2((x+0.5f)/size*2-1,(y+0.5f)/size*2-1);
                float angle=Mathf.Atan2(point.y,point.x);
                float radius=point.magnitude;
                float edge=.7f+.08f*Mathf.Sin(angle*7)+.07f*Mathf.Cos(angle*13);
                float alpha=Mathf.Clamp01((edge-radius)*8f);
                float rim=Mathf.Clamp01(1f-Mathf.Abs(radius-.55f)*13f);
                byte shade=(byte)(13f+rim*65f);
                pixels[y*size+x]=new Color32(shade,shade,shade,(byte)(alpha*235f));
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(pixels);
            texture.Apply(false,true);
            material=new Material(shader){name="Shield impact decal",mainTexture=texture};
            quad=new Mesh {name="Shield impact quad",
                vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f)},
                uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},
                colors=new[]{Color.white,Color.white,Color.white,Color.white}, triangles=new[]{0,2,1,0,3,2}};
            quad.RecalculateBounds();
            return true;
        }
    }
}
