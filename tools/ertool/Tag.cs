using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class TagSection
    {
        public string Tag;
        public bool Leaf;
        public byte[] Data; // leaf payload
        public List<TagSection> Children = new List<TagSection>();
        public TagSection Find(string t) { return Children.FirstOrDefault(c => c.Tag == t); }
    }

    class TagItem
    {
        public uint TypeFlags; // raw first u32
        public int Type { get { return (int)(TypeFlags & 0xFFFFFF); } }
        public int Flags { get { return (int)(TypeFlags >> 24); } }
        public int Offset, Count;
    }

    class TagFile
    {
        public TagSection Root;
        public byte[] Data;
        public List<string> TypeStrings, FieldStrings;
        public List<string> TypeNames = new List<string>(); // index -> name (index 0 = null)
        public List<TagItem> Items = new List<TagItem>();
        public List<KeyValuePair<int, List<int>>> Patches = new List<KeyValuePair<int, List<int>>>();
        public Dictionary<int, TypeBody> Bodies = new Dictionary<int, TypeBody>();

        public class Member { public string Name; public int Flags, Offset, Type; }
        public class TypeBody { public int Type, Parent, Opt, Format, SubType, Version, Size, Align, TFlags; public List<Member> Members = new List<Member>(); }

        static TagSection ReadSection(byte[] b, ref int p)
        {
            uint h = (uint)BE.I32(b, p);
            int size = (int)(h & 0x3FFFFFFF);
            bool leaf = (h & 0x40000000) != 0;
            var s = new TagSection { Tag = Encoding.ASCII.GetString(b, p + 4, 4), Leaf = leaf };
            int end = p + size;
            int q = p + 8;
            if (leaf) { s.Data = new byte[size - 8]; Buffer.BlockCopy(b, q, s.Data, 0, size - 8); }
            else while (q < end) s.Children.Add(ReadSection(b, ref q));
            p = end;
            return s;
        }

        static void WriteSection(BinaryWriter bw, TagSection s)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            if (s.Leaf) w.Write(s.Data);
            else foreach (var c in s.Children) WriteSection(w, c);
            int size = (int)ms.Length + 8;
            uint h = (uint)size | (s.Leaf ? 0x40000000u : 0u);
            bw.Write((byte)(h >> 24)); bw.Write((byte)(h >> 16)); bw.Write((byte)(h >> 8)); bw.Write((byte)h);
            bw.Write(Encoding.ASCII.GetBytes(s.Tag));
            ms.WriteTo(bw.BaseStream);
        }

        public static long VarInt(byte[] b, ref int p)
        {
            byte x = b[p++];
            if ((x & 0x80) == 0) return x;
            if ((x & 0xC0) == 0x80) { long v = ((x & 0x3F) << 8) | b[p]; p += 1; return v; }
            if ((x & 0xE0) == 0xC0) { long v = ((x & 0x1F) << 16) | (b[p] << 8) | b[p + 1]; p += 2; return v; }
            if ((x & 0xF0) == 0xE0) { long v = ((long)(x & 0x0F) << 24) | ((long)b[p] << 16) | ((long)b[p + 1] << 8) | b[p + 2]; p += 3; return v; }
            if ((x & 0xF8) == 0xF0) { long v = ((long)(x & 0x07) << 32) | ((long)b[p] << 24) | ((long)b[p + 1] << 16) | ((long)b[p + 2] << 8) | b[p + 3]; p += 4; return v; }
            throw new Exception("varint " + x.ToString("X2") + " at " + (p - 1));
        }

        static List<string> Strings(byte[] d)
        {
            var l = new List<string>();
            int s = 0;
            for (int i = 0; i < d.Length; i++) if (d[i] == 0) { l.Add(Encoding.ASCII.GetString(d, s, i - s)); s = i + 1; }
            return l;
        }

        public static TagFile Read(byte[] b)
        {
            var t = new TagFile();
            int p = 0;
            t.Root = ReadSection(b, ref p);
            if (Environment.GetEnvironmentVariable("TAGTREE") != null) { Action<TagSection, string> pr = null; pr = (s, ind) => { Console.WriteLine(ind + s.Tag + (s.Leaf ? " " + s.Data.Length : "")); foreach (var c in s.Children) pr(c, ind + "  "); }; pr(t.Root, ""); }
            if (t.Root.Tag != "TAG0") throw new Exception("not TAG0");
            t.Data = t.Root.Find("DATA").Data;
            var type = t.Root.Find("TYPE");
            t.TypeStrings = Strings(type.Find("TSTR").Data);
            t.FieldStrings = Strings(type.Find("FSTR").Data);
            var tn = (type.Find("TNAM") ?? type.Find("TNA1")).Data;
            int q = 0;
            long n = VarInt(tn, ref q);
            t.TypeNames.Add(null);
            for (int i = 1; i < n; i++)
            {
                string name = t.TypeStrings[(int)VarInt(tn, ref q)];
                long tc = VarInt(tn, ref q);
                var tps = new List<string>();
                for (int k = 0; k < tc; k++)
                {
                    string tpn = t.TypeStrings[(int)VarInt(tn, ref q)];
                    long v = VarInt(tn, ref q);
                    tps.Add(tpn + "=" + v);
                }
                t.TypeNames.Add(tps.Count > 0 ? name + "<" + string.Join(",", tps) + ">" : name);
            }
            if (Environment.GetEnvironmentVariable("TAGNAMES") != null) for (int i = 1; i < 40; i++) Console.WriteLine(i + " " + t.TypeNames[i]);
            var tb = type.Find("TBOD") ?? type.Find("TBDY");
            if (tb != null)
            {
                var d = tb.Data; q = 0;
                while (q < d.Length)
                {
                    int qStart = q; int ti = (int)VarInt(d, ref q);
                    try {
                    if (ti == 0) continue;
                    var body = new TypeBody { Type = ti };
                    body.Parent = (int)VarInt(d, ref q);
                    body.Opt = (int)VarInt(d, ref q);
                    if ((body.Opt & 1) != 0) body.Format = (int)VarInt(d, ref q);
                    if ((body.Opt & 2) != 0) body.SubType = (int)VarInt(d, ref q);
                    if ((body.Opt & 4) != 0) body.Version = (int)VarInt(d, ref q);
                    if ((body.Opt & 8) != 0) { body.Size = (int)VarInt(d, ref q); body.Align = (int)VarInt(d, ref q); }
                    if ((body.Opt & 0x10) != 0) body.TFlags = (int)VarInt(d, ref q);
                    if ((body.Opt & 0x20) != 0)
                    {
                        long mc = VarInt(d, ref q) & 0xFFFF;
                        for (int k = 0; k < mc; k++)
                        {
                            var m = new Member();
                            m.Name = t.FieldStrings[(int)VarInt(d, ref q)];
                            m.Flags = (int)VarInt(d, ref q); m.Offset = (int)VarInt(d, ref q); m.Type = (int)VarInt(d, ref q);
                            body.Members.Add(m);
                        }
                    }
                    if ((body.Opt & 0x40) != 0)
                    {
                        long ic = VarInt(d, ref q);
                        for (int k = 0; k < ic; k++) { VarInt(d, ref q); VarInt(d, ref q); }
                    }
                    if ((body.Opt & 0x80) != 0) VarInt(d, ref q);
                    if ((body.Opt & ~0xFF) != 0) throw new Exception("unknown opt bits " + body.Opt.ToString("X"));
                    t.Bodies[ti] = body;
                    } catch (Exception ex) { throw new Exception("TBDY parse failed at " + qStart + " type " + ti + " bytes " + BitConverter.ToString(d, qStart, Math.Min(40, d.Length - qStart)) + " fs=" + t.FieldStrings.Count, ex); }
                }
            }
            var indx = t.Root.Find("INDX");
            var it = indx.Find("ITEM").Data;
            for (int i = 0; i + 12 <= it.Length; i += 12)
                t.Items.Add(new TagItem { TypeFlags = BitConverter.ToUInt32(it, i), Offset = BitConverter.ToInt32(it, i + 4), Count = BitConverter.ToInt32(it, i + 8) });
            var pt = indx.Find("PTCH").Data;
            for (int i = 0; i < pt.Length;)
            {
                int ty = BitConverter.ToInt32(pt, i), c = BitConverter.ToInt32(pt, i + 4); i += 8;
                var l = new List<int>();
                for (int k = 0; k < c; k++, i += 4) l.Add(BitConverter.ToInt32(pt, i));
                t.Patches.Add(new KeyValuePair<int, List<int>>(ty, l));
            }
            return t;
        }

        public byte[] Write()
        {
            Root.Find("DATA").Data = Data;
            var indx = Root.Find("INDX");
            var ims = new MemoryStream(); var iw = new BinaryWriter(ims);
            foreach (var x in Items) { iw.Write(x.TypeFlags); iw.Write(x.Offset); iw.Write(x.Count); }
            indx.Find("ITEM").Data = ims.ToArray();
            var pms = new MemoryStream(); var pw = new BinaryWriter(pms);
            foreach (var kv in Patches) { pw.Write(kv.Key); pw.Write(kv.Value.Count); foreach (var o in kv.Value) pw.Write(o); }
            indx.Find("PTCH").Data = pms.ToArray();
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            WriteSection(bw, Root);
            return ms.ToArray();
        }

        public int TypeSize(int ti)
        {
            TypeBody b;
            while (ti != 0 && Bodies.TryGetValue(ti, out b))
            {
                if ((b.Opt & 8) != 0) return b.Size;
                ti = b.Parent;
            }
            return -1;
        }

        public IEnumerable<Member> AllMembers(int ti)
        {
            var chain = new List<TypeBody>();
            TypeBody b;
            while (ti != 0 && Bodies.TryGetValue(ti, out b)) { chain.Insert(0, b); ti = b.Parent; }
            foreach (var c in chain) foreach (var m in c.Members) yield return m;
        }
    }
}
