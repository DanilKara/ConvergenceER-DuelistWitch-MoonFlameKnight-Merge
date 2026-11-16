using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    static partial class Program
    {
        public static Bnd4 OpenBnd(string path) { Dcx d; bool e; return Bnd4.Read(Open(path, out d, out e)); }

        public static string Short(string n) { int i = n.LastIndexOfAny(new[] { '\\', '/' }); return i >= 0 ? n.Substring(i + 1) : n; }

        public static bool Eq(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string Hexs(byte[] d, int o, int n) { return BitConverter.ToString(d, o, Math.Min(n, d.Length - o)).Replace("-", " "); }

        static int Extra(string[] args)
        {
            string cmd = args[0];
            if (cmd == "param-roundtrip")
            {
                var bnd = OpenBnd(args[1]);
                int bad = 0;
                foreach (var f in bnd.Files)
                {
                    var p = Param.Read(f.Bytes);
                    var w = p.Write();
                    if (!Eq(w, f.Bytes)) { bad++; Console.WriteLine("MISMATCH " + Short(f.Name) + " " + f.Bytes.Length + " vs " + w.Length); }
                }
                Console.WriteLine("checked " + bnd.Files.Count + " bad=" + bad);
                return 0;
            }
            if (cmd == "param-diff3")
            {
                // param-diff3 base witch knight [paramFilter]
                var B = OpenBnd(args[1]); var W = OpenBnd(args[2]); var K = OpenBnd(args[3]);
                string filter = args.Length > 4 ? args[4] : null;
                for (int i = 0; i < B.Files.Count; i++)
                {
                    string nm = Short(B.Files[i].Name);
                    if (filter != null && !nm.StartsWith(filter)) continue;
                    var wf = W.Files.First(x => x.Name == B.Files[i].Name);
                    var kf = K.Files.First(x => x.Name == B.Files[i].Name);
                    if (Eq(wf.Bytes, B.Files[i].Bytes) && Eq(kf.Bytes, B.Files[i].Bytes)) continue;
                    var pb = Param.Read(B.Files[i].Bytes); var pw = Param.Read(wf.Bytes); var pk = Param.Read(kf.Bytes);
                    Console.WriteLine("=== " + nm + " rowsize=" + pb.RowSize + " rows b/w/k=" + pb.Rows.Count + "/" + pw.Rows.Count + "/" + pk.Rows.Count + (pb.RowSize != pw.RowSize || pb.RowSize != pk.RowSize ? " ROWSIZE MISMATCH" : ""));
                    var db = pb.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    var dw = pw.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    var dk = pk.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    var ids = new SortedSet<int>(db.Keys.Concat(dw.Keys).Concat(dk.Keys));
                    int wOnly = 0, kOnly = 0, same = 0, conf = 0;
                    var lines = new List<string>();
                    foreach (int id in ids)
                    {
                        PRow rb, rw, rk; db.TryGetValue(id, out rb); dw.TryGetValue(id, out rw); dk.TryGetValue(id, out rk);
                        bool wc = !RowEq(rb, rw), kc = !RowEq(rb, rk);
                        if (!wc && !kc) continue;
                        if (wc && kc)
                        {
                            if (RowEq(rw, rk)) { same++; continue; }
                            conf++;
                            string s = "  CONFLICT id=" + id + " b=" + Desc(rb) + " w=" + Desc(rw) + " k=" + Desc(rk);
                            if (rb != null && rw != null && rk != null)
                            {
                                var offs = new List<string>();
                                for (int o = 0; o < pb.RowSize; o++)
                                {
                                    bool a = rw.Data[o] != rb.Data[o], c = rk.Data[o] != rb.Data[o];
                                    if (a && c && rw.Data[o] != rk.Data[o]) offs.Add("0x" + o.ToString("X") + "(" + rb.Data[o].ToString("X2") + "/" + rw.Data[o].ToString("X2") + "/" + rk.Data[o].ToString("X2") + ")");
                                }
                                s += " byteConflicts=" + offs.Count + " " + string.Join(",", offs.Take(12));
                            }
                            lines.Add(s);
                        }
                        else if (wc) { wOnly++; if (args.Length > 5) lines.Add("  W id=" + id + " " + Desc(rb) + " -> " + Desc(rw)); }
                        else { kOnly++; if (args.Length > 5) lines.Add("  K id=" + id + " " + Desc(rb) + " -> " + Desc(rk)); }
                    }
                    Console.WriteLine("  witchOnly=" + wOnly + " knightOnly=" + kOnly + " sameChange=" + same + " conflicts=" + conf);
                    foreach (var l in lines) Console.WriteLine(l);
                }
                return 0;
            }
            if (cmd == "param-dump")
            {
                // param-dump regulation paramName id1,id2,...
                var bnd = OpenBnd(args[1]);
                var f = bnd.Files.First(x => Short(x.Name).StartsWith(args[2]));
                var p = Param.Read(f.Bytes);
                var ids = args.Length > 3 ? new HashSet<int>(args[3].Split(new[]{','}, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)) : null;
                int lo = args.Length > 4 ? int.Parse(args[4]) : int.MinValue, hi = args.Length > 5 ? int.Parse(args[5]) : int.MaxValue;
                foreach (var r in p.Rows)
                {
                    if (ids != null && ids.Count > 0 && !ids.Contains(r.ID)) continue;
                    if (r.ID < lo || r.ID > hi) continue;
                    Console.WriteLine(r.ID + " [" + r.Name + "] " + Hexs(r.Data, 0, r.Data.Length));
                }
                return 0;
            }
            if (cmd == "emevd-dump")
            {
                Dcx d; bool enc;
                Console.Write(Emevd.Read(Open(args[1], out d, out enc)).Dump());
                return 0;
            }
            if (cmd == "emevd-roundtrip")
            {
                Dcx d; bool enc;
                byte[] raw = Open(args[1], out d, out enc);
                byte[] w = Emevd.Read(raw).Write();
                Console.WriteLine(raw.Length + " vs " + w.Length + " eq=" + Eq(raw, w));
                for (int i = 0, n = 0; i < Math.Min(raw.Length, w.Length) && n < 8; i++) if (raw[i] != w[i]) { Console.WriteLine("diff 0x" + i.ToString("X")); n++; }
                return 0;
            }
            if (cmd == "tag-info")
            {
                byte[] raw = File.ReadAllBytes(args[1]);
                var t = TagFile.Read(raw);
                Console.WriteLine("roundtrip eq=" + Eq(t.Write(), raw));
                Action<TagSection, string> pr = null;
                pr = (s, ind) => { Console.WriteLine(ind + s.Tag + (s.Leaf ? " " + s.Data.Length : "")); foreach (var c in s.Children) pr(c, ind + "  "); };
                pr(t.Root, "");
                Console.WriteLine("types=" + t.TypeNames.Count + " bodies=" + t.Bodies.Count + " items=" + t.Items.Count + " patchGroups=" + t.Patches.Count + " patches=" + t.Patches.Sum(x => x.Value.Count));
                foreach (var g in t.Items.Skip(1).GroupBy(x => x.Type).OrderByDescending(g => g.Count()).Take(25))
                    Console.WriteLine("  type " + g.Key + " " + t.TypeNames[g.Key] + " items=" + g.Count() + " flags=" + string.Join(",", g.Select(x => x.Flags.ToString("X")).Distinct()) + " size=" + t.TypeSize(g.Key));
                foreach (var nm in args.Skip(2))
                {
                    int ti = t.TypeNames.IndexOf(nm);
                    Console.WriteLine("TYPE " + nm + " idx=" + ti + " size=" + t.TypeSize(ti));
                    foreach (var m in t.AllMembers(ti)) Console.WriteLine("   +" + m.Offset + " " + m.Name + " : " + t.TypeNames[m.Type] + " (" + m.Type + ") flags=" + m.Flags);
                }
                for (int i = 0; i < 12; i++) { var x = t.Items[i]; Console.WriteLine("item " + i + " type=" + x.Type + " " + t.TypeNames[x.Type] + " flags=" + x.Flags.ToString("X") + " off=" + x.Offset + " count=" + x.Count); }
                foreach (var kv in t.Patches.Take(10)) Console.WriteLine("patch type " + kv.Key + " " + t.TypeNames[kv.Key] + " n=" + kv.Value.Count + " first=" + string.Join(",", kv.Value.Take(5)));
                return 0;
            }
            if (cmd == "tag-item")
            {
                var t = TagFile.Read(File.ReadAllBytes(args[1]));
                var h = new TagHelper(t);
                foreach (var a in args.Skip(2)) { int i = int.Parse(a); var x = t.Items[i]; Console.WriteLine("item " + i + " " + t.TypeNames[x.Type] + " flags " + x.Flags.ToString("X") + " off " + x.Offset + " count " + x.Count + " size " + h.ItemSize(i) + " name=" + h.ObjName(i)); Console.WriteLine("  " + BitConverter.ToString(t.Data, x.Offset, Math.Min(h.ItemSize(i), 288))); foreach (var r in h.ReferrersOf(i)) Console.WriteLine("  ref by " + r.Key + " " + t.TypeNames[t.Items[r.Key].Type] + " +" + (r.Value - t.Items[r.Key].Offset) + " name=" + h.ObjName(r.Key)); }
                return 0;
            }
            if (cmd == "tag-clips")
            {
                var t = TagFile.Read(File.ReadAllBytes(args[1]));
                var h = new TagHelper(t);
                int ti = t.TypeNames.IndexOf("hkbClipGenerator");
                int ai = h.MemberOffset(ti, "animationInternalId"), an = h.MemberOffset(ti, "animationName"), tr = h.MemberOffset(ti, "triggers"), vb = h.MemberOffset(ti, "variableBindingSet");
                for (int i = 1; i < t.Items.Count; i++) if (t.Items[i].Type == ti) { var o = t.Items[i].Offset; Console.WriteLine(h.ObjName(i) + "	" + h.Str(h.Ptr(o + an)) + "	" + BitConverter.ToInt16(t.Data, o + ai) + "	" + h.Ptr(o + tr) + "	" + h.Ptr(o + vb)); }
                return 0;
            }
            if (cmd == "tag-explore")
            {
                var t = TagFile.Read(File.ReadAllBytes(args[1]));
                var h = new TagHelper(t);
                int ci = h.FindByName("hkbClipGenerator", args[2]);
                Console.WriteLine("clip item " + ci);
                int cur = ci;
                for (int depth = 0; depth < 4 && cur > 0; depth++)
                {
                    var refs = h.ReferrersOf(cur);
                    foreach (var r in refs) Console.WriteLine("  ref by item " + r.Key + " (" + t.TypeNames[t.Items[r.Key].Type] + " flags " + t.Items[r.Key].Flags.ToString("X") + " count " + t.Items[r.Key].Count + ") at data+" + (r.Value - t.Items[r.Key].Offset) + " patchType " + h.PatchTypeAt(r.Value) + " name=" + h.ObjName(r.Key));
                    cur = refs.Count > 0 ? refs[0].Key : -1;
                }
                foreach (var nm in args.Skip(3))
                {
                    int ti = t.TypeNames.IndexOf(nm);
                    Console.WriteLine("TYPE " + nm + " idx=" + ti + " size=" + t.TypeSize(ti));
                    foreach (var m in t.AllMembers(ti)) Console.WriteLine("   +" + m.Offset + " " + m.Name + " : " + t.TypeNames[m.Type] + " (" + m.Type + ") flags=" + m.Flags);
                }
                return 0;
            }
            if (cmd == "beh-merge")
            {
                // beh-merge base target source out
                var B = TagFile.Read(File.ReadAllBytes(args[1]));
                var W = TagFile.Read(File.ReadAllBytes(args[2]));
                var K = TagFile.Read(File.ReadAllBytes(args[3]));
                File.WriteAllBytes(args[4], BehMerge.Merge(B, W, K, Console.Out));
                return 0;
            }
            if (cmd == "beh-sem")
            {
                var t = TagFile.Read(File.ReadAllBytes(args[1]));
                File.WriteAllLines(args[2], BehMerge.Semantic(t, true));
                return 0;
            }
            if (cmd == "tae-dump")
            {
                Console.Write(Tae.Dump(File.ReadAllBytes(args[1])));
                return 0;
            }
            if (cmd == "merge-all")
            {
                return MergeAll.Run(args[1], args[2], args[3], args[4], args[5], args[6]) == 0 ? 0 : 3;
            }
            if (cmd == "verify-reg")
            {
                // verify-reg base witch knight merged : every row equals the expected source
                var B = OpenBnd(args[1]); var W = OpenBnd(args[2]); var K = OpenBnd(args[3]); var M = OpenBnd(args[4]);
                var reloc = new Dictionary<string, Dictionary<int, int>> {
                    { "CharaInitParam.param", new Dictionary<int, int> { { 3029, 3030 }, { 3158, 3160 }, { 3159, 3161 } } },
                    { "CharMakeMenuListItemParam.param", new Dictionary<int, int> { { 100229, 100230 } } } };
                int bad = 0, checkedRows = 0;
                foreach (var mf in M.Files)
                {
                    string nm = Short(mf.Name);
                    var pb = Param.Read(B.Files.First(x => x.Name == mf.Name).Bytes);
                    var pw = Param.Read(W.Files.First(x => x.Name == mf.Name).Bytes);
                    var pk = Param.Read(K.Files.First(x => x.Name == mf.Name).Bytes);
                    var pm = Param.Read(mf.Bytes);
                    for (int i = 1; i < pm.Rows.Count; i++) if (pm.Rows[i].ID < pm.Rows[i - 1].ID) { Console.WriteLine("UNSORTED " + nm); bad++; break; }
                    Dictionary<int, int> rl; reloc.TryGetValue(nm, out rl);
                    var db = pb.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    var dw = pw.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    var dk = new Dictionary<int, PRow>();
                    foreach (var r in pk.Rows) { int id = r.ID; int nid; if (rl != null && rl.TryGetValue(id, out nid)) id = nid; if (!dk.ContainsKey(id)) dk[id] = r; }
                    var dm = pm.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.First());
                    // knight rows at relocated old ids should look like base there
                    if (rl != null) foreach (var kv in rl) { PRow x; if (db.TryGetValue(kv.Key, out x)) dk[kv.Key] = x; else dk.Remove(kv.Key); }
                    var ids = new HashSet<int>(db.Keys.Concat(dw.Keys).Concat(dk.Keys).Concat(dm.Keys));
                    foreach (int id in ids)
                    {
                        PRow rb, rw, rk, rm; db.TryGetValue(id, out rb); dw.TryGetValue(id, out rw); dk.TryGetValue(id, out rk); dm.TryGetValue(id, out rm);
                        checkedRows++;
                        bool wc = !RowEq(rb, rw), kc = !RowEq(rb, rk);
                        PRow expect = kc && !wc ? rk : rw;
                        if (kc && wc && !RowEq(rw, rk)) { Console.WriteLine("both changed " + nm + " " + id); continue; }
                        bool ok = expect == null ? rm == null : (rm != null && Eq(expect.Data, rm.Data));
                        if (!ok) { bad++; if (bad < 20) Console.WriteLine("BAD " + nm + " row " + id); }
                    }
                }
                var menu = Param.Read(M.Files.First(x => Short(x.Name) == "CharMakeMenuListItemParam.param").Bytes);
                foreach (var r in menu.Rows.Where(r => r.ID >= 100226 && r.ID <= 100299)) Console.WriteLine("menu " + r.ID + " [" + r.Name + "] value=" + BitConverter.ToInt32(r.Data, 0) + " text=" + BitConverter.ToInt32(r.Data, 4));
                var init = Param.Read(M.Files.First(x => Short(x.Name) == "CharaInitParam.param").Bytes);
                foreach (var r in init.Rows.Where(r => r.ID == 3029 || r.ID == 3030 || (r.ID >= 3156 && r.ID <= 3165))) Console.WriteLine("init " + r.ID + " [" + r.Name + "]");
                Console.WriteLine("rows checked " + checkedRows + " bad=" + bad);
                return 0;
            }
            if (cmd == "verify-bnd")
            {
                // verify-bnd base witch knight merged : every merged entry equals witch's or knight's, nothing lost
                var B = OpenBnd(args[1]); var W = OpenBnd(args[2]); var K = OpenBnd(args[3]); var M = OpenBnd(args[4]);
                Func<Bnd4, Dictionary<string, BndFile>> d = b => b.Files.ToDictionary(f => f.Name.ToLowerInvariant());
                var db = d(B); var dw = d(W); var dk = d(K); var dm = d(M);
                int bad = 0;
                foreach (var kv in dm)
                {
                    BndFile w, k, b; dw.TryGetValue(kv.Key, out w); dk.TryGetValue(kv.Key, out k); db.TryGetValue(kv.Key, out b);
                    bool eqw = w != null && Eq(w.Bytes, kv.Value.Bytes), eqk = k != null && Eq(k.Bytes, kv.Value.Bytes);
                    string src = eqw && eqk ? "both" : eqw ? "witch" : eqk ? "knight" : "MERGED";
                    bool kChanged = k != null && (b == null || !Eq(b.Bytes, k.Bytes));
                    bool wChanged = w != null && (b == null || !Eq(b.Bytes, w.Bytes));
                    if (src == "witch" && kChanged) { Console.WriteLine("  knight change dropped: " + Short(kv.Key)); bad++; }
                    if (src == "knight" && wChanged) { Console.WriteLine("  witch change dropped: " + Short(kv.Key)); bad++; }
                    if (src == "MERGED") Console.WriteLine("  merged entry: " + Short(kv.Key));
                }
                foreach (var k in dw.Keys.Concat(dk.Keys).Distinct()) if (!dm.ContainsKey(k)) { Console.WriteLine("  missing: " + Short(k)); bad++; }
                var ids = M.Files.GroupBy(f => f.ID).Where(g => g.Count() > 1).Count();
                Console.WriteLine("entries " + M.Files.Count + " dupIds=" + ids + " bad=" + bad);
                return 0;
            }
            if (cmd == "reg-head")
            {
                byte[] b = Regulation.Decrypt(File.ReadAllBytes(args[1]));
                Console.WriteLine(BitConverter.ToString(b, 0, 0x60));
                int comp = BE.I32(b, 0x20);
                Console.WriteLine("comp=" + comp + " decrypted len=" + b.Length + " tail=" + BitConverter.ToString(b, 0x4C + comp - 8, Math.Min(24, b.Length - 0x4C - comp + 8)));
                return 0;
            }
            if (cmd == "reg-recompress")
            {
                byte[] rawFile = File.ReadAllBytes(args[1]);
                Dcx d; bool enc;
                byte[] raw = Open(args[1], out d, out enc);
                if (args.Length > 3 && args[3] == "rewrite") { var bnd = Bnd4.Read(raw); foreach (var f in bnd.Files) f.Bytes = Param.Read(f.Bytes).Write(); raw = bnd.Write(); }
                byte[] iv = new byte[16]; Buffer.BlockCopy(rawFile, 0, iv, 0, 16);
                File.WriteAllBytes(args[2], Regulation.Encrypt(d.Compress(raw), iv));
                return 0;
            }
            if (cmd == "dcx-recompress")
            {
                Dcx d; bool enc;
                byte[] raw = Open(args[1], out d, out enc);
                File.WriteAllBytes(args[2], d.Compress(raw));
                return 0;
            }
            if (cmd == "fmg-rewrite")
            {
                Dcx d; bool enc;
                var bnd = Bnd4.Read(Open(args[1], out d, out enc));
                foreach (var f in bnd.Files) f.Bytes = Fmg.Read(f.Bytes).Write();
                File.WriteAllBytes(args[2], args.Length > 3 ? File.ReadAllBytes(args[1]).Take(0).ToArray() : d.Compress(bnd.Write()));
                return 0;
            }
            if (cmd == "dump")
            {
                foreach (var a in args.Skip(1)) { Console.WriteLine("=== " + Path.GetFileName(a) + " " + File.GetLastWriteTime(a)); Console.WriteLine(MiniDump.Analyze(a)); }
                return 0;
            }
            if (cmd == "esd-dump")
            {
                var e = Esd.Read(File.ReadAllBytes(args[1]));
                string d1 = e.Dump();
                File.WriteAllText(args[2], d1);
                byte[] w = e.Write();
                string d2 = Esd.Read(w).Dump();
                Console.WriteLine("semantic roundtrip eq=" + (d1 == d2) + " bytes eq=" + Eq(w, File.ReadAllBytes(args[1])) + " " + w.Length);
                if (args.Length > 3) File.WriteAllBytes(args[3], w);
                return 0;
            }
            if (cmd == "tpf-list")
            {
                Dcx d; bool enc;
                byte[] raw = Open(args[1], out d, out enc);
                var t = Tpf.Read(raw);
                foreach (var x in t.Textures) Console.WriteLine(x.Name + "\t" + x.Format + "\t" + x.Mipmaps + "\t" + x.Flags1 + "\t" + x.Data.Length + "\t" + Md5(x.Data));
                byte[] w = t.Write();
                Console.Error.WriteLine("roundtrip eq=" + Eq(w, raw) + " " + raw.Length + " vs " + w.Length);
                return 0;
            }
            if (cmd == "fmg-range")
            {
                var bnd = OpenBnd(args[1]);
                var f = Fmg.Read(bnd.Files.First(x => Short(x.Name) == args[2]).Bytes);
                int lo = int.Parse(args[3]), hi = int.Parse(args[4]);
                foreach (var kv in f.Entries) if (kv.Key >= lo && kv.Key <= hi) Console.WriteLine(kv.Key + "\t" + (kv.Value == null ? "<null>" : kv.Value.Replace("\n", " / ")));
                return 0;
            }
            if (cmd == "fmg-roundtrip")
            {
                var bnd = OpenBnd(args[1]);
                int bad = 0;
                foreach (var f in bnd.Files)
                {
                    var w = Fmg.Read(f.Bytes).Write();
                    if (!Eq(w, f.Bytes)) { bad++; Console.WriteLine("MISMATCH " + Short(f.Name) + " " + f.Bytes.Length + " vs " + w.Length); }
                }
                Console.WriteLine("checked " + bnd.Files.Count + " bad=" + bad);
                return 0;
            }
            if (cmd == "fmg-diff3")
            {
                var B = OpenBnd(args[1]); var W = OpenBnd(args[2]); var K = OpenBnd(args[3]);
                foreach (var bf in B.Files)
                {
                    var wf = W.Files.First(x => x.Name == bf.Name); var kf = K.Files.First(x => x.Name == bf.Name);
                    if (Eq(wf.Bytes, bf.Bytes) && Eq(kf.Bytes, bf.Bytes)) continue;
                    var fb = Fmg.Read(bf.Bytes); var fw = Fmg.Read(wf.Bytes); var fk = Fmg.Read(kf.Bytes);
                    Console.WriteLine("=== " + Short(bf.Name));
                    var ids = new SortedSet<int>(fb.Entries.Keys.Concat(fw.Entries.Keys).Concat(fk.Entries.Keys));
                    foreach (int id in ids)
                    {
                        string sb, sw, sk; bool hb = fb.Entries.TryGetValue(id, out sb), hw = fw.Entries.TryGetValue(id, out sw), hk = fk.Entries.TryGetValue(id, out sk);
                        bool wc = hb != hw || sb != sw, kc = hb != hk || sb != sk;
                        if (!wc && !kc) continue;
                        string tag = wc && kc ? (hw == hk && sw == sk ? "SAME" : "CONFLICT") : (wc ? "W" : "K");
                        Func<bool, string, string> d = (h, s) => !h ? "<absent>" : s == null ? "<null>" : "\"" + s.Replace("\n", "\\n") + "\"";
                        Console.WriteLine("  " + tag + " " + id + " b=" + Trunc(d(hb, sb)) + (wc ? " w=" + Trunc(d(hw, sw)) : "") + (kc ? " k=" + Trunc(d(hk, sk)) : ""));
                    }
                }
                return 0;
            }
            Console.Error.WriteLine("unknown command " + cmd);
            return 2;
        }

        static bool RowEq(PRow a, PRow b)
        {
            if (a == null || b == null) return a == b;
            return a.Name == b.Name && Eq(a.Data, b.Data);
        }

        static string Trunc(string s) { return s.Length > 90 ? s.Substring(0, 90) + "…" : s; }

        static string Desc(PRow r) { return r == null ? "<none>" : "[" + r.Name + "]"; }
    }
}
