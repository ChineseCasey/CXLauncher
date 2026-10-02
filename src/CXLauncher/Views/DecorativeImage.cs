using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace CXLauncher.Views;

// These images accompany readable labels; screen readers use the labels and buttons.
public sealed class DecorativeImage : Image
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
