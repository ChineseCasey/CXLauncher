using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Input;

namespace CXLauncher.Views;

public sealed class LiquidBackdrop : Image
{
    protected override System.Windows.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
    private readonly LiquidEffect liquid = new();
    private readonly LiquidFlowField flow = new();
    private readonly WriteableBitmap flowTexture = new(LiquidFlowField.Width * 2, LiquidFlowField.Height, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] flowPixels = new byte[LiquidFlowField.Width * 2 * LiquidFlowField.Height * 4];
    private readonly Stopwatch clock = new();
    private static readonly HashSet<LiquidBackdrop> active = [];
    private Window? owner;
    private bool invalidShader;
    private Point? lastPointer, pendingPointer;
    private double lastPointerTime, previousTick, accumulator;
    public int ActiveDisturbanceCount => flow.Energy > 0.000001 ? 1 : 0;
    public bool IsRunning { get; private set; }
    public bool UsesGpu => !invalidShader && RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly && RenderCapability.IsPixelShaderVersionSupported(3, 0);
    public double CurrentTime => liquid.Time;
    public static int ActiveBackgroundCount => active.Count;

    static LiquidBackdrop()
    {
        Motion.EnabledProperty.OverrideMetadata(typeof(LiquidBackdrop), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits,
            (d, _) => ((LiquidBackdrop)d).UpdatePlayback()));
    }
    public LiquidBackdrop()
    {
        IsHitTestVisible = false; Focusable = false; Stretch = Stretch.Fill;
        UploadFlow(); liquid.Flow = new ImageBrush(flowTexture) { Stretch = Stretch.Fill };
        Source = RenderFrame(0);
        Loaded += (_, _) => { AttachOwner(); RenderCapability.TierChanged += OnTierChanged; PixelShader.InvalidPixelShaderEncountered += OnInvalidShader; UpdatePlayback(); };
        Unloaded += (_, _) => { Pause(); DetachOwner(); RenderCapability.TierChanged -= OnTierChanged; PixelShader.InvalidPixelShaderEncountered -= OnInvalidShader; };
        IsVisibleChanged += (_, _) => UpdatePlayback();
        SizeChanged += (_, _) => { liquid.Aspect = Math.Clamp(ActualWidth / Math.Max(1, ActualHeight), 0.2, 5); if (!UsesGpu) Source = RenderFrame(CurrentTime); };
    }
    private void AttachOwner()
    {
        DetachOwner(); owner = Window.GetWindow(this);
        if (owner is null) return;
        owner.Activated += OnWindowChanged; owner.Deactivated += OnWindowChanged; owner.StateChanged += OnWindowChanged;
        owner.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(OnPointerMove), true);
        owner.MouseLeave += OnPointerLeave;
    }
    private void DetachOwner()
    {
        if (owner is null) return;
        owner.Activated -= OnWindowChanged; owner.Deactivated -= OnWindowChanged; owner.StateChanged -= OnWindowChanged;
        owner.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(OnPointerMove)); owner.MouseLeave -= OnPointerLeave; owner = null;
    }
    private void OnPointerLeave(object sender, MouseEventArgs e) { lastPointer = null; pendingPointer = null; }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        Stir(new Point(point.X / Math.Max(1, ActualWidth), point.Y / Math.Max(1, ActualHeight)));
    }
    public void Stir(Point point)
    {
        if (!IsRunning) return;
        if (point.X is < 0 or > 1 || point.Y is < 0 or > 1) { lastPointer = null; pendingPointer = null; return; }
        pendingPointer = point;
    }
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds; var dt = Math.Min(0.05, now - previousTick); previousTick = now;
        if (dt <= 0) return;
        liquid.Time += dt;
        if (pendingPointer is { } point)
        {
            if (lastPointer is { } previous && CurrentTime - lastPointerTime < 0.25)
                flow.Stir(previous.X, previous.Y, point.X, point.Y, liquid.Aspect);
            lastPointer = point; lastPointerTime = CurrentTime; pendingPointer = null;
        }
        accumulator += dt;
        while (accumulator >= 1.0 / 60) { flow.Step(1.0 / 60, liquid.Aspect); accumulator -= 1.0 / 60; }
        UploadFlow();
    }
    private void UploadFlow()
    {
        flow.CopyPixels(flowPixels);
        flowTexture.WritePixels(new Int32Rect(0, 0, flowTexture.PixelWidth, flowTexture.PixelHeight), flowPixels, flowTexture.PixelWidth * 4, 0);
    }
    private void OnWindowChanged(object? sender, EventArgs e) => UpdatePlayback();
    private void OnTierChanged(object? sender, EventArgs e) { Pause(); UpdatePlayback(); }
    private void OnInvalidShader(object? sender, EventArgs e) { invalidShader = true; Pause(); Effect = null; Source = RenderFrame(CurrentTime); }
    private void UpdatePlayback()
    {
        Effect = UsesGpu ? liquid : null;
        var run = UsesGpu && Motion.GetEnabled(this) && IsLoaded && IsVisible && owner is { IsActive: true, WindowState: not WindowState.Minimized };
        if (!run) { Pause(); return; }
        if (IsRunning) return;
        clock.Restart(); previousTick = 0;
        CompositionTarget.Rendering += OnFrame;
        IsRunning = true; active.Add(this);
    }
    private void Pause()
    {
        CompositionTarget.Rendering -= OnFrame; clock.Stop();
        IsRunning = false; active.Remove(this);
        lastPointer = null; pendingPointer = null;
    }
    // WPF's bitmap capture cannot execute ps_3_0; sample the same palette and flow on CPU.
    public IDisposable PrepareSnapshot()
    {
        var previousEffect = Effect; var previousSource = Source;
        Effect = null; Source = RenderFrame(CurrentTime);
        return new Snapshot(this, previousEffect, previousSource);
    }
    private sealed class Snapshot(LiquidBackdrop backdrop, Effect? effect, ImageSource? source) : IDisposable
    { public void Dispose() { backdrop.Source = source; backdrop.Effect = effect; } }
    public BitmapSource RenderFrame(double time)
    {
        const int width = 384, height = 256;
        var pixels = new byte[width * height * 4]; var t = time * 0.18;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var u = (x + 0.5) / width; var v = (y + 0.5) / height;
            var displacement = flow.SampleOffset(u, v);
            var px = (u + displacement.X - 0.5) * liquid.Aspect * 2.4; var py = (v + displacement.Y - 0.5) * 2.4;
            for (var k = 1; k <= 3; k++)
            {
                var wx = Math.Sin(py * 1.65 + k * 1.2 + t * 0.7); var wy = Math.Cos(px * 1.45 - k * 0.9 - t * 0.6);
                px += wx * (0.65 / k); py += wy * (0.65 / k);
            }
            var phase = px * 1.6 + py * 1.1 + 0.65 * Math.Sin(py * 1.8 - px * 0.7 + t * 0.3);
            var band = 0.5 + 0.5 * Math.Sin(phase);
            var blend = Smooth(0.06, 0.94, band);
            var sheen = Math.Pow(0.5 + 0.5 * Math.Sin(phase + 0.55), 14) * 0.22;
            var shade = 0.96 - 0.3 * ((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5));
            var offset = (y * width + x) * 4;
            pixels[offset + 2] = Channel(0.065, 0.46, 0.76);
            pixels[offset + 1] = Channel(0.072, 0.49, 0.79);
            pixels[offset] = Channel(0.085, 0.535, 0.835); pixels[offset + 3] = 255;
            byte Channel(double a, double b, double shine) => (byte)Math.Clamp(Math.Round(Mix(Mix(a, b, blend), shine, sheen) * shade * 255), 0, 255);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }
    private static double Mix(double a, double b, double t) => a + (b - a) * t;
    private static double Smooth(double a, double b, double value) { var t = Math.Clamp((value - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
}
