using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CXLauncher.Views;

// GSAP's transform/opacity, overwrite and cleanup rules, implemented with native WPF clocks.
public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits,
            (d, e) => { if (!(bool)e.NewValue && d is FrameworkElement element) Cancel(element); }));
    public static readonly DependencyProperty RootProperty = DependencyProperty.RegisterAttached(
        "Root", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRootChanged));
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached(
        "Feedback", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFeedbackChanged));
    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(double), typeof(Motion), new PropertyMetadata(0.985));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(Motion));
    private static readonly DependencyProperty FeedbackStateProperty = DependencyProperty.RegisterAttached("FeedbackState", typeof(FeedbackState), typeof(Motion));
    private static readonly HashSet<State> active = [];
    public static int ActiveAnimationCount => active.Sum(state => state.Runs.Count);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetRoot(DependencyObject element) => (bool)element.GetValue(RootProperty);
    public static void SetRoot(DependencyObject element, bool value) => element.SetValue(RootProperty, value);
    public static bool GetFeedback(DependencyObject element) => (bool)element.GetValue(FeedbackProperty);
    public static void SetFeedback(DependencyObject element, bool value) => element.SetValue(FeedbackProperty, value);
    public static double GetPressScale(DependencyObject element) => (double)element.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject element, double value) => element.SetValue(PressScaleProperty, value);

    public static void Reveal(FrameworkElement element, double x = 0, double y = 6, int duration = 180, int delay = 0)
    {
        var state = GetState(element);
        state.OwnsOpacity = true;
        if (!CanAnimate(element)) { state.Cancel(); return; }
        // Continue from the rendered values when an entrance is interrupted.
        if (state.Runs.Count == 0) { element.Opacity = 0.72; state.Translation.X = x; state.Translation.Y = y; }
        state.To(element, UIElement.OpacityProperty, 1, duration, delay);
        state.To(state.Translation, TranslateTransform.XProperty, 0, duration, delay);
        state.To(state.Translation, TranslateTransform.YProperty, 0, duration, delay);
    }

    public static void Fade(FrameworkElement element, int duration = 140)
    {
        var state = GetState(element);
        state.OwnsOpacity = true;
        if (!CanAnimate(element)) { state.Cancel(); return; }
        if (!state.Runs.ContainsKey((element, UIElement.OpacityProperty))) element.Opacity = 0.65;
        state.To(element, UIElement.OpacityProperty, 1, duration);
    }

    public static void Scale(FrameworkElement element, double value, int duration = 140)
    {
        var state = GetState(element);
        if (!CanAnimate(element)) { state.Cancel(); return; }
        state.To(state.Scaling, ScaleTransform.ScaleXProperty, value, duration);
        state.To(state.Scaling, ScaleTransform.ScaleYProperty, value, duration);
    }

    public static void Move(FrameworkElement element, double x = 0, double y = 0, int duration = 180)
    {
        var state = GetState(element);
        if (!CanAnimate(element)) { state.Cancel(); return; }
        state.To(state.Translation, TranslateTransform.XProperty, x, duration);
        state.To(state.Translation, TranslateTransform.YProperty, y, duration);
    }

    public static void Cancel(FrameworkElement element) => (element.GetValue(StateProperty) as State)?.Cancel();
    public static void CancelAll(Window window)
    {
        foreach (var state in active.Where(s => Window.GetWindow(s.Element) == window).ToArray()) state.Cancel();
    }

    private static bool CanAnimate(FrameworkElement element) => GetEnabled(element) && element.IsLoaded && element.IsVisible
        && Window.GetWindow(element)?.WindowState != WindowState.Minimized;
    private static State GetState(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is State state) return state;
        state = new(element); element.SetValue(StateProperty, state); return state;
    }

    private static void OnRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        if ((bool)e.NewValue) { window.Loaded += OnWindowLoaded; window.Unloaded += OnWindowUnloaded; window.StateChanged += OnWindowStateChanged; }
        else { window.Loaded -= OnWindowLoaded; window.Unloaded -= OnWindowUnloaded; window.StateChanged -= OnWindowStateChanged; OnWindowUnloaded(window, new()); }
    }
    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;
        SystemParameters.StaticPropertyChanged += OnSystemPolicyChanged;
        UpdateSystemPolicy(window);
    }
    private static void OnWindowUnloaded(object sender, RoutedEventArgs e)
    { SystemParameters.StaticPropertyChanged -= OnSystemPolicyChanged; CancelAll((Window)sender); }
    private static void OnSystemPolicyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        if (Application.Current is not { } app) return;
        app.Dispatcher.InvokeAsync(() => { foreach (Window window in app.Windows) if (GetRoot(window)) UpdateSystemPolicy(window); });
    }
    private static void UpdateSystemPolicy(Window window) => SetEnabled(window, SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast);
    private static void OnWindowStateChanged(object? sender, EventArgs e)
    { if (sender is Window { WindowState: WindowState.Minimized } window) CancelAll(window); }

    private static void OnFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        if ((bool)e.NewValue)
        {
            var feedback = new FeedbackState(button); button.SetValue(FeedbackStateProperty, feedback);
            button.Loaded += feedback.OnLoaded; button.Unloaded += feedback.OnUnloaded;
            if (button.IsLoaded) feedback.OnLoaded(button, new());
        }
        else if (button.GetValue(FeedbackStateProperty) is FeedbackState feedback)
        {
            feedback.OnUnloaded(button, new()); button.Loaded -= feedback.OnLoaded; button.Unloaded -= feedback.OnUnloaded;
            button.ClearValue(FeedbackStateProperty);
        }
    }

    private sealed class FeedbackState(Button button)
    {
        private readonly DependencyPropertyDescriptor pressed = DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(Button));
        private FrameworkElement? frame;
        public void OnLoaded(object sender, RoutedEventArgs e)
        {
            OnUnloaded(sender, e);
            button.ApplyTemplate(); frame = button.Template?.FindName("Frame", button) as FrameworkElement;
            pressed.AddValueChanged(button, Update);
            button.IsEnabledChanged += OnEnabledChanged;
        }
        public void OnUnloaded(object sender, RoutedEventArgs e)
        { pressed.RemoveValueChanged(button, Update); button.IsEnabledChanged -= OnEnabledChanged; if (frame is not null) Cancel(frame); frame = null; }
        private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update(sender, EventArgs.Empty);
        private void Update(object? sender, EventArgs e)
        { if (frame is not null) Scale(frame, button.IsEnabled && button.IsPressed ? GetPressScale(button) : 1, button.IsPressed ? 85 : 140); }
    }

    private sealed class State
    {
        public FrameworkElement Element { get; }
        public ScaleTransform Scaling { get; } = new();
        public TranslateTransform Translation { get; } = new();
        public Dictionary<(DependencyObject Target, DependencyProperty Property), DoubleAnimation> Runs { get; } = [];
        public bool OwnsOpacity { get; set; }
        public State(FrameworkElement element)
        {
            Element = element;
            var group = new TransformGroup();
            if (element.RenderTransform != Transform.Identity && element.RenderTransform is { } original) group.Children.Add(original);
            group.Children.Add(Scaling); group.Children.Add(Translation);
            element.RenderTransform = group; element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.Unloaded += (_, _) => Cancel();
            element.IsVisibleChanged += (_, _) => { if (!element.IsVisible) Cancel(); };
        }
        public void To(DependencyObject target, DependencyProperty property, double value, int duration, int delay = 0)
        {
            var from = (double)target.GetValue(property);
            var key = (target, property);
            RemoveClock(target, property); Runs.Remove(key);
            target.SetValue(property, value);
            if (Math.Abs(from - value) < 0.0001) { if (Runs.Count == 0) active.Remove(this); return; }
            var animation = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(duration))
            { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            Runs[key] = animation; active.Add(this);
            animation.Completed += (_, _) =>
            {
                if (!Runs.TryGetValue(key, out var current) || current != animation) return;
                RemoveClock(target, property); Runs.Remove(key); if (Runs.Count == 0) active.Remove(this);
            };
            BeginClock(target, property, animation);
        }
        public void Cancel()
        {
            foreach (var key in Runs.Keys.ToArray()) RemoveClock(key.Target, key.Property);
            Runs.Clear(); active.Remove(this);
            if (OwnsOpacity) Element.Opacity = 1;
            Scaling.ScaleX = Scaling.ScaleY = 1; Translation.X = Translation.Y = 0;
        }
        private static void BeginClock(DependencyObject target, DependencyProperty property, AnimationTimeline? animation)
        {
            if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            else ((Animatable)target).BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
        private static void RemoveClock(DependencyObject target, DependencyProperty property) => BeginClock(target, property, null);
    }
}
