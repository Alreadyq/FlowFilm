using PictureBook.Core.Film;
using Xunit;

namespace PictureBook.Core.Tests;

public class TetrahedralLutApplierTests
{
    /// <summary>构造恒等 LUT：输出 = 输入。</summary>
    private static Lut3D IdentityLut(int n)
    {
        var lut = Lut3D.Create(n);
        int i = 0;
        for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    lut.Data[i++] = x / (float)(n - 1);
                    lut.Data[i++] = y / (float)(n - 1);
                    lut.Data[i++] = z / (float)(n - 1);
                }

        return lut;
    }

    /// <summary>构造线性增益 LUT：输出 = 输入 × 0.5。</summary>
    private static Lut3D HalfGainLut(int n)
    {
        var lut = IdentityLut(n);
        for (int i = 0; i < lut.Data.Length; i++)
        {
            lut.Data[i] *= 0.5f;
        }

        return lut;
    }

    [Theory]
    [InlineData(33)]
    [InlineData(65)]
    public void IdentityLut_ReturnsInput(int n)
    {
        var src = new float[] { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f, 0.5f, 0.1f, 0.9f, 0.3f };
        var dst = new float[src.Length];
        TetrahedralLutApplier.Apply(src, dst, IdentityLut(n), 3, 1);

        Assert.Equal(src.Length, dst.Length);
        for (int i = 0; i < src.Length; i++)
        {
            Assert.InRange(dst[i], src[i] - 0.02f, src[i] + 0.02f);
        }
    }

    [Theory]
    [InlineData(33)]
    [InlineData(65)]
    public void HalfGainLut_HalvesChannels(int n)
    {
        var src = new float[] { 0.8f, 0.4f, 1.0f, 0.2f, 0.6f, 0.3f };
        var dst = new float[src.Length];
        TetrahedralLutApplier.Apply(src, dst, HalfGainLut(n), 2, 1);

        for (int i = 0; i < src.Length; i++)
        {
            Assert.InRange(dst[i], src[i] * 0.5f - 0.02f, src[i] * 0.5f + 0.02f);
        }
    }

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        var src = new float[] { -0.5f, 2.0f, 0.7f };
        var dst = new float[src.Length];
        TetrahedralLutApplier.Apply(src, dst, HalfGainLut(33), 1, 1);

        Assert.Equal(0f, dst[0], 2f);
        Assert.Equal(0.5f, dst[1], 2f);
    }

    [Fact]
    public void ParallelMatchesSerial()
    {
        var rnd = new Random(7);
        var src = new float[64 * 48 * 3];
        for (int i = 0; i < src.Length; i++)
        {
            src[i] = (float)rnd.NextDouble();
        }

        var lut = HalfGainLut(33);
        var a = new float[src.Length];
        var b = new float[src.Length];
        TetrahedralLutApplier.Apply(src, a, lut, 64, 48, parallel: false);
        TetrahedralLutApplier.Apply(src, b, lut, 64, 48, parallel: true);

        Assert.Equal(a, b);
    }
}

public class PblFormatTests
{
    [Fact]
    public void RoundTrip_PreservesData()
    {
        var lut = Lut3D.Create(8, "spektrafilm by Andrea Volpato · CC BY-SA 4.0");
        var rnd = new Random(1);
        for (int i = 0; i < lut.Data.Length; i++)
        {
            lut.Data[i] = (float)rnd.NextDouble();
        }

        using var ms = new MemoryStream();
        PblFormat.Write(ms, lut);
        ms.Position = 0;
        var back = PblFormat.Read(ms);

        Assert.Equal(lut.Size, back.Size);
        Assert.Equal(lut.Data, back.Data);
        Assert.Equal(lut.Attribution, back.Attribution);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        Assert.Throws<InvalidDataException>(() => PblFormat.Read(new MemoryStream([1, 2, 3, 4, 5, 6])));
    }
}
