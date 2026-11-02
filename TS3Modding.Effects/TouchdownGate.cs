namespace TS3Modding.Effects
{
    // Arm only after observing airborne motion in this live session. A loaded parked
    // aircraft must not generate a puff, nor may ground-altitude jitter repeat it.
    internal sealed class TouchdownGate
    {
        private bool airborne;
        internal bool Observe(bool live, bool landing, float altitude)
        {
            if(!live || float.IsNaN(altitude) || float.IsInfinity(altitude)){airborne=false;return false;}
            if(altitude>3){airborne=true;return false;}
            if(altitude>1)return false;
            bool trigger=airborne && landing;
            airborne=false;
            return trigger;
        }
    }
}
