using Avalonia.Controls;
using Avalonia.Threading;

namespace BitMagic.BennyBox.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text) => Dispatcher.UIThread.Post(() => StatusText.Text = text);
}
