namespace TS3Modding.Tugs
{
    internal static class TowCommandMode
    {
        internal static string Refusal(bool towOwned,bool requestingPushback,bool atStand,string state)
        {
            if(towOwned)return null;
            if(requestingPushback)return "we require pushback";
            if(!atStand&&(state=="STATE_TO_RUNWAY"||state=="STATE_TO_TERMINAL"||state=="STATE_ESCAPE_RUNWAY"||state=="STATE_TO_INTERSECTION"))return "we are taxiing under our own power";
            return null;
        }
    }
}
