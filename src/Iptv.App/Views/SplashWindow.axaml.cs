using Avalonia.Controls;
using Avalonia.Threading;

namespace Iptv.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text) => Dispatcher.UIThread.Post(() => StatusText.Text = text);
}
