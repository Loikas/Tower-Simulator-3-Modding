using System;
namespace TS3Modding.Effects
{
    internal static class ParticleFade
    {
        internal static float EmissionAt(float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0||seconds>=.75f)return 0;
            return (float)Math.Exp(-seconds*7)*Math.Max(0,1-seconds/.75f);
        }
        internal static int Frame(int column,int rowFromBottom)=> (7-rowFromBottom)*8+column;
        // This is baked into lifetime atlas frames, never written to shared material
        // opacity while particles exist. Each particle advances through its own age.
        internal static float At(float age)
        {
            if(float.IsNaN(age)||float.IsInfinity(age)||age<=0||age>=1)return 0;
            float birth=Math.Min(1,age/.06f);
            float remaining=1-age;
            return birth*remaining*remaining*(3-2*remaining);
        }
    }
}
