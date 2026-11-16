using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class Fmg
    {
        public byte Version;
        public SortedDictionary<int, string> Entries = new SortedDictionary<int, string>(); // null value = null string

        public static Fmg Read(byte[] b)
        {
            var f = new Fmg();
            if (b[1] != 0) throw new Exception("BE fmg");
            f.Version = b[2];
            if (f.Version != 2) throw new Exception("fmg version " + f.Version);
            int groupCount = BitConverter.ToInt32(b, 0xC);
            long so = BitConverter.ToInt64(b, 0x18);
            int p = 0x28;
            for (int g = 0; g < groupCount; g++, p += 16)
            {
                int idx = BitConverter.ToInt32(b, p), first = BitConverter.ToInt32(b, p + 4), last = BitConverter.ToInt32(b, p + 8);
                for (int id = first; id <= last; id++)
                {
                    long off = BitConverter.ToInt64(b, (int)so + 8 * (idx + id - first));
                    string s = null;
                    if (off != 0)
                    {
                        int e = (int)off; while (b[e] != 0 || b[e + 1] != 0) e += 2;
                        s = Encoding.Unicode.GetString(b, (int)off, e - (int)off);
                    }
                    f.Entries[id] = s;
                }
            }
            return f;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            var ids = Entries.Keys.ToList();
            var groups = new List<int[]>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (groups.Count > 0 && groups[groups.Count - 1][2] == ids[i] - 1) groups[groups.Count - 1][2] = ids[i];
                else groups.Add(new[] { i, ids[i], ids[i] });
            }
            bw.Write((byte)0); bw.Write((byte)0); bw.Write(Version); bw.Write((byte)0);
            bw.Write(0); // file size
            bw.Write((byte)1); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write(groups.Count);
            bw.Write(ids.Count);
            bw.Write(0xFF);
            bw.Write(0L); // string offsets offset
            bw.Write(0L);
            foreach (var g in groups) { bw.Write(g[0]); bw.Write(g[1]); bw.Write(g[2]); bw.Write(0); }
            long so = ms.Position;
            ms.Position = 0x18; bw.Write(so); ms.Position = so;
            bw.Write(new byte[8 * ids.Count]);
            var offs = new long[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                string s = Entries[ids[i]];
                if (s == null) continue;
                offs[i] = ms.Position;
                bw.Write(Encoding.Unicode.GetBytes(s)); bw.Write((short)0);
            }
            long end = ms.Position;
            ms.Position = so;
            foreach (var o in offs) bw.Write(o);
            ms.Position = 4; bw.Write((int)end);
            return ms.ToArray();
        }
    }
}
