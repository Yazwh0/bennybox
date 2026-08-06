#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using BitMagic.BennyBox.ViewModels;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.Debug;

// DEBUG-only local automation surface - lets a local tool (an AI coding agent, a test script, ad-hoc
// curl) drive the running app directly by reading/writing ViewModel state and invoking commands,
// instead of guessing at screen pixel coordinates with a screenshot+click loop. Binds to 127.0.0.1
// only, so it is never reachable from the network. This entire file is behind #if DEBUG, so it does
// not exist at all in a Release build - there is no code path, port, or attack surface to disable.
//
// Routes (all under http://127.0.0.1:{Port}/):
//   GET  /                    - lists available view model names and routes
//   GET  /state                - every view model's public property values
//   GET  /state/{vm}            - one view model's public property values
//   POST /navigate              body {"page":"Settings"}                    - MainWindowViewModel.NavigateCommand
//   POST /vm/{vm}/set            body {"property":"X","value":"Y"}          - set a public writable property
//   POST /vm/{vm}/invoke          body {"command":"X","parameter":"Y"?}     - execute an ICommand property
public sealed class DebugRemoteControlServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, object> _viewModels;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public DebugRemoteControlServer(MainWindowViewModel mainWindowViewModel, ILogger logger, int port = 47811)
    {
        _logger = logger;
        Port = port;
        _viewModels = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainWindow"] = mainWindowViewModel,
            ["LiveTv"] = mainWindowViewModel.LiveTv,
            ["Guide"] = mainWindowViewModel.Guide,
            ["Series"] = mainWindowViewModel.Series,
            ["Movies"] = mainWindowViewModel.Movies,
            ["Favorites"] = mainWindowViewModel.Favorites,
            ["Settings"] = mainWindowViewModel.Settings,
            ["Player"] = mainWindowViewModel.Player,
            ["RemoteControl"] = mainWindowViewModel.RemoteControl,
        };
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Debug remote control server failed to start on port {Port} - continuing without it", Port);
            return;
        }

        _logger.LogWarning("Debug remote control server listening on http://127.0.0.1:{Port}/ (DEBUG build only - see DebugRemoteControlServer.cs)", Port);
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        (int status, string body) result;
        try
        {
            result = await RouteAsync(ctx.Request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Debug remote control request failed: {Method} {Url}", ctx.Request.HttpMethod, ctx.Request.Url);
            result = (500, JsonSerializer.Serialize(new { error = ex.Message }));
        }

        try
        {
            ctx.Response.StatusCode = result.status;
            ctx.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(result.body);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Debug remote control server failed to write response");
        }
    }

    private async Task<(int status, string body)> RouteAsync(HttpListenerRequest request)
    {
        var path = request.Url?.AbsolutePath.Trim('/') ?? "";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = request.HttpMethod;

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
                    "POST /vm/{vm}/invoke {command,parameter?}"
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
            var doc = JsonDocument.Parse(await ReadBodyAsync(request)).RootElement;
            var page = doc.GetProperty("page").GetString();
            var mainWindowVm = (MainWindowViewModel)_viewModels["MainWindow"];
            await RunOnUiThreadAsync(() => mainWindowVm.NavigateCommand.Execute(page));
            return (200, Ok());
        }

        if (method == "POST" && segments.Length == 3 && segments[0] == "vm" && segments[2] == "set")
        {
            if (!TryGetViewModel(segments[1], out var vm))
            {
                return (404, Error($"Unknown view model '{segments[1]}'"));
            }

            var doc = JsonDocument.Parse(await ReadBodyAsync(request)).RootElement;
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

            var doc = JsonDocument.Parse(await ReadBodyAsync(request)).RootElement;
            var commandName = doc.GetProperty("command").GetString()!;
            var prop = vm.GetType().GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(vm) is not ICommand command)
            {
                return (400, Error($"Command '{commandName}' not found on '{segments[1]}'"));
            }

            object? parameter = doc.TryGetProperty("parameter", out var paramEl) && paramEl.ValueKind != JsonValueKind.Null
                ? paramEl.GetString()
                : null;
            await RunOnUiThreadAsync(() => command.Execute(parameter));
            return (200, Ok());
        }

        return (404, Error("Unknown route - GET / for a list"));
    }

    private bool TryGetViewModel(string name, out object viewModel) =>
        _viewModels.TryGetValue(name, out viewModel!);

    // Best-effort shallow dump: primitives/strings/enums/dates serialize as their value, ICommand
    // properties as a marker so callers know what's invokable, collections as a count (not their
    // contents - a channel list can be 15k+ items), everything else as just its type name. Deep
    // graphs and circular references (ViewModels reference each other, e.g. MainWindow -> Player)
    // make a full recursive dump both slow and liable to stack overflow, so this deliberately never
    // recurses past one level.
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

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string Ok() => JsonSerializer.Serialize(new { ok = true });
    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
    }
}
#endif
