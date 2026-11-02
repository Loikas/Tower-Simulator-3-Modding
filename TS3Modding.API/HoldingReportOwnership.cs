using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.API
{
    // A custom operations module may own holding speech for exact native aircraft objects.
    public static class HoldingReportOwnership
    {
        private static readonly object Gate=new object();
        private static readonly List<Func<object,bool>> Owners=new List<Func<object,bool>>();
        public static IDisposable Register(Func<object,bool> owns)
        {if(owns==null)throw new ArgumentNullException(nameof(owns));lock(Gate)Owners.Add(owns);return new Registration(owns);}
        public static bool IsOwned(object aircraft)
        {if(aircraft==null)return false;Func<object,bool>[] snapshot;lock(Gate)snapshot=Owners.ToArray();return snapshot.Any(owns=>owns(aircraft));}
        private sealed class Registration:IDisposable
        {
            private Func<object,bool> owns;internal Registration(Func<object,bool> owns){this.owns=owns;}
            public void Dispose(){lock(Gate){if(owns!=null)Owners.Remove(owns);owns=null;}}
        }
    }
}
