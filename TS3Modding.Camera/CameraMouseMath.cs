using System;
namespace TS3Modding.CameraMod
{
    internal static class CameraMouseMath
    {
        public static float Wheel(float value,float steps,float strength,float minimum,float maximum)
            =>Clamp((float)(value*Math.Exp(-steps*strength)),minimum,maximum);
        public static float Orbit(float degrees,float pixels,float sensitivity)
        {var next=degrees+pixels*sensitivity;return (next+180f)%360f<0?(next+180f)%360f+180f:(next+180f)%360f-180f;}
        public static float Pitch(float degrees,float pixels,float sensitivity)=>Clamp(degrees+pixels*sensitivity,0,75);
        private static float Clamp(float value,float minimum,float maximum)=>Math.Max(minimum,Math.Min(maximum,value));
    }
}
