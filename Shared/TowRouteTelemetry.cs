using System;
using System.IO;
using System.Linq;
namespace TS3Modding.DisplayBridge
{
    internal struct TowRouteVertex { internal float X,Y,Z;internal TowRouteVertex(float x,float y,float z){X=x;Y=y;Z=z;} }
    internal sealed class TowRoutePreview
    {
        internal DateTime CreatedUtc;internal int NetworkId;internal string Callsign;internal bool NativeChecked;
        internal TowRouteVertex[] Points;
    }
    internal static class TowRouteTelemetry
    {
        internal static string PathForGame(string root)=>HoldDisplayTelemetry.PathForGame(root)+".tow-route";
        internal static TowRoutePreview Selected(TowRoutePreview[] routes,int network,string callsign,DateTime now)
        {
            var matches=(routes??new TowRoutePreview[0]).Where(r=>r!=null&&r.NetworkId==network&&string.Equals(r.Callsign,callsign,StringComparison.OrdinalIgnoreCase)&&now-r.CreatedUtc<=TimeSpan.FromSeconds(3)&&r.CreatedUtc-now<=TimeSpan.FromSeconds(1)).ToArray();
            return matches.Length==1?matches[0]:null;
        }
        internal static byte[] Encode(TowRoutePreview[] routes)
        {
            if(routes==null||routes.Length>64||routes.Any(r=>r==null||string.IsNullOrWhiteSpace(r.Callsign)||r.Callsign.Length>64||r.Points==null||r.Points.Length<2||r.Points.Length>4096)||routes.Sum(r=>(long)r.Points.Length)>32768)throw new InvalidDataException("Invalid tow route preview bounds.");
            using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))
            {
                writer.Write("TS3-TOW-ROUTE-1");writer.Write(routes.Length);
                foreach(var r in routes){writer.Write(r.CreatedUtc.Ticks);writer.Write(r.NetworkId);writer.Write(r.Callsign);writer.Write(r.NativeChecked);writer.Write(r.Points.Length);foreach(var p in r.Points){if(new[]{p.X,p.Y,p.Z}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))throw new InvalidDataException("Invalid route point.");writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Z);}}
                writer.Flush();return output.ToArray();
            }
        }
        internal static TowRoutePreview[] Decode(byte[] bytes,DateTime now)
        {
            try
            {
                if(bytes==null||bytes.Length>524288)return new TowRoutePreview[0];
                using(var input=new MemoryStream(bytes))using(var reader=new BinaryReader(input))
                {
                    if(reader.ReadString()!="TS3-TOW-ROUTE-1")return new TowRoutePreview[0];
                    var count=reader.ReadInt32();if(count<0||count>64)return new TowRoutePreview[0];var routes=new TowRoutePreview[count];var total=0;
                    for(int i=0;i<count;i++)
                    {
                        var r=new TowRoutePreview{CreatedUtc=new DateTime(reader.ReadInt64(),DateTimeKind.Utc),NetworkId=reader.ReadInt32(),Callsign=reader.ReadString(),NativeChecked=reader.ReadBoolean()};
                        var length=reader.ReadInt32();total+=length;if(length<2||length>4096||total>32768||string.IsNullOrWhiteSpace(r.Callsign)||r.Callsign.Length>64)return new TowRoutePreview[0];
                        r.Points=new TowRouteVertex[length];for(int j=0;j<length;j++){var p=new TowRouteVertex(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(new[]{p.X,p.Y,p.Z}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))return new TowRoutePreview[0];r.Points[j]=p;}routes[i]=r;
                    }
                    if(input.Position!=input.Length)return new TowRoutePreview[0];
                    return routes.Where(r=>now-r.CreatedUtc<=TimeSpan.FromSeconds(3)&&r.CreatedUtc-now<=TimeSpan.FromSeconds(1)).ToArray();
                }
            }catch(Exception e)when(e is IOException||e is ArgumentException||e is OverflowException){return new TowRoutePreview[0];}
        }
        internal static void Write(string path,TowRoutePreview[] routes)
        {var bytes=Encode(routes);Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        internal static TowRoutePreview[] Read(string path,DateTime now)
        {try{if(new FileInfo(path).Length>524288)return new TowRoutePreview[0];return Decode(File.ReadAllBytes(path),now);}catch(IOException){return new TowRoutePreview[0];}catch(UnauthorizedAccessException){return new TowRoutePreview[0];}}
    }
}
