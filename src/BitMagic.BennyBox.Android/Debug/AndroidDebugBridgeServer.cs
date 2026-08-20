#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using BitMagic.BennyBox.Android.ViewModels;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.Android.Debug;

// Android equivalent of the desktop's DebugRemoteControlServer (see
// src/BitMagic.BennyBox/Debug/DebugRemoteControlServer.cs) - same purpose, same route shape, same
// reflection-based state/invoke logic, ported verbatim where possible. The one thing that can't be
// reused as-is is the HTTP transport: System.Net.HttpListener wraps http.sys and is Windows-only, so
// this hand-rolls a minimal HTTP/1.1 server over a raw TcpListener instead (fine for a DEBUG-only
// tool with tiny, trusted, same-host requests - it does not need to be a general-purpose HTTP stack).
//
// Reachability: the app binds 127.0.0.1 *inside* the emulator/device, which is not the same network
// namespace as the host machine - use `adb forward tcp:{port} tcp:{port}` to reach it from the host.
//
// No /screenshot route here (unlike desktop): adb already has a reliable, non-focus-stealing
// screenshot mechanism (`adb shell screencap`) that doesn't need an in-process off-screen render to
// work around - that workaround only exists on desktop because Win32 screen capture requires
// stealing window focus. Real pixel verification should keep using `adb shell screencap`; this
// bridge exists to solve the *other* problem: reliably reading state and invoking commands without
// guessing screen-pixel tap coordinates (which, on top of being fragile, can crash the app - see
// PLAN.md's write-up of the `uiautomator dump` / InteropAutomationPeer accessibility crash).
//
// Routes (all under http://127.0.0.1:{Port}/):
//   GET  /                    - lists available view model names and routes
//   GET  /state                - every view model's public property values
//   GET  /state/{vm}            - one view model's public property values
//   POST /navigate              body {"page":"Settings"}                    - AndroidShellViewModel.NavigateCommand
//   POST /vm/{vm}/set            body {"property":"X","value":"Y"}          - set a public writable property
//   POST /vm/{vm}/invoke          body {"command":"X","parameter":"Y"?}     - execute an ICommand property
//                                 or   {"command":"X","parameterFromCollection":{"property":"Rows","match":"Name","equals":"Y"}}
//                                      - same, but the parameter is looked up from another collection
//                                        property on this vm (first item whose named property equals
//                                        the given string) - for commands wanting a row ViewModel
public sealed class AndroidDebugBridgeServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Dictionary<string, object> _viewModels;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public AndroidDebugBridgeServer(AndroidShellViewModel shellViewModel, ILogger logger, int port = 47812)
    {
        _logger = logger;
        Port = port;
        _viewModels = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Shell"] = shellViewModel,
            ["Search"] = shellViewModel.Search,
            ["LiveTv"] = shellViewModel.LiveTv,
            ["Guide"] = shellViewModel.Guide,
            ["Series"] = shellViewModel.Series,
            ["Movies"] = shellViewModel.Movies,
            ["Clips"] = shellViewModel.Clips,
            ["Downloads"] = shellViewModel.Downloads,
            ["Favorites"] = shellViewModel.Favorites,
            ["Settings"] = shellViewModel.Settings,
            ["Player"] = shellViewModel.Player,
        };
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Debug bridge failed to start on port {Port} - continuing without it", Port);
            return;
        }

        _logger.LogWarning("Debug bridge listening on http://127.0.0.1:{Port}/ (DEBUG build only - see AndroidDebugBridgeServer.cs)", Port);
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (method, path, bodyBytes) = await ReadRequestAsync(stream);
                var bodyJson = Encoding.UTF8.GetString(bodyBytes);

                (int status, string body) result;
                try
                {
                    result = await RouteAsync(method, path, bodyJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Debug bridge request failed: {Method} {Path}", method, path);
                    result = (500, JsonSerializer.Serialize(new { error = ex.Message }));
                }

                await WriteResponseAsync(stream, result.status, Encoding.UTF8.GetBytes(result.body));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Debug bridge connection failed");
            }
        }
    }

    // Hand-rolled HTTP/1.1 request-line + header + body parse - byte-oriented throughout (not
    // StreamReader) so Content-Length, which is a byte count, lines up with what's actually read;
    // mixing a char-based reader with a byte-counted body read would drift apart for any non-ASCII
    // JSON string (e.g. a channel name used as a parameterFromCollection match value).
    private static async Task<(string method, string path, byte[] body)> ReadRequestAsync(NetworkStream stream)
    {
        var requestLine = await ReadLineAsync(stream) ?? throw new IOException("Empty request");
        var parts = requestLine.Split(' ');
        var method = parts[0];
        var path = parts.Length > 1 ? parts[1] : "/";

        var contentLength = 0;
        string? line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream)))
        {
            var idx = line.IndexOf(':');
            if (idx > 0 && line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line[(idx + 1)..].Trim());
            }
        }

        var bodyBytes = Array.Empty<byte>();
        if (contentLength > 0)
        {
            bodyBytes = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var n = await stream.ReadAsync(bodyBytes.AsMemory(read, contentLength - read));
                if (n == 0)
                {
                    break;
                }

                read += n;
            }
        }

        return (method, path, bodyBytes);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var oneByte = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(oneByte.AsMemory(0, 1));
            if (n == 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (oneByte[0] == (byte)'\n')
            {
                break;
            }

            if (oneByte[0] != (byte)'\r')
            {
                buffer.Add(oneByte[0]);
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, byte[] body)
    {
        var statusText = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "Internal Server Error"
        };
        var header = $"HTTP/1.1 {status} {statusText}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private async Task<(int status, string body)> RouteAsync(string method, string rawPath, string bodyJson)
    {
        var path = rawPath.Split('?')[0].Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (method == "GET" && segments.Length == 0)
        {
            return (200, JsonSerializer.Serialize(new
            {
                viewModels = _viewModels.Keys,
                routes = new[]
                {
                    "GET /state", "GET /state/{vm}",
                    "POST /navigate {page}",
                    "POST /vm/{vm}/set {property,value}",
                    "POST /vm/{vm}/invoke {command,parameter?|parameterFromCollection?}"
                }
            }));
        }

        if (method == "GET" && segments.Length == 1 && segments[0] == "state")
        {
            var all = new Dictionary<string, object?>();
            foreach (var (name, vm) in _viewModels)
            {
                all[name] = await RunOnUiThreadAsync(() => SerializeViewModel(vm));
            }

            return (200, JsonSerializer.Serialize(all, JsonOptions));
        }

        if (method == "GET" && segments.Length == 2 && segments[0] == "state")
        {
            if (!TryGetViewModel(segments[1], out var vm))
            {
                return (404, Error($"Unknown view model '{segments[1]}'"));
            }

            var state = await RunOnUiThreadAsync(() => SerializeViewModel(vm));
            return (200, JsonSerializer.Serialize(state, JsonOptions));
        }

        if (method == "POST" && segments.Length == 1 && segments[0] == "navigate")
        {
            var doc = JsonDocument.Parse(bodyJson).RootElement;
            var page = doc.GetProperty("page").GetString();
            var shellVm = (AndroidShellViewModel)_viewModels["Shell"];
            await RunOnUiThreadAsync(() => shellVm.NavigateCommand.Execute(page));
            return (200, Ok());
        }

        if (method == "POST" && segments.Length == 3 && segments[0] == "vm" && segments[2] == "set")
        {
            if (!TryGetViewModel(segments[1], out var vm))
            {
                return (404, Error($"Unknown view model '{segments[1]}'"));
            }

            var doc = JsonDocument.Parse(bodyJson).RootElement;
            var propName = doc.GetProperty("property").GetString()!;
            var prop = vm.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite)
            {
                return (400, Error($"Property '{propName}' not found or not writable on '{segments[1]}'"));
            }

            var converted = ConvertJsonValue(doc.GetProperty("value"), prop.PropertyType);
            await RunOnUiThreadAsync(() => prop.SetValue(vm, converted));
            return (200, Ok());
        }

        if (method == "POST" && segments.Length == 3 && segments[0] == "vm" && segments[2] == "invoke")
        {
            if (!TryGetViewModel(segments[1], out var vm))
            {
                return (404, Error($"Unknown view model '{segments[1]}'"));
            }

            var doc = JsonDocument.Parse(bodyJson).RootElement;
            var commandName = doc.GetProperty("command").GetString()!;
            var prop = vm.GetType().GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(vm) is not ICommand command)
            {
                return (400, Error($"Command '{commandName}' not found on '{segments[1]}'"));
            }

            object? parameter;
            if (doc.TryGetProperty("parameterFromCollection", out var pfc))
            {
                var collectionPropName = pfc.GetProperty("property").GetString()!;
                var matchPropName = pfc.GetProperty("match").GetString()!;
                var equalsValue = pfc.GetProperty("equals").GetString()!;

                var collectionProp = vm.GetType().GetProperty(collectionPropName, BindingFlags.Public | BindingFlags.Instance);
                if (collectionProp?.GetValue(vm) is not IEnumerable items)
                {
                    return (400, Error($"Collection property '{collectionPropName}' not found on '{segments[1]}'"));
                }

                parameter = await RunOnUiThreadAsync(() => FindMatchingItem(items, matchPropName, equalsValue));
                if (parameter is null)
                {
                    return (404, Error($"No item in '{collectionPropName}' with {matchPropName} == '{equalsValue}'"));
                }
            }
            else
            {
                parameter = doc.TryGetProperty("parameter", out var paramEl) && paramEl.ValueKind != JsonValueKind.Null
                    ? paramEl.GetString()
                    : null;
            }

            await RunOnUiThreadAsync(() => command.Execute(parameter));
            return (200, Ok());
        }

        return (404, Error("Unknown route - GET / for a list"));
    }

    private bool TryGetViewModel(string name, out object viewModel) =>
        _viewModels.TryGetValue(name, out viewModel!);

    private static object? FindMatchingItem(IEnumerable items, string matchPropName, string equalsValue)
    {
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var itemProp = item.GetType().GetProperty(matchPropName, BindingFlags.Public | BindingFlags.Instance);
            if (itemProp?.GetValue(item)?.ToString() == equalsValue)
            {
                return item;
            }
        }

        return null;
    }

    private static Dictionary<string, object?> SerializeViewModel(object vm)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = prop.GetValue(vm);
            }
            catch (Exception ex)
            {
                result[prop.Name] = $"<error reading: {ex.Message}>";
                continue;
            }

            result[prop.Name] = value switch
            {
                null => null,
                ICommand => "<command>",
                string or bool or byte or short or int or long or float or double or decimal or DateTime or DateOnly or TimeOnly or Guid or Enum => value,
                IEnumerable and not string => $"<collection: {CountEnumerable((IEnumerable)value)} item(s)>",
                _ => $"<{value.GetType().Name}>"
            };
        }

        return result;
    }

    private static int CountEnumerable(IEnumerable items)
    {
        var count = 0;
        foreach (var _ in items)
        {
            count++;
        }

        return count;
    }

    private static object? ConvertJsonValue(JsonElement element, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (underlying == typeof(string))
        {
            return element.GetString();
        }
        if (underlying == typeof(bool))
        {
            return element.GetBoolean();
        }
        if (underlying == typeof(int))
        {
            return element.GetInt32();
        }
        if (underlying == typeof(long))
        {
            return element.GetInt64();
        }
        if (underlying == typeof(double))
        {
            return element.GetDouble();
        }
        if (underlying.IsEnum)
        {
            return Enum.Parse(underlying, element.GetString()!, ignoreCase: true);
        }

        throw new NotSupportedException($"Setting a property of type {targetType.Name} isn't supported by the debug bridge");
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<T> action) => await Dispatcher.UIThread.InvokeAsync(action);
    private static async Task RunOnUiThreadAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);

    private static string Ok() => JsonSerializer.Serialize(new { ok = true });
    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
#endif
