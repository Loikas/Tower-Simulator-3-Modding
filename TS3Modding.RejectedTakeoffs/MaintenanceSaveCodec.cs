using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed class MaintenanceSaveRecord
    {
        internal string Kind="maintenance";internal double Stopped,Checks;internal bool Ready,StopReported,Returned,TugRequested,RunwayOccupied,OwnedRoute;internal int ClearSamples;
        internal int Network;internal string Callsign,Flight,Stand,Runway="";internal double Remaining;
    }
    internal static class MaintenanceSaveCodec
    {
        private static byte[] Hash(byte[] native){using(var h=SHA256.Create())return h.ComputeHash(native);}
        internal static byte[] Encode(byte[] native,IList<MaintenanceSaveRecord> records)
        {
            if(records.Count>256)throw new InvalidDataException("Too many maintenance records.");
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8)){
                writer.Write("TS3RTO-MAINTENANCE-2");writer.Write(Hash(native));writer.Write(records.Count);
                foreach(var r in records){Validate(r);writer.Write(r.Network);writer.Write(r.Callsign);writer.Write(r.Flight);writer.Write(r.Stand);writer.Write(r.Runway);writer.Write(r.Remaining);writer.Write(r.Kind);writer.Write(r.Stopped);writer.Write(r.Checks);writer.Write(r.Ready);writer.Write(r.StopReported);writer.Write(r.Returned);writer.Write(r.TugRequested);writer.Write(r.RunwayOccupied);writer.Write(r.OwnedRoute);writer.Write(r.ClearSamples);}
                writer.Flush();return stream.ToArray();
            }
        }
        private static void Validate(MaintenanceSaveRecord r){if(r==null||string.IsNullOrEmpty(r.Callsign)||r.Callsign.Length>128||r.Flight==null||r.Flight.Length>2048||r.Stand==null||r.Stand.Length>256||r.Runway==null||r.Runway.Length>128||double.IsNaN(r.Remaining)||double.IsInfinity(r.Remaining)||r.Remaining<0||r.Remaining>86400||(r.Kind!="maintenance"&&r.Kind!="incident")||double.IsNaN(r.Stopped)||double.IsInfinity(r.Stopped)||r.Stopped<0||r.Stopped>86400||double.IsNaN(r.Checks)||double.IsInfinity(r.Checks)||r.Checks<0||r.Checks>86400||r.ClearSamples<0||r.ClearSamples>1000000||r.Kind=="incident"&&(r.Runway.Length==0||r.Checks<=0||r.TugRequested&&r.Stand.Length==0))throw new InvalidDataException("Invalid maintenance record.");}
        internal static MaintenanceSaveRecord[] Decode(byte[] native,byte[] data)
        {
            if(data.Length>1024*1024)throw new InvalidDataException("Maintenance metadata exceeds its limit.");
            using(var stream=new MemoryStream(data))using(var reader=new BinaryReader(stream,Encoding.UTF8)){
                var version=reader.ReadString();if(version!="TS3RTO-MAINTENANCE-1"&&version!="TS3RTO-MAINTENANCE-2")throw new InvalidDataException("Unsupported maintenance metadata.");
                var hash=reader.ReadBytes(32);var expected=Hash(native);if(hash.Length!=32)throw new InvalidDataException("Incomplete maintenance metadata.");for(var i=0;i<32;i++)if(hash[i]!=expected[i])throw new InvalidDataException("Maintenance metadata belongs to another save.");
                var count=reader.ReadInt32();if(count<0||count>256)throw new InvalidDataException("Invalid maintenance record count.");
                var result=new List<MaintenanceSaveRecord>();var identities=new HashSet<string>();
                for(var i=0;i<count;i++){var r=new MaintenanceSaveRecord{Network=reader.ReadInt32(),Callsign=reader.ReadString(),Flight=reader.ReadString(),Stand=reader.ReadString(),Runway=reader.ReadString(),Remaining=reader.ReadDouble()};if(version=="TS3RTO-MAINTENANCE-2"){r.Kind=reader.ReadString();r.Stopped=reader.ReadDouble();r.Checks=reader.ReadDouble();r.Ready=reader.ReadBoolean();r.StopReported=reader.ReadBoolean();r.Returned=reader.ReadBoolean();r.TugRequested=reader.ReadBoolean();r.RunwayOccupied=reader.ReadBoolean();r.OwnedRoute=reader.ReadBoolean();r.ClearSamples=reader.ReadInt32();}Validate(r);if(!identities.Add(r.Network+"/"+r.Callsign))throw new InvalidDataException("Duplicate maintenance identity.");result.Add(r);}
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing maintenance data.");return result.ToArray();
            }
        }
    }
}
