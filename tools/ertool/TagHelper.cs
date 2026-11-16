using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class TagHelper
    {
        public TagFile T;
        public Dictionary<int, int> PatchType = new Dictionary<int, int>(); // data offset -> patch type
        public int[] Sorted; // item indices sorted by offset (non-null)
        public Dictionary<int, List<KeyValuePair<int, int>>> Referrers; // target item -> (item, dataOffset)

        public TagHelper(TagFile t)
        {
            T = t;
            foreach (var kv in t.Patches) foreach (var o in kv.Value) PatchType[o] = kv.Key;
            Sorted = Enumerable.Range(1, t.Items.Count - 1).OrderBy(i => t.Items[i].Offset).ToArray();
        }

        public int ItemSize(int i)
        {
            var x = T.Items[i];
            return T.TypeSize(x.Type) * x.Count;
        }

        public int ItemAt(int dataOffset)
        {
            int lo = 0, hi = Sorted.Length - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (T.Items[Sorted[mid]].Offset <= dataOffset) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            // several items can share an offset only if zero-sized; pick last with offset <= dataOffset
            return best >= 0 ? Sorted[best] : -1;
        }

        public int PatchTypeAt(int off) { int t; return PatchType.TryGetValue(off, out t) ? t : -1; }

        public int Ptr(int off) { return (int)BitConverter.ToUInt32(T.Data, off); }

        public void BuildReferrers()
        {
            Referrers = new Dictionary<int, List<KeyValuePair<int, int>>>();
            foreach (var kv in PatchType)
            {
                int target = Ptr(kv.Key);
                if (target == 0) continue;
                List<KeyValuePair<int, int>> l;
                if (!Referrers.TryGetValue(target, out l)) Referrers[target] = l = new List<KeyValuePair<int, int>>();
                l.Add(new KeyValuePair<int, int>(ItemAt(kv.Key), kv.Key));
            }
        }

        public List<KeyValuePair<int, int>> ReferrersOf(int item)
        {
            if (Referrers == null) BuildReferrers();
            List<KeyValuePair<int, int>> l;
            return Referrers.TryGetValue(item, out l) ? l : new List<KeyValuePair<int, int>>();
        }

        public string Str(int item)
        {
            if (item <= 0) return null;
            var x = T.Items[item];
            int n = x.Count; while (n > 0 && T.Data[x.Offset + n - 1] == 0) n--;
            return Encoding.ASCII.GetString(T.Data, x.Offset, n);
        }

        public int MemberOffset(int type, string name)
        {
            foreach (var m in T.AllMembers(type)) if (m.Name == name) return m.Offset;
            return -1;
        }

        public string ObjName(int item)
        {
            var x = T.Items[item];
            int no = MemberOffset(x.Type, "name");
            if (no < 0 || x.Flags != 0x10) return null;
            return Str(Ptr(x.Offset + no));
        }

        public int FindByName(string typeName, string name)
        {
            int ti = T.TypeNames.IndexOf(typeName);
            for (int i = 1; i < T.Items.Count; i++)
                if (T.Items[i].Type == ti && ObjName(i) == name) return i;
            return -1;
        }

        // canonical (numbering-independent) type name
        Dictionary<int, string> canon = new Dictionary<int, string>();
        public string Canon(int ti)
        {
            string s;
            if (canon.TryGetValue(ti, out s)) return s;
            canon[ti] = "?";
            string raw = T.TypeNames[ti];
            if (raw == null) return canon[ti] = "null";
            int lt = raw.IndexOf('<');
            if (lt < 0) return canon[ti] = raw;
            var parts = raw.Substring(lt + 1, raw.Length - lt - 2).Split(',');
            var o = new List<string>();
            foreach (var p in parts)
            {
                var kv = p.Split('=');
                if (kv[0].StartsWith("t")) o.Add(kv[0] + "=" + Canon(int.Parse(kv[1])));
                else o.Add(p);
            }
            return canon[ti] = raw.Substring(0, lt) + "<" + string.Join(",", o) + ">";
        }

        public Dictionary<string, int> CanonIndex()
        {
            var d = new Dictionary<string, int>();
            for (int i = 1; i < T.TypeNames.Count; i++) { string c = Canon(i); if (!d.ContainsKey(c)) d[c] = i; }
            return d;
        }
    }
}
