using System.Windows;

namespace CXLauncher.Views;
public partial class DataSettingsWindow : Window
{
    public DataSettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { NativeWindow.UseDarkCaption(this); Motion.Reveal((FrameworkElement)Content, y: 8); };
    }
}
