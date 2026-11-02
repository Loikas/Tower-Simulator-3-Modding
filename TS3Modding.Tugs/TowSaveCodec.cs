using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    // Data only, never BinaryFormatter or native object deserialization. Unknown
    // JSON fields are ignored by the native loader. Metadata travels with the save.
    internal static class TowSaveCodec
    {
        private const string Key="ts3moddingTows";
        private static string Hash(string value){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
        internal static string Attach(string json,IList<Dictionary<string,string>> records)
        {
            if(records.Count==0)return json;
            if(json.Contains("\""+Key+"\""))throw new InvalidDataException("Tow metadata already exists.");
            var end=json.LastIndexOf('}');if(end<0||!json.TrimStart().StartsWith("{"))throw new InvalidDataException("Native save is not a JSON object.");
            byte[] bytes;
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Encoding.UTF8))
            {
                w.Write(1);w.Write(records.Count);
                foreach(var record in records){w.Write(record.Count);foreach(var item in record.OrderBy(x=>x.Key,StringComparer.Ordinal)){w.Write(item.Key);w.Write(item.Value??"");}}
                w.Flush();bytes=stream.ToArray();
            }
            if(bytes.Length>8*1024*1024)throw new InvalidDataException("Tow save exceeds metadata limit.");
            return json.Insert(end,",\""+Key+"\":\""+Hash(json)+"."+Convert.ToBase64String(bytes)+"\"");
        }
        internal static List<Dictionary<string,string>> Read(string json)
        {
            if(!json.Contains("\""+Key+"\""))return new List<Dictionary<string,string>>();
            var m=Regex.Match(json,",\""+Key+"\":\"([A-F0-9]{64})\\.([A-Za-z0-9+/=]+)\"(?=\\}\\s*$)");
            if(!m.Success||m.Groups[2].Length>12*1024*1024||Hash(json.Remove(m.Index,m.Length))!=m.Groups[1].Value)throw new InvalidDataException("Tow metadata is damaged or belongs to a different native save.");
            var result=new List<Dictionary<string,string>>();
            using(var stream=new MemoryStream(Convert.FromBase64String(m.Groups[2].Value)))using(var r=new BinaryReader(stream,Encoding.UTF8))
            {
                if(r.ReadInt32()!=1)throw new InvalidDataException("Unsupported tow save version.");
                int count=r.ReadInt32();if(count<0||count>128)throw new InvalidDataException("Invalid tow record count.");
                for(int i=0;i<count;i++)
                {
                    int fields=r.ReadInt32();if(fields<1||fields>128)throw new InvalidDataException("Invalid tow field count.");
                    var record=new Dictionary<string,string>(StringComparer.Ordinal);
                    for(int j=0;j<fields;j++){var key=r.ReadString();var value=r.ReadString();if(key.Length>80||value.Length>2*1024*1024||record.ContainsKey(key))throw new InvalidDataException("Invalid tow field.");record.Add(key,value);}
                    result.Add(record);
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing tow data.");
            }
            return result;
        }
    }
}
