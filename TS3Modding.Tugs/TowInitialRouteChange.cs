using System;
using System.Linq;
namespace TS3Modding.Tugs
{
    internal static class TowInitialRouteChange
    {
        // Native ground steering inserts a short push-out scaffold at the stand.
        // Accept only a bounded starting prefix, with an identical remaining route
        // and every runway encounter/hold entirely beyond both changed prefixes.
        internal static bool Safe(TowPoint[] before,TowPoint[] after,TowCrossingPreview[] oldEvents,TowCrossingPreview[] newEvents)
        {
            if(before==null||after==null||before.Length<2||after.Length<2||before.Any(p=>!p.Finite)||after.Any(p=>!p.Finite)||before[0].Distance(after[0])>.001||Math.Abs(before.Length-after.Length)>32)return false;
            int same=0;while(same<Math.Min(before.Length,after.Length)&&before[before.Length-1-same].Distance(after[after.Length-1-same])<.001)same++;
            if(same<2)return false;
            int oldPrefix=before.Length-same,newPrefix=after.Length-same;
            int common=0;while(common<Math.Min(oldPrefix,newPrefix)&&before[common].Distance(after[common])<.001)common++;
            double oldLength=Prefix(before,oldPrefix),newLength=Prefix(after,newPrefix);
            // Cargo stands can have a long unchanged lead-in before native steering
            // inserts its short turn scaffold. Bound the actual inserted region,
            // not the number/distance of unchanged waypoints preceding it.
            bool insertion=common==oldPrefix&&common>0&&newPrefix-oldPrefix>0&&newPrefix-oldPrefix<=32;
            if(insertion)
            {
                var anchor=before[common-1];
                if(after.Skip(common).Take(newPrefix-common).Any(p=>p.Distance(anchor)>100)||newLength-oldLength>200)return false;
            }
            else if(oldPrefix>32||newPrefix>32||oldLength>250||newLength>250||before.Take(oldPrefix+1).Concat(after.Take(newPrefix+1)).Any(p=>p.Distance(before[0])>150))return false;
            if(oldEvents==null||newEvents==null||oldEvents.Length!=newEvents.Length||oldEvents.Length==0)return false;
            if(oldEvents[0].Hold<=oldLength+20||newEvents[0].Hold<=newLength+20)return false;
            double delta=newLength-oldLength;
            for(int i=0;i<oldEvents.Length;i++)
            {
                var a=oldEvents[i];var b=newEvents[i];
                if(a.Runway!=b.Runway||a.Name!=b.Name||Math.Abs(b.Entry-a.Entry-delta)>.01||Math.Abs(b.Exit-a.Exit-delta)>.01)return false;
            }
            return true;
        }
        private static double Prefix(TowPoint[] path,int end)
        {double length=0;for(int i=1;i<=end;i++)length+=path[i-1].Distance(path[i]);return length;}
    }
}
