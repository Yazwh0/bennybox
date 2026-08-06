using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Media.Imaging;
using BitMagic.BennyBox.RemoteControl;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace BitMagic.BennyBox.ViewModels;

// Owns the QR/session-code UI state for the phone remote control feature - RemoteControlServer does
// the actual HTTP serving, this just starts/stops it on demand and renders the current session's
// connect URL as a scannable QR code (see MainWindow's Remote flyout).
public partial class RemoteControlViewModel : ViewModelBase
{
    private readonly RemoteControlServer _server;
    private readonly ILogger<RemoteControlViewModel> _logger;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private Bitmap? _qrCodeImage;

    [ObservableProperty]
    private string? _connectUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public RemoteControlViewModel(RemoteControlServer server, ILogger<RemoteControlViewModel> logger)
    {
        _server = server;
        _logger = logger;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsActive)
        {
            return;
        }

        var lanAddress = GetLanIpAddress();
        if (lanAddress is null)
        {
            StatusMessage = "No network connection found - connect to WiFi/Ethernet first.";
            return;
        }

        try
        {
            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start remote control server");
            StatusMessage = "Couldn't start the remote control server.";
            return;
        }

        RenderCurrentCode(lanAddress);
        IsActive = true;
        StatusMessage = null;
    }

    [RelayCommand]
    private void RegenerateCode()
    {
        if (!IsActive)
        {
            return;
        }

        _server.Regenerate();
        var lanAddress = GetLanIpAddress();
        if (lanAddress is not null)
        {
            RenderCurrentCode(lanAddress);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _server.StopAsync();
        IsActive = false;
        QrCodeImage = null;
        ConnectUrl = null;
    }

    private void RenderCurrentCode(string lanAddress)
    {
        var url = $"http://{lanAddress}:{_server.Port}/?token={_server.Token}";
        ConnectUrl = url;

        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(20);
        using var stream = new MemoryStream(png);
        QrCodeImage = new Bitmap(stream);
    }

    // Picks the first "real" (non-loopback, non-virtual-adapter-ish) IPv4 address on an interface
    // that's actually up - good enough for a home network where there's usually exactly one relevant
    // adapter; if there are several (e.g. WiFi + a virtual VPN/Hyper-V adapter), this may not always
    // guess the one a phone can actually reach, but it's a reasonable default with the fallback text
    // URL there for the user to double check.
    private static string? GetLanIpAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                {
                    return address.Address.ToString();
                }
            }
        }

        return null;
    }
}
