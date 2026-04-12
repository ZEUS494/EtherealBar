using System.Windows;
using System.Windows.Media.Animation;

namespace EtherealBar
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Ограничиваем частоту кадров анимаций для снижения нагрузки на GPU
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata(60));
        }
    }
}

