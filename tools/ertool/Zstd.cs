using System;
using System.Runtime.InteropServices;
namespace ErTool {
static class Zstd {
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern UIntPtr ZSTD_decompress(byte[] dst, UIntPtr dstCap, byte[] src, UIntPtr srcSize);
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern UIntPtr ZSTD_compressBound(UIntPtr srcSize);
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern uint ZSTD_isError(UIntPtr code);
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr ZSTD_createCCtx();
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern UIntPtr ZSTD_freeCCtx(IntPtr cctx);
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern UIntPtr ZSTD_CCtx_setParameter(IntPtr cctx, int param, int value);
    [DllImport("libzstd.dll", CallingConvention = CallingConvention.Cdecl)] static extern UIntPtr ZSTD_compress2(IntPtr cctx, byte[] dst, UIntPtr dstCap, byte[] src, UIntPtr srcSize);

    const int c_compressionLevel = 100, c_windowLog = 101, c_contentSizeFlag = 200, c_checksumFlag = 201, c_dictIDFlag = 202;

    public static byte[] Decompress(byte[] src, int rawLen) {
        byte[] dst = new byte[rawLen];
        var r = ZSTD_decompress(dst, (UIntPtr)rawLen, src, (UIntPtr)src.Length);
        if (ZSTD_isError(r) != 0 || (long)r.ToUInt64() != rawLen) throw new Exception("zstd decompress failed");
        return dst;
    }

    static void Set(IntPtr c, int p, int v) { if (ZSTD_isError(ZSTD_CCtx_setParameter(c, p, v)) != 0) throw new Exception("zstd param " + p); }

    // Matches the frames FromSoftware ships: 64 KB window (windowLog 16), no content size, no checksum.
    // The game's decoder crashes on frames with a larger window.
    public static byte[] Compress(byte[] src, int level) {
        ulong cap = ZSTD_compressBound((UIntPtr)src.Length).ToUInt64();
        byte[] dst = new byte[cap];
        IntPtr c = ZSTD_createCCtx();
        try {
            Set(c, c_compressionLevel, level);
            Set(c, c_windowLog, 16);
            Set(c, c_contentSizeFlag, 0);
            Set(c, c_checksumFlag, 0);
            Set(c, c_dictIDFlag, 0);
            var r = ZSTD_compress2(c, dst, (UIntPtr)cap, src, (UIntPtr)src.Length);
            if (ZSTD_isError(r) != 0) throw new Exception("zstd compress failed");
            byte[] o = new byte[r.ToUInt64()]; Buffer.BlockCopy(dst, 0, o, 0, o.Length); return o;
        } finally { ZSTD_freeCCtx(c); }
    }
}}
