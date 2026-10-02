using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CXLauncher.Views;

// A single foreground light band, plus one-shot hover reflections on realized cards.
public sealed class GlassSurface : ContentControl
{
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(nameof(Interactive), typeof(bool), typeof(GlassSurface), new PropertyMetadata(false));
    public static readonly DependencyProperty FlowingProperty = DependencyProperty.Register(nameof(Flowing), typeof(bool), typeof(GlassSurface), new PropertyMetadata(false, (d, _) => ((GlassSurface)d).UpdateFlow()));
    private static readonly HashSet<GlassSurface> active = [];
    private Window? owner;
    private FrameworkElement? chrome, sheen, edge, flow;
    private readonly TranslateTransform sweep = new(), drift = new();
    private int sweepVersion;
    private bool sweeping, flowing;
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }
    public bool Flowing { get => (bool)GetValue(FlowingProperty); set => SetValue(FlowingProperty, value); }
    public bool IsSweeping => sweeping;
    public static int ActiveEffectCount => active.Sum(surface => (surface.sweeping ? 1 : 0) + (surface.flowing ? 1 : 0));

    static GlassSurface()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(typeof(GlassSurface)));
        Motion.EnabledProperty.OverrideMetadata(typeof(GlassSurface), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits,
            (d, _) => { var surface = (GlassSurface)d; surface.StopReflection(); surface.UpdateFlow(); }));
    }
    public GlassSurface()
    {
        Focusable = false;
        Loaded += (_, _) => { AttachOwner(); UpdateFlow(); };
        Unloaded += (_, _) => { StopEffects(); DetachOwner(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopEffects(); else UpdateFlow(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) StopEffects(); else UpdateFlow(); };
        DataContextChanged += (_, _) => { StopReflection(); Motion.Cancel(this); };
        SizeChanged += (_, _) => UpdateClip();
        MouseEnter += (_, _) => Reflect();
        MouseLeave += (_, _) => { StopReflection(); if (Interactive) Motion.Move(this, y: 0); };
    }
    public override void OnApplyTemplate()
    {
        StopEffects(); base.OnApplyTemplate();
        chrome = GetTemplateChild("PART_Chrome") as FrameworkElement;
        sheen = GetTemplateChild("PART_Sheen") as FrameworkElement;
        edge = GetTemplateChild("PART_Edge") as FrameworkElement;
        flow = GetTemplateChild("PART_Flow") as FrameworkElement;
        if (sheen is not null) { sheen.RenderTransform = sweep; sheen.Opacity = 0; }
        if (flow is Border { Background: LinearGradientBrush brush } line)
        { var instance = brush.Clone(); instance.RelativeTransform = drift; line.Background = instance; }
        UpdateClip(); UpdateFlow();
    }
    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    { base.OnIsKeyboardFocusWithinChanged(e); if ((bool)e.NewValue) Reflect(); else if (!IsMouseOver) { StopReflection(); Motion.Move(this, y: 0); } }
    private bool CanRun => Motion.GetEnabled(this) && IsLoaded && IsVisible && IsEnabled && owner is { IsActive: true, WindowState: not WindowState.Minimized };
    private void UpdateClip()
    { if (chrome is not null) chrome.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), 16, 16); }
    private void AttachOwner()
    {
        DetachOwner(); owner = Window.GetWindow(this);
        if (owner is null) return;
        owner.Activated += OnActivated; owner.Deactivated += OnDeactivated; owner.StateChanged += OnStateChanged;
    }
    private void DetachOwner()
    {
        if (owner is null) return;
        owner.Activated -= OnActivated; owner.Deactivated -= OnDeactivated; owner.StateChanged -= OnStateChanged; owner = null;
    }
    private void OnActivated(object? sender, EventArgs e) => UpdateFlow();
    private void OnDeactivated(object? sender, EventArgs e) => StopEffects();
    private void OnStateChanged(object? sender, EventArgs e) { if (!CanRun) StopEffects(); else UpdateFlow(); }
    private void Reflect()
    {
        if (!Interactive || !CanRun || sheen is null) return;
        StopReflection(); Motion.Move(this, y: -5, duration: 240);
        if (edge is not null) edge.Opacity = 1;
        sheen.Opacity = 1; sweeping = true; active.Add(this);
        var version = ++sweepVersion;
        var animation = new DoubleAnimation(-ActualWidth, ActualWidth, TimeSpan.FromMilliseconds(700)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { if (version == sweepVersion) StopReflection(); };
        sweep.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private void UpdateFlow()
    {
        if (flow is null) return;
        flow.Visibility = Flowing ? Visibility.Visible : Visibility.Collapsed;
        if (!Flowing || !CanRun) { StopFlow(); return; }
        if (flowing) return;
        flowing = true; active.Add(this);
        drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-0.9, 0.9, TimeSpan.FromSeconds(3.8)) { RepeatBehavior = RepeatBehavior.Forever }, HandoffBehavior.SnapshotAndReplace);
    }
    private void StopReflection()
    {
        sweepVersion++; sweep.BeginAnimation(TranslateTransform.XProperty, null); sweep.X = 0;
        if (sheen is not null) sheen.Opacity = 0;
        if (edge is not null) edge.Opacity = 0;
        sweeping = false; Track();
    }
    private void StopFlow() { drift.BeginAnimation(TranslateTransform.XProperty, null); drift.X = 0; flowing = false; Track(); }
    private void Track() { if (!sweeping && !flowing) active.Remove(this); }
    private void StopEffects() { StopReflection(); StopFlow(); Motion.Cancel(this); }
}
