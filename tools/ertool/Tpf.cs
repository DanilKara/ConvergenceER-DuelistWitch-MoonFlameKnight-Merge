using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class TpfTex
    {
        public string Name;
        public byte Format, Type, Mipmaps, Flags1;
        public int Flags2;
        public byte[] Data;
    }

    // PC TPF (as used by Elden Ring menu textures)
    class Tpf
    {
        public byte[] Head; // 0x10
        public List<TpfTex> Textures = new List<TpfTex>();

        public static Tpf Read(byte[] b)
        {
            if (Encoding.ASCII.GetString(b, 0, 3) != "TPF") throw new Exception("not TPF");
            var t = new Tpf();
            t.Head = new byte[0x10]; Buffer.BlockCopy(b, 0, t.Head, 0, 0x10);
            int count = BitConverter.ToInt32(b, 8);
            if (b[0xC] != 0) throw new Exception("non-PC TPF");
            bool uni = b[0xE] == 1;
            for (int i = 0; i < count; i++)
            {
                int p = 0x10 + i * 0x14;
                var x = new TpfTex();
                int off = BitConverter.ToInt32(b, p), size = BitConverter.ToInt32(b, p + 4);
                x.Format = b[p + 8]; x.Type = b[p + 9]; x.Mipmaps = b[p + 10]; x.Flags1 = b[p + 11];
                if (x.Flags1 != 0 && x.Flags1 != 1) throw new Exception("compressed tpf entry");
                int no = BitConverter.ToInt32(b, p + 12);
                x.Flags2 = BitConverter.ToInt32(b, p + 16);
                if (x.Flags2 != 0) throw new Exception("tpf float struct unsupported");
                int e = no;
                if (uni) { while (b[e] != 0 || b[e + 1] != 0) e += 2; x.Name = Encoding.Unicode.GetString(b, no, e - no); }
                else { while (b[e] != 0) e++; x.Name = Encoding.GetEncoding(932).GetString(b, no, e - no); }
                x.Data = new byte[size]; Buffer.BlockCopy(b, off, x.Data, 0, size);
                t.Textures.Add(x);
            }
            return t;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bool uni = Head[0xE] == 1;
            bw.Write(Head);
            int n = Textures.Count;
            bw.Write(new byte[0x14 * n]);
            var nameOff = new int[n];
            for (int i = 0; i < n; i++)
            {
                nameOff[i] = (int)ms.Position;
                if (uni) { bw.Write(Encoding.Unicode.GetBytes(Textures[i].Name)); bw.Write((short)0); }
                else { bw.Write(Encoding.GetEncoding(932).GetBytes(Textures[i].Name)); bw.Write((byte)0); }
            }
            var dataOff = new int[n];
            long dataStart = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == 0) while (ms.Position % 0x10 != 0) bw.Write((byte)0);
                if (i == 0) dataStart = ms.Position;
                dataOff[i] = (int)ms.Position;
                bw.Write(Textures[i].Data);
            }
            long end = ms.Position;
            ms.Position = 4; bw.Write((int)(end - dataStart)); bw.Write(n);
            for (int i = 0; i < n; i++)
            {
                ms.Position = 0x10 + i * 0x14;
                var x = Textures[i];
                bw.Write(dataOff[i]); bw.Write(x.Data.Length); bw.Write(x.Format); bw.Write(x.Type); bw.Write(x.Mipmaps); bw.Write(x.Flags1); bw.Write(nameOff[i]); bw.Write(x.Flags2);
            }
            return ms.ToArray();
        }
    }
}
