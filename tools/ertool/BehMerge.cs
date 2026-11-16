using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    static class BehMerge
    {
        // Semantic dump: one line per named object (plus root), pointer slots replaced by descriptors.
        public static List<string> Semantic(TagFile t, bool skipInternalId)
        {
            var h = new TagHelper(t);
            int clipT = t.TypeNames.IndexOf("hkbClipGenerator");
            int aiOff = h.MemberOffset(clipT, "animationInternalId");
            // patch offsets per item
            var ptrs = new Dictionary<int, List<int>>();
            foreach (var kv in h.PatchType)
            {
                int it = h.ItemAt(kv.Key);
                List<int> l; if (!ptrs.TryGetValue(it, out l)) ptrs[it] = l = new List<int>();
                l.Add(kv.Key);
            }
            var memo = new Dictionary<int, string>();
            Func<int, bool, string> desc = null;
            desc = (item, top) =>
            {
                if (item == 0) return "null";
                var x = t.Items[item];
                string tn = h.Canon(x.Type);
                if (tn == "char") return "\"" + h.Str(item) + "\"";
                string nm = h.ObjName(item);
                if (!top && nm != null) return "@" + tn + ":" + nm;
                string m; if (!top && memo.TryGetValue(item, out m)) return m;
                int size = h.ItemSize(item);
                byte[] raw = new byte[size];
                Buffer.BlockCopy(t.Data, x.Offset, raw, 0, size);
                if (skipInternalId && x.Type == clipT) { raw[aiOff] = 0; raw[aiOff + 1] = 0; }
                var sb = new StringBuilder();
                sb.Append(tn + "x" + x.Count + "{");
                List<int> pl;
                var subs = new List<string>();
                if (ptrs.TryGetValue(item, out pl))
                    foreach (int po in pl.OrderBy(z => z))
                    {
                        int rel = po - x.Offset;
                        int target = h.Ptr(po);
                        for (int z = 0; z < 8 && rel + z < raw.Length; z++) raw[rel + z] = 0;
                        subs.Add(rel + ":" + desc(target, false));
                    }
                sb.Append(Convert.ToBase64String(System.Security.Cryptography.MD5.Create().ComputeHash(raw)));
                sb.Append("|" + string.Join(";", subs) + "}");
                string r = sb.ToString();
                if (!top) memo[item] = r;
                return r;
            };
            var lines = new List<string>();
            for (int i = 1; i < t.Items.Count; i++)
            {
                string nm = h.ObjName(i);
                if (nm == null && i != 1) continue;
                lines.Add(h.Canon(t.Items[i].Type) + ":" + nm + " = " + desc(i, true));
            }
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        public static byte[] Merge(TagFile B, TagFile W, TagFile K, TextWriter log)
        {
            var hb = new TagHelper(B); var hw = new TagHelper(W); var hk = new TagHelper(K);
            int clipK = K.TypeNames.IndexOf("hkbClipGenerator"), clipW = W.TypeNames.IndexOf("hkbClipGenerator"), clipB = B.TypeNames.IndexOf("hkbClipGenerator");
            var baseNames = new HashSet<string>();
            for (int i = 1; i < B.Items.Count; i++) if (B.Items[i].Type == clipB) baseNames.Add(hb.ObjName(i));
            var wNames = new HashSet<string>();
            int maxIdW = 0;
            int aiK = hk.MemberOffset(clipK, "animationInternalId"), aiW = hw.MemberOffset(clipW, "animationInternalId"), aiB = hb.MemberOffset(clipB, "animationInternalId");
            int maxIdB = 0;
            for (int i = 1; i < B.Items.Count; i++) if (B.Items[i].Type == clipB) maxIdB = Math.Max(maxIdB, BitConverter.ToInt16(B.Data, B.Items[i].Offset + aiB));
            for (int i = 1; i < W.Items.Count; i++) if (W.Items[i].Type == clipW) { wNames.Add(hw.ObjName(i)); maxIdW = Math.Max(maxIdW, BitConverter.ToInt16(W.Data, W.Items[i].Offset + aiW)); }
            var newClips = new List<int>();
            for (int i = 1; i < K.Items.Count; i++)
                if (K.Items[i].Type == clipK && !baseNames.Contains(hk.ObjName(i)))
                {
                    if (wNames.Contains(hk.ObjName(i))) throw new Exception("clip name collision " + hk.ObjName(i));
                    newClips.Add(i);
                }
            log.WriteLine("new clips in source: " + newClips.Count + ", base max internal id " + maxIdB + ", target max " + maxIdW);
            // remap internal ids of new animations
            var idMap = new Dictionary<int, int>();
            int nextId = maxIdW + 1;
            foreach (int c in newClips.OrderBy(c => BitConverter.ToInt16(K.Data, K.Items[c].Offset + aiK)))
            {
                int old = BitConverter.ToInt16(K.Data, K.Items[c].Offset + aiK);
                if (old <= maxIdB) continue; // existing animation, keep id
                if (!idMap.ContainsKey(old)) idMap[old] = nextId++;
            }
            var canonW = hw.CanonIndex();
            var data = new MemoryStream();
            data.Write(W.Data, 0, W.Data.Length);
            var patchAdd = new Dictionary<int, List<int>>(); // W patch type -> offsets
            var copied = new Dictionary<int, int>(); // K item -> W item
            Func<int, int> mapType = kt =>
            {
                string c = hk.Canon(kt); int wt;
                if (!canonW.TryGetValue(c, out wt)) throw new Exception("type missing in target: " + c);
                return wt;
            };
            Action<int, int> addPatch = (wtype, off) => { List<int> l; if (!patchAdd.TryGetValue(wtype, out l)) patchAdd[wtype] = l = new List<int>(); l.Add(off); };
            // patch offsets per K item
            var kPtrs = new Dictionary<int, List<int>>();
            foreach (var kv in hk.PatchType)
            {
                int it = hk.ItemAt(kv.Key);
                List<int> l; if (!kPtrs.TryGetValue(it, out l)) kPtrs[it] = l = new List<int>();
                l.Add(kv.Key);
            }
            Func<int, int> copy = null;
            copy = ki =>
            {
                if (ki == 0) return 0;
                int wi;
                if (copied.TryGetValue(ki, out wi)) return wi;
                var x = K.Items[ki];
                string nm = hk.ObjName(ki);
                if (x.Type != clipK && nm != null) throw new Exception("deep copy would duplicate named object " + nm);
                int size = hk.ItemSize(ki);
                while (data.Length % 16 != 0) data.WriteByte(0);
                int off = (int)data.Length;
                byte[] raw = new byte[size];
                Buffer.BlockCopy(K.Data, x.Offset, raw, 0, size);
                data.Write(raw, 0, size);
                wi = W.Items.Count;
                W.Items.Add(new TagItem { TypeFlags = (uint)(x.TypeFlags & 0xFF000000) | (uint)mapType(x.Type), Offset = off, Count = x.Count });
                copied[ki] = wi;
                List<int> pl;
                if (kPtrs.TryGetValue(ki, out pl))
                    foreach (int po in pl)
                    {
                        int target = hk.Ptr(po);
                        int wt = copy(target);
                        int woff = off + (po - x.Offset);
                        byte[] b4 = BitConverter.GetBytes((uint)wt);
                        data.Position = woff; data.Write(b4, 0, 4); data.Position = data.Length;
                        addPatch(mapType(hk.PatchType[po]), woff);
                    }
                if (x.Type == clipK)
                {
                    int old = BitConverter.ToInt16(raw, aiK);
                    int nw; if (idMap.TryGetValue(old, out nw))
                    {
                        byte[] b2 = BitConverter.GetBytes((short)nw);
                        data.Position = off + aiW; data.Write(b2, 0, 2); data.Position = data.Length;
                    }
                }
                return wi;
            };
            // owners: arrays in K referencing new clips, and owner objects of those arrays
            var newSet = new HashSet<int>(newClips);
            var arrays = new HashSet<int>();
            foreach (int c in newClips) foreach (var r in hk.ReferrersOf(c)) arrays.Add(r.Key);
            int ownersDone = 0;
            foreach (int arr in arrays.OrderBy(a => a))
            {
                var owners = hk.ReferrersOf(arr);
                if (owners.Count == 0) { log.WriteLine("  orphan array " + arr + " skipped"); continue; }
                foreach (var ow in owners)
                {
                    int ownerK = ow.Key;
                    string ownerName = hk.ObjName(ownerK);
                    int fieldRel = ow.Value - K.Items[ownerK].Offset;
                    string ownerType = hk.Canon(K.Items[ownerK].Type);
                    if (ownerName == null) throw new Exception("unnamed owner of array " + arr);
                    // find owner in W
                    int ownerW = -1;
                    for (int i = 1; i < W.Items.Count && ownerW < 0; i++)
                        if (W.Items[i].Flags == 0x10 && hw.Canon(W.Items[i].Type) == ownerType && hw.ObjName(i) == ownerName) ownerW = i;
                    if (ownerW < 0) throw new Exception("owner not found in target: " + ownerName);
                    int fieldW = W.Items[ownerW].Offset + fieldRel;
                    if (hw.PatchTypeAt(fieldW) < 0) throw new Exception("field mismatch in " + ownerName);
                    int arrW = hw.Ptr(fieldW);
                    // elements to add: K array elements that are new clips
                    var kElems = new List<int>();
                    var kx = K.Items[arr];
                    for (int e = 0; e < kx.Count; e++) kElems.Add(hk.Ptr(kx.Offset + 8 * e));
                    var add = kElems.Where(e => newSet.Contains(e)).ToList();
                    // sanity: every other K element must exist (by name) in W array
                    var wx = W.Items[arrW];
                    var wElems = new List<int>();
                    for (int e = 0; e < wx.Count; e++) wElems.Add(hw.Ptr(wx.Offset + 8 * e));
                    var wElemNames = new HashSet<string>(wElems.Select(e => hw.ObjName(e)));
                    foreach (var e in kElems) if (!newSet.Contains(e) && !wElemNames.Contains(hk.ObjName(e))) log.WriteLine("  WARN " + ownerName + ": source element " + hk.ObjName(e) + " not in target array");
                    var newElems = new List<int>(wElems);
                    foreach (var e in add) newElems.Add(copy(e));
                    // new array item
                    while (data.Length % 16 != 0) data.WriteByte(0);
                    int aoff = (int)data.Length;
                    int elemPatchType = hw.PatchTypeAt(wx.Offset);
                    if (elemPatchType < 0) elemPatchType = mapType(hk.PatchType[kx.Offset]);
                    foreach (var e in newElems) { data.Write(BitConverter.GetBytes((ulong)(uint)e), 0, 8); }
                    for (int e = 0; e < newElems.Count; e++) addPatch(elemPatchType, aoff + 8 * e);
                    int newArr = W.Items.Count;
                    W.Items.Add(new TagItem { TypeFlags = wx.TypeFlags, Offset = aoff, Count = newElems.Count });
                    byte[] b = BitConverter.GetBytes((uint)newArr);
                    data.Position = fieldW; data.Write(b, 0, 4); data.Position = data.Length;
                    // the old array item stays as an unreferenced (orphan) item: valid data, simply unused
                    ownersDone++;
                    log.WriteLine("  " + ownerName + ": " + wElems.Count + " -> " + newElems.Count + " generators (+" + add.Count + ")");
                }
            }
            foreach (int c in newClips) if (!copied.ContainsKey(c)) log.WriteLine("  WARN clip not attached: " + hk.ObjName(c));
            while (data.Length % 16 != 0) data.WriteByte(0);
            W.Data = data.ToArray();
            foreach (var kv in patchAdd)
            {
                var g = W.Patches.FirstOrDefault(p => p.Key == kv.Key);
                if (g.Value == null) { W.Patches.Add(new KeyValuePair<int, List<int>>(kv.Key, new List<int>())); g = W.Patches.Last(); }
                g.Value.AddRange(kv.Value);
                g.Value.Sort();
            }
            log.WriteLine("owners updated: " + ownersDone + ", items added: " + copied.Count + ", id remap: " + string.Join(",", idMap.Select(kv => kv.Key + "->" + kv.Value)));
            return W.Write();
        }
    }
}
