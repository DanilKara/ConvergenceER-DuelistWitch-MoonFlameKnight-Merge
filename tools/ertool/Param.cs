using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class PRow
    {
        public int ID;
        public byte[] Data;
        public string Name; // null = no name offset (0)
    }

    class Param
    {
        public byte[] Head; // 0x40
        public string ParamType;
        public int RowSize;
        public List<PRow> Rows = new List<PRow>();
        public bool DedupNames;

        public static Param Read(byte[] b)
        {
            var p = new Param();
            p.Head = new byte[0x40];
            Buffer.BlockCopy(b, 0, p.Head, 0, 0x40);
            if (b[0x2D] != 0x85 && b[0x2D] != 0x05 && (b[0x2D] & 0x80) == 0) throw new Exception("Unexpected param format " + b[0x2D].ToString("X2"));
            int rowCount = BitConverter.ToUInt16(b, 0xA);
            long typeOff = BitConverter.ToInt64(b, 0x10);
            long dataStart = BitConverter.ToInt64(b, 0x30);
            int e = (int)typeOff; while (b[e] != 0) e++;
            p.ParamType = Encoding.ASCII.GetString(b, (int)typeOff, e - (int)typeOff);
            p.RowSize = rowCount > 0 ? (int)((typeOff - dataStart) / rowCount) : 0;
            if (rowCount > 0 && (typeOff - dataStart) % rowCount != 0) throw new Exception("row size not integral");
            var seen = new HashSet<long>();
            for (int i = 0; i < rowCount; i++)
            {
                int h = 0x40 + i * 24;
                var r = new PRow();
                r.ID = BitConverter.ToInt32(b, h);
                long doff = BitConverter.ToInt64(b, h + 8);
                long noff = BitConverter.ToInt64(b, h + 16);
                if (doff != dataStart + (long)i * p.RowSize) throw new Exception("non-contiguous data row " + i);
                r.Data = new byte[p.RowSize];
                Buffer.BlockCopy(b, (int)doff, r.Data, 0, p.RowSize);
                if (noff != 0)
                {
                    if (!seen.Add(noff)) p.DedupNames = true;
                    int q = (int)noff; while (b[q] != 0 || b[q + 1] != 0) q += 2;
                    r.Name = Encoding.Unicode.GetString(b, (int)noff, q - (int)noff);
                }
                p.Rows.Add(r);
            }
            return p;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            byte[] head = (byte[])Head.Clone();
            int n = Rows.Count;
            long dataStart = 0x40 + 24L * n;
            long typeOff = dataStart + (long)n * RowSize;
            BitConverter.GetBytes((uint)typeOff).CopyTo(head, 0);
            BitConverter.GetBytes((ushort)n).CopyTo(head, 0xA);
            BitConverter.GetBytes(typeOff).CopyTo(head, 0x10);
            BitConverter.GetBytes(dataStart).CopyTo(head, 0x30);
            bw.Write(head);
            // compute name offsets
            long pos = typeOff + Encoding.ASCII.GetByteCount(ParamType) + 1;
            var nameOffs = new long[n];
            var dict = new Dictionary<string, long>();
            var nameBlob = new MemoryStream();
            for (int i = 0; i < n; i++)
            {
                string nm = Rows[i].Name;
                if (nm == null) { nameOffs[i] = 0; continue; }
                long o;
                if (DedupNames && dict.TryGetValue(nm, out o)) { nameOffs[i] = o; continue; }
                o = pos + nameBlob.Length;
                byte[] nb = Encoding.Unicode.GetBytes(nm);
                nameBlob.Write(nb, 0, nb.Length); nameBlob.WriteByte(0); nameBlob.WriteByte(0);
                if (DedupNames) dict[nm] = o;
                nameOffs[i] = o;
            }
            for (int i = 0; i < n; i++)
            {
                bw.Write(Rows[i].ID); bw.Write(0);
                bw.Write(dataStart + (long)i * RowSize);
                bw.Write(nameOffs[i]);
            }
            foreach (var r in Rows)
            {
                if (r.Data.Length != RowSize) throw new Exception("bad row size");
                bw.Write(r.Data);
            }
            bw.Write(Encoding.ASCII.GetBytes(ParamType)); bw.Write((byte)0);
            nameBlob.WriteTo(ms);
            bw.Write((short)0);
            return ms.ToArray();
        }
    }
}
