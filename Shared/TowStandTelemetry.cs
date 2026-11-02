using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace TS3Modding.DisplayBridge
{
    internal sealed class TowStandMarker
    {
        internal DateTime CreatedUtc;internal int NetworkId;internal string Callsign;
        internal float X,Y,Z,Heading;internal string Destination;
    }
    internal static class TowStandTelemetry
    {
        internal static string PathForGame(string root)=>HoldDisplayTelemetry.PathForGame(root)+".tow";
        internal static string Encode(TowStandMarker m)=>m==null?"":string.Join("\t",new[]{"TS3-TOW-2",m.CreatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),m.NetworkId.ToString(CultureInfo.InvariantCulture),Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Callsign)),m.X.ToString("R",CultureInfo.InvariantCulture),m.Y.ToString("R",CultureInfo.InvariantCulture),m.Z.ToString("R",CultureInfo.InvariantCulture),m.Heading.ToString("R",CultureInfo.InvariantCulture),Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Destination??""))});
        internal static TowStandMarker Decode(string text,DateTime now)
        {
            try{
                if(text==null||text.Length>2048)return null;var p=text.Split('\t');if(p.Length!=9||p[0]!="TS3-TOW-2")return null;
                var m=new TowStandMarker{CreatedUtc=new DateTime(long.Parse(p[1],CultureInfo.InvariantCulture),DateTimeKind.Utc),NetworkId=int.Parse(p[2],CultureInfo.InvariantCulture),Callsign=Encoding.UTF8.GetString(Convert.FromBase64String(p[3])),X=float.Parse(p[4],CultureInfo.InvariantCulture),Y=float.Parse(p[5],CultureInfo.InvariantCulture),Z=float.Parse(p[6],CultureInfo.InvariantCulture),Heading=float.Parse(p[7],CultureInfo.InvariantCulture)};
                m.Destination=Encoding.UTF8.GetString(Convert.FromBase64String(p[8]));
                if(now-m.CreatedUtc>TimeSpan.FromSeconds(3)||m.CreatedUtc-now>TimeSpan.FromSeconds(1)||string.IsNullOrWhiteSpace(m.Callsign)||m.Destination.Length>64)return null;
                foreach(var v in new[]{m.X,m.Y,m.Z,m.Heading})if(float.IsNaN(v)||float.IsInfinity(v))return null;return m;
            }catch(Exception e)when(e is FormatException||e is ArgumentException||e is OverflowException){return null;}
        }
        internal static TowStandMarker Read(string path,DateTime now){try{return Decode(File.ReadAllText(path),now);}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}}
        internal static TowStandMarker[] DecodeMany(string text,DateTime now)
        {if(text==null||text.Length>131072)return new TowStandMarker[0];return text.Split('\n').Take(64).Select(line=>Decode(line,now)).Where(m=>m!=null).ToArray();}
        internal static TowStandMarker[] ReadMany(string path,DateTime now){try{return DecodeMany(File.ReadAllText(path),now);}catch(IOException){return new TowStandMarker[0];}catch(UnauthorizedAccessException){return new TowStandMarker[0];}}
        internal static void Write(string path,TowStandMarker marker)
        {
            WriteMany(path,marker==null?new TowStandMarker[0]:new[]{marker});
        }
        internal static void WriteMany(string path,TowStandMarker[] markers){Directory.CreateDirectory(Path.GetDirectoryName(path));var temporary=path+".tmp";File.WriteAllText(temporary,string.Join("\n",markers.Take(64).Select(Encode)));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
    }
}
