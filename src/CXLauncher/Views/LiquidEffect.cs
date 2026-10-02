using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CXLauncher.Views;

public sealed class LiquidEffect : ShaderEffect
{
    private static readonly PixelShader shader = LoadShader();
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(LiquidEffect), 0);
    public static readonly DependencyProperty FlowProperty = RegisterPixelShaderSamplerProperty(nameof(Flow), typeof(LiquidEffect), 1, SamplingMode.Bilinear);
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(nameof(Time), typeof(double), typeof(LiquidEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty AspectProperty = DependencyProperty.Register(nameof(Aspect), typeof(double), typeof(LiquidEffect), new UIPropertyMetadata(1.5, PixelShaderConstantCallback(1)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Brush Flow { get => (Brush)GetValue(FlowProperty); set => SetValue(FlowProperty, value); }
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public double Aspect { get => (double)GetValue(AspectProperty); set => SetValue(AspectProperty, value); }
    public LiquidEffect()
    {
        PixelShader = shader;
        foreach (var property in new[] { InputProperty, FlowProperty, TimeProperty, AspectProperty }) UpdateShaderValue(property);
    }
    private static PixelShader LoadShader()
    {
        var result = new PixelShader { UriSource = new Uri("pack://application:,,,/CXLauncher;component/Assets/Shaders/liquid.ps"), ShaderRenderMode = ShaderRenderMode.HardwareOnly };
        result.Freeze(); return result;
    }
}
