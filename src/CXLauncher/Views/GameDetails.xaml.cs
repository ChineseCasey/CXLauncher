using System.Windows.Controls;
namespace CXLauncher.Views;
public partial class GameDetails : UserControl
{
    public GameDetails() => InitializeComponent();
    public void RevealSelection()
    {
        Motion.Reveal(this, x: 14, y: 0);
        Motion.Fade(DetailsCover, 160);
        Motion.Reveal(DetailsBody, y: 6, duration: 150, delay: 30);
    }
}
