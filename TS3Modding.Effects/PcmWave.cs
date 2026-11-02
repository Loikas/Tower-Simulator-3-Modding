using System;
using System.IO;
namespace TS3Modding.Effects
{
    internal sealed class PcmWave
    {
        internal float[] Samples;internal int Channels,Rate;
        internal static PcmWave Read(Stream stream)
        {
            if(stream==null)throw new InvalidDataException("Missing sound resource");
            using(var r=new BinaryReader(stream))
            {
                if(new string(r.ReadChars(4))!="RIFF")throw new InvalidDataException("Expected RIFF");r.ReadUInt32();
                if(new string(r.ReadChars(4))!="WAVE")throw new InvalidDataException("Expected WAVE");
                int format=0,channels=0,rate=0,bits=0;byte[] data=null;
                while(stream.Position+8<=stream.Length)
                {
                    var id=new string(r.ReadChars(4));int size=checked((int)r.ReadUInt32());long end=stream.Position+size;
                    if(size<0 || end>stream.Length)throw new InvalidDataException("Truncated WAV chunk");
                    if(id=="fmt "){if(size<16)throw new InvalidDataException("Short format");format=r.ReadUInt16();channels=r.ReadUInt16();rate=r.ReadInt32();r.ReadInt32();r.ReadUInt16();bits=r.ReadUInt16();}
                    else if(id=="data")data=r.ReadBytes(size);
                    stream.Position=end+(size%2);
                }
                if(format!=1 || bits!=16 || channels<1 || channels>2 || rate<8000 || rate>96000 || data==null || data.Length==0 || data.Length%(channels*2)!=0)throw new InvalidDataException("Expected nonempty PCM16 WAV");
                var samples=new float[data.Length/2];for(int i=0;i<samples.Length;i++)samples[i]=(short)(data[i*2]|data[i*2+1]<<8)/32768f;
                return new PcmWave{Samples=samples,Channels=channels,Rate=rate};
            }
        }
    }
}
