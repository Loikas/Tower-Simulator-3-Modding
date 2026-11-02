using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.API
{
    public sealed class StripRoutingBlock
    {
        public int Id; public string Header; public string[] Runways; public int[] Next;
    }
    public static class StripRunwayRouting
    {
        // Siblings must share a real predecessor in this board, rather than a name convention.
        public static IReadOnlyList<StripRoute> Routes(StripRoutingBlock[] blocks,int source,bool arrival,int bin)
        {
            var current=blocks.Single(b=>b.Id==source);
            var ids=new HashSet<int>(current.Next??new int[0]);
            if(arrival&&(current.Runways?.Length??0)>0)
                foreach(var predecessor in blocks.Where(b=>(b.Next??new int[0]).Contains(source)))
                    foreach(var sibling in blocks.Where(b=>(predecessor.Next??new int[0]).Contains(b.Id)&&(b.Runways?.Length??0)>0))ids.Add(sibling.Id);
            return blocks.Where(b=>ids.Contains(b.Id)&&b.Id!=source&&b.Id!=bin).Select(b=>new StripRoute(b.Header,b.Runways??new string[0])).ToArray();
        }
    }
}
