using System;
using System.IO;
using UnityEngine;

namespace Hawks.RonShields
{
    internal sealed class ShieldPose
    {
        internal Vector3 Shield, Left, Right, RightShoulder, RightElbow;
        internal float AimWeight;
        internal Quaternion ShieldRotation, RightWristRotation;
        internal readonly Vector3[] Fingers = new Vector3[15];
        internal void Blend(ShieldPose a, ShieldPose b, float t)
        {
            AimWeight=Mathf.Lerp(a.AimWeight,b.AimWeight,t);
            Shield=Vector3.Lerp(a.Shield,b.Shield,t);
            Left=Vector3.Lerp(a.Left,b.Left,t);
            Right=Vector3.Lerp(a.Right,b.Right,t);
            RightShoulder=Vector3.Lerp(a.RightShoulder,b.RightShoulder,t);
            RightElbow=Vector3.Lerp(a.RightElbow,b.RightElbow,t);
            ShieldRotation=Quaternion.Slerp(a.ShieldRotation,b.ShieldRotation,t);
            RightWristRotation=Quaternion.Slerp(a.RightWristRotation,b.RightWristRotation,t);
            for(int i=0;i<Fingers.Length;i++) Fingers[i]=Vector3.Lerp(a.Fingers[i],b.Fingers[i],t);
        }
    }
    internal readonly struct ShieldPoseFrame
    {
        internal readonly Vector3 Center,Left,Right,Upper,Elbow;
        internal readonly Quaternion LeftBasis,Rotation;
        internal ShieldPoseFrame(ShieldPose p,Vector3 shoulder,Quaternion facing,Quaternion fatigue,float scale)
        {
            var turn=p.ShieldRotation*Quaternion.Inverse(ShieldMotion.Frames[48].ShieldRotation);
            var basis=turn*Quaternion.Inverse(p.ShieldRotation);
            var rightBasis=facing*basis;
            Center=shoulder+facing*(p.Shield*scale);
            Rotation=facing*fatigue*turn;
            LeftBasis=facing*fatigue*basis;
            Left=Center+LeftBasis*((p.Left-p.Shield)*scale);
            var rest=Shields.RightRestCm;
            var aim=new Vector3(5f,-1.5f,0f)+(Shields.RightAimCm);
            var rightShift=facing*(Vector3.Lerp(rest,aim,p.AimWeight)*(.01f*scale));
            Right=Center+rightBasis*((p.Right-p.Shield)*scale)+rightShift;
            Upper=Center+rightBasis*((p.RightShoulder-p.Shield)*scale);
            Elbow=Center+rightBasis*((p.RightElbow-p.Shield)*scale)+rightShift*.5f;
        }
    }
    internal static class ShieldMotion
    {
        internal static Quaternion WristDelta(ShieldPose pose)
        {
            var rest=Quaternion.Euler(Shields.RightRestDegrees);
            var aim=Quaternion.Euler(Shields.RightAimDegrees);
            var adjustment=Quaternion.Slerp(rest,aim,pose.AimWeight);
            return adjustment*Quaternion.AngleAxis(-pose.AimWeight,Vector3.right)*pose.RightWristRotation*Quaternion.Inverse(Frames[100].RightWristRotation);
        }
        internal static readonly ShieldPose[] Frames=Read();
        static Vector3 V(BinaryReader b)=>new Vector3(b.ReadSingle(),b.ReadSingle(),b.ReadSingle());
        static Quaternion Q(BinaryReader b)=>new Quaternion(b.ReadSingle(),b.ReadSingle(),b.ReadSingle(),b.ReadSingle());
        static ShieldPose[] Read()
        {
            using(var stream=typeof(ShieldMotion).Assembly.GetManifestResourceStream("RoN.ShieldMotion"))
            using(var b=new BinaryReader(stream)){
                if(stream.Length!=179*68*sizeof(float))throw new InvalidDataException("Invalid shield motion track");
                var frames=new ShieldPose[179];
                for(int i=0;i<frames.Length;i++){
                    var p=new ShieldPose{Shield=V(b),ShieldRotation=Q(b),Left=V(b),Right=V(b),RightWristRotation=Q(b),RightShoulder=V(b),RightElbow=V(b)};
                    for(int j=0;j<p.Fingers.Length;j++)p.Fingers[j]=V(b);
                    // Author a lateral target on the raise / hold / lower tracks before pose blending and IK.
                    int frame=i+1;
                    float extension=Mathf.Min(Mathf.InverseLerp(80,100,frame),Mathf.InverseLerp(141,121,frame));
                    p.AimWeight=Mathf.SmoothStep(0f,1f,extension);
                    frames[i]=p;
                }
                return frames;
            }
        }
        internal static void Sample(int first,int last,float phase,ShieldPose output)
        {
            float frame=first-1+(last-first)*Mathf.Clamp01(phase);
            int i=(int)frame;
            output.Blend(Frames[i],Frames[Math.Min(i+1,last-1)],frame-i);
        }
    }
}
