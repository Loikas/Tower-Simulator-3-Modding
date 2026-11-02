using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TS3Modding.DisplayBridge
{
    internal sealed class HoldMarker
    {
        public int NetworkId;
        public string Callsign;
        public double X, Z;
        public string Key => NetworkId.ToString(CultureInfo.InvariantCulture)+":"+(Callsign??"").ToUpperInvariant();
    }
    internal sealed class HoldDisplaySnapshot
    {
        public DateTime CreatedUtc;
        public readonly Dictionary<string,HoldMarker> Markers=new Dictionary<string,HoldMarker>();
        public bool Fresh(DateTime now) => now-CreatedUtc<=TimeSpan.FromSeconds(3)&&CreatedUtc-now<=TimeSpan.FromSeconds(1);
    }
    internal static class HoldDisplayTelemetry
    {
        public static string PathForGame(string gameRoot)
        {
            string key;
            using(var sha=SHA256.Create()) key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).TrimEnd('\\','/').ToUpperInvariant()))).Replace("-","").Substring(0,20);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TS3Modding","Runtime",key+"-holds.txt");
        }
        public static string Encode(IEnumerable<HoldMarker> markers,DateTime now)
        {
            var text=new StringBuilder("TS3-HOLDS-1\t"+now.Ticks.ToString(CultureInfo.InvariantCulture)+"\n");
            foreach(var row in markers.Take(4096)) text.Append(row.NetworkId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(row.Callsign))).Append('\t')
                .Append(row.X.ToString("R",CultureInfo.InvariantCulture)).Append('\t').Append(row.Z.ToString("R",CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }
        public static HoldDisplaySnapshot Decode(string text,DateTime now)
        {
            try
            {
                if(text==null||text.Length>1024*1024) return null;
                var lines=text.Split('\n'); var header=lines[0].TrimEnd('\r').Split('\t'); long ticks;
                if(header.Length!=2||header[0]!="TS3-HOLDS-1"||!long.TryParse(header[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out ticks)) return null;
                var result=new HoldDisplaySnapshot{CreatedUtc=new DateTime(ticks,DateTimeKind.Utc)};
                if(!result.Fresh(now)||lines.Length>4098) return null;
                foreach(var raw in lines.Skip(1))
                {
                    if(string.IsNullOrWhiteSpace(raw)) continue;
                    var p=raw.TrimEnd('\r').Split('\t'); int id; double x,z;
                    if(p.Length!=4||!int.TryParse(p[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out id)||
                        !double.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out z)||
                        double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(z)||double.IsInfinity(z)) return null;
                    var name=Encoding.UTF8.GetString(Convert.FromBase64String(p[1])); if(name.Length==0||name.Length>128) return null;
                    var marker=new HoldMarker{NetworkId=id,Callsign=name,X=x,Z=z}; result.Markers.Add(marker.Key,marker);
                }
                return result;
            }
            catch(ArgumentException) { return null; }
            catch(FormatException) { return null; }
        }
        public static HoldDisplaySnapshot Read(string path,DateTime now)
        {
            try
            {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                { if(stream.Length>1024*1024) return null; using(var reader=new StreamReader(stream)) return Decode(reader.ReadToEnd(),now); }
            }
            catch(IOException) { return null; }
            catch(UnauthorizedAccessException) { return null; }
        }
        public static void Write(string path,IEnumerable<HoldMarker> rows,DateTime now)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,Encode(rows,now),new UTF8Encoding(false));
                if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        public static IEnumerable<HoldMarker> Requests(string path,DateTime now)
        {
            var directory=Path.GetDirectoryName(path); if(!Directory.Exists(directory)) return new HoldMarker[0];
            return Directory.GetFiles(directory,Path.GetFileName(path)+".request-*")
                .Where(file=>now-File.GetLastWriteTimeUtc(file)<TimeSpan.FromSeconds(3)).Take(32)
                .Select(file=>Read(file,now)).Where(snapshot=>snapshot!=null).SelectMany(snapshot=>snapshot.Markers.Values)
                .GroupBy(marker=>marker.Key).Select(group=>group.First()).ToArray();
        }
    }
}
