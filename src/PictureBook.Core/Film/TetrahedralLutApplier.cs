using System.Buffers;

namespace PictureBook.Core.Film;

/// <summary>
/// 四面体插值 3D LUT 应用（DaVinci / OCIO 标准做法）。
/// 三线性在对角线上会「抄近路」穿过错误色域，胶片 LUT 必须用四面体。
/// </summary>
public static class TetrahedralLutApplier
{
    /// <param name="src">交错 RGB float32，[0,1]，长度 = width*height*3</param>
    /// <param name="dst">输出缓冲，可与 src 相同则原地</param>
    public static void Apply(float[] src, float[] dst, Lut3D lut, int width, int height,
        bool parallel = true)
    {
        int n = lut.Size;
        float scale = n - 1;
        int sx = 3, sy = n * 3, sz = n * n * 3;
        int last = n - 2;
        var data = lut.Data;

        void Row(int y)
        {
            int o = y * width * 3;
            for (int x = 0; x < width; x++)
            {
                int p = o + x * 3;
                float r = Math.Clamp(src[p], 0f, 1f) * scale;
                float g = Math.Clamp(src[p + 1], 0f, 1f) * scale;
                float b = Math.Clamp(src[p + 2], 0f, 1f) * scale;

                int ix = (int)r, iy = (int)g, iz = (int)b;
                if (ix > last) ix = last;
                if (iy > last) iy = last;
                if (iz > last) iz = last;
                float rx = r - ix, ry = g - iy, rz = b - iz;

                // 按分数大小排序，确定当前点落在立方体的哪个四面体里
                float f0, f1, f2;
                int a0, a1;
                if (rx >= ry)
                {
                    if (ry >= rz) { f0 = rx; f1 = ry; f2 = rz; a0 = 0; a1 = 1; }
                    else if (rx >= rz) { f0 = rx; f1 = rz; f2 = ry; a0 = 0; a1 = 2; }
                    else { f0 = rz; f1 = rx; f2 = ry; a0 = 2; a1 = 0; }
                }
                else
                {
                    if (rz >= ry) { f0 = rz; f1 = ry; f2 = rx; a0 = 2; a1 = 1; }
                    else if (rz >= rx) { f0 = ry; f1 = rz; f2 = rx; a0 = 1; a1 = 2; }
                    else { f0 = ry; f1 = rx; f2 = rz; a0 = 1; a1 = 0; }
                }

                int s0 = a0 switch { 0 => sx, 1 => sy, _ => sz };
                int s1 = a1 switch { 0 => sx, 1 => sy, _ => sz };

                int c0 = (iz * n + iy) * n * 3 + ix * 3;
                int c1 = c0 + s0;
                int c2 = c1 + s1;
                int c3 = c0 + sx + sy + sz;

                float w0 = 1f - f0, w1 = f0 - f1, w2 = f1 - f2, w3 = f2;
                dst[p] = data[c0] * w0 + data[c1] * w1 + data[c2] * w2 + data[c3] * w3;
                dst[p + 1] = data[c0 + 1] * w0 + data[c1 + 1] * w1 + data[c2 + 1] * w2 + data[c3 + 1] * w3;
                dst[p + 2] = data[c0 + 2] * w0 + data[c1 + 2] * w1 + data[c2 + 2] * w2 + data[c3 + 2] * w3;
            }
        }

        if (parallel && height > 8)
        {
            Parallel.For(0, height, Row);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                Row(y);
            }
        }
    }

    /// <summary>租借像素缓冲。24MP float32 ≈ 288MB，必须池化，不能每张 new。</summary>
    public static float[] RentPixels(int pixelCount) => ArrayPool<float>.Shared.Rent(pixelCount * 3);

    public static void ReturnPixels(float[] buffer) => ArrayPool<float>.Shared.Return(buffer);
}
