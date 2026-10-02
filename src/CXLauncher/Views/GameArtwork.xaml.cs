using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CXLauncher.ViewModels;

namespace CXLauncher.Views;

public partial class GameArtwork : UserControl
{
    private CancellationTokenSource? cancellation;
    private GameItemViewModel? active;
    public GameArtwork()
    {
        InitializeComponent();
        MouseEnter += (_, _) => { if (ActualHeight >= 100 && CoverImage.Source is not null) Motion.Scale(CoverImage, 1.025, 160); };
        MouseLeave += (_, _) => Motion.Scale(CoverImage, 1, 180);
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 11, 11);
            PlaceholderLabel.Visibility = ActualHeight >= 140 ? Visibility.Visible : Visibility.Collapsed;
            PlaceholderGlyph.Width = ActualHeight < 100 ? 32 : 54;
            PlaceholderGlyph.Height = ActualHeight < 100 ? 32 : 54;
        };
    }
    private async void Start()
    {
        Stop();
        if (!IsLoaded || !IsVisible || DataContext is not GameItemViewModel game) return;
        active = game; active.RetainVisual(); active.VisualChanged += Start; cancellation = new();
        await game.LoadVisualAsync(cancellation.Token);
    }
    private void Stop()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        if (active is not null) { active.VisualChanged -= Start; active.ReleaseVisual(); active = null; }
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => Start();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Stop();
    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) Start(); }
    private void OnCoverUpdated(object sender, DataTransferEventArgs e)
    { if (e.Property == Image.SourceProperty && CoverImage.Source is not null && CoverImage.IsVisible) Motion.Fade(CoverImage); }
}
