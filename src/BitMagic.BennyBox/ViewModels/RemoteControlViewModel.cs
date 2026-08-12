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

        if (!NetworkInterface.GetIsNetworkAvailable())
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

        RenderCurrentCode();
        IsActive = true;
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task RegenerateCodeAsync()
    {
        if (!IsActive)
        {
            return;
        }

        await _server.RegenerateAsync();
        RenderCurrentCode();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _server.StopAsync();
        IsActive = false;
        QrCodeImage = null;
        ConnectUrl = null;
    }

    private void RenderCurrentCode()
    {
        var url = $"http://{GetPreferredHost()}:{_server.Port}/?token={_server.Token}";
        ConnectUrl = url;

        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(20);
        using var stream = new MemoryStream(png);
        QrCodeImage = new Bitmap(stream);
    }

    // A raw IP needs no name resolution at all on the phone's end, so it sidesteps mDNS/.local
    // working on the phone's network entirely (client-isolated guest WiFi, Google Wifi mesh points
    // not reflecting mDNS between each other, Android not resolving .local from a plain browser
    // request, etc) - it just has to be the *right* IP. Falls back to a hostname only if no adapter
    // has a gateway at all (unusual - e.g. a machine that's genuinely offline).
    private static string GetPreferredHost() => GetLanIpAddress() ?? GetHostNameFallback();

    // Prefers the adapter with an actual default gateway configured - on a typical dev machine, VPN
    // tunnel adapters (NordLynx/TAP-NordVPN/OpenVPN), Hyper-V/WSL virtual switches, and Bluetooth PAN
    // interfaces are all "Up" with a real unicast IPv4 address but no gateway, while the one adapter
    // actually carrying LAN traffic always has one - a far more reliable signal than "first Up,
    // non-loopback interface" (which used to return whichever VPN/virtual adapter happened to
    // enumerate first, e.g. a NordVPN tunnel IP that's naturally unreachable from a phone on the
    // same LAN). Also excludes link-local (169.254.0.0/16) addresses - an adapter that hasn't
    // actually picked up a DHCP lease yet (e.g. WiFi sitting idle while Ethernet is the active
    // connection) still shows up as "Up" with one of these, but it's not a usable address.
    private static string? GetLanIpAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.GetIPProperties().GatewayAddresses.Count == 0)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address) &&
                    !IsLinkLocal(address.Address))
                {
                    return address.Address.ToString();
                }
            }
        }

        return null;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    // Domain FQDN wins when this machine is actually domain-joined - resolvable via the domain's own
    // DNS for a phone on the same managed network. Otherwise "<hostname>.local" relies on mDNS.
    private static string GetHostNameFallback()
    {
        var hostName = Dns.GetHostName();
        var domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        return string.IsNullOrEmpty(domainName) ? $"{hostName}.local" : $"{hostName}.{domainName}";
    }
}
