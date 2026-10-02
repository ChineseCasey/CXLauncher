namespace CXLauncher.Views;

// A bounded, persistent velocity/displacement field. The pointer deposits momentum
// along a whole segment; advection and pressure projection carry it into a wake.
public sealed class LiquidFlowField
{
    public const int Width = 64, Height = 40;
    private const int Count = Width * Height;
    private float[] vx = new float[Count], vy = new float[Count], dx = new float[Count], dy = new float[Count];
    private float[] nextVx = new float[Count], nextVy = new float[Count], nextDx = new float[Count], nextDy = new float[Count];
    private readonly float[] pressure = new float[Count], divergence = new float[Count];
    public double Energy => vx.Zip(vy, (x, y) => (double)x * x + (double)y * y).Sum() / Count;

    public void Stir(double ax, double ay, double bx, double by, double aspect)
    {
        var moveX = (bx - ax) * aspect; var moveY = by - ay;
        var length = Math.Sqrt(moveX * moveX + moveY * moveY);
        if (length < 0.00001) return;
        var strength = Math.Min(length, 0.12) * 28;
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
        {
            var px = ((double)x / (Width - 1) - ax) * aspect; var py = (double)y / (Height - 1) - ay;
            var along = Math.Clamp((px * moveX + py * moveY) / (length * length), 0, 1);
            px -= along * moveX; py -= along * moveY;
            var weight = Math.Exp(-(px * px + py * py) / 0.008);
            var index = y * Width + x;
            vx[index] = (float)Math.Clamp(vx[index] + moveX / length * strength * weight / aspect, -1.5, 1.5);
            vy[index] = (float)Math.Clamp(vy[index] + moveY / length * strength * weight, -1.5, 1.5);
        }
    }

    public void Step(double dt, double aspect)
    {
        var damping = Math.Exp(-2.4 * dt); var relaxation = Math.Exp(-0.85 * dt);
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
        {
            var i = y * Width + x; var px = x - vx[i] * dt * (Width - 1); var py = y - vy[i] * dt * (Height - 1);
            nextVx[i] = (float)(Sample(vx, px, py) * damping); nextVy[i] = (float)(Sample(vy, px, py) * damping);
        }
        (vx, nextVx) = (nextVx, vx); (vy, nextVy) = (nextVy, vy);
        // Solve pressure in physical coordinates so wide windows retain round eddies.
        var sx = (Width - 1) / aspect; var sy = Height - 1.0;
        Array.Clear(pressure);
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
        {
            var i = y * Width + x;
            divergence[i] = (float)(0.5 * ((vx[i + 1] - vx[i - 1]) * aspect * sx + (vy[i + Width] - vy[i - Width]) * sy));
        }
        for (var iteration = 0; iteration < 10; iteration++)
            for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
            {
                var i = y * Width + x;
                pressure[i] = (float)(((pressure[i - 1] + pressure[i + 1]) * sx * sx + (pressure[i - Width] + pressure[i + Width]) * sy * sy - divergence[i]) / (2 * (sx * sx + sy * sy)));
            }
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
        {
            var i = y * Width + x;
            vx[i] -= (float)(0.5 * (pressure[i + 1] - pressure[i - 1]) * sx / aspect);
            vy[i] -= (float)(0.5 * (pressure[i + Width] - pressure[i - Width]) * sy);
            var px = x - vx[i] * dt * (Width - 1); var py = y - vy[i] * dt * (Height - 1);
            nextDx[i] = (float)Math.Clamp((Sample(dx, px, py) - vx[i] * dt) * relaxation, -0.45, 0.45);
            nextDy[i] = (float)Math.Clamp((Sample(dy, px, py) - vy[i] * dt) * relaxation, -0.45, 0.45);
        }
        (dx, nextDx) = (nextDx, dx); (dy, nextDy) = (nextDy, dy);
    }

    private static double Sample(float[] field, double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001); y = Math.Clamp(y, 0, Height - 1.001);
        var ix = (int)x; var iy = (int)y; var fx = x - ix; var fy = y - iy; var i = iy * Width + ix;
        return (field[i] * (1 - fx) + field[i + 1] * fx) * (1 - fy) + (field[i + Width] * (1 - fx) + field[i + Width + 1] * fx) * fy;
    }
    public (double X, double Y) SampleOffset(double u, double v) => (Sample(dx, u * (Width - 1), v * (Height - 1)), Sample(dy, u * (Width - 1), v * (Height - 1)));
    public void CopyPixels(byte[] pixels)
    {
        for (var i = 0; i < Count; i++)
        {
            // Two channels per component preserve subtle motion at 16-bit precision.
            var x = (int)Math.Round((dx[i] + 0.5) * 65535); var y = (int)Math.Round((dy[i] + 0.5) * 65535);
            var offset = (i / Width * Width * 2 + i % Width) * 4;
            pixels[offset] = 0; pixels[offset + 1] = (byte)x;
            pixels[offset + 2] = (byte)(x >> 8); pixels[offset + 3] = 255;
            offset += Width * 4;
            pixels[offset] = 0; pixels[offset + 1] = (byte)y;
            pixels[offset + 2] = (byte)(y >> 8); pixels[offset + 3] = 255;
        }
    }
}
