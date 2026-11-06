using Android.Gms.Tasks;
using Java.Interop;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace BitMagic.BennyBox.Android.Services;

// Google Play services' code scanner: a full-screen camera UI owned by Play services itself, so the
// app needs no CAMERA permission and no camera code of its own. Used by SettingsView's "Scan QR code"
// button to read the setup code desktop shows (see ProfileSetupCode). JavaCast rather than `as` for the
// result - see AndroidCredentialProtector.GetOrCreateKey for why a plain C# cast silently fails.
public static class QrCodeScanner
{
    // The scanned text, or null if the user backed out. Throws if Play services can't scan (missing,
    // or its scanner module couldn't be downloaded) - the caller falls back to pasting the code.
    public static System.Threading.Tasks.Task<string?> ScanAsync()
    {
        var activity = MainActivity.Instance ?? throw new InvalidOperationException("The app isn't in the foreground.");

        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .Build();

        var listener = new ScanListener();
        GmsBarcodeScanning.GetClient(activity, options)
            .StartScan()
            .AddOnSuccessListener(listener)
            .AddOnFailureListener(listener)
            .AddOnCanceledListener(listener);
        return listener.Result.Task;
    }

    private sealed class ScanListener : Java.Lang.Object, IOnSuccessListener, IOnFailureListener, IOnCanceledListener
    {
        public System.Threading.Tasks.TaskCompletionSource<string?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnSuccess(Java.Lang.Object? result) => Result.TrySetResult(result?.JavaCast<Barcode>()?.RawValue);

        public void OnFailure(Java.Lang.Exception e) => Result.TrySetException(new InvalidOperationException(e.Message ?? "Couldn't start the scanner.", e));

        public void OnCanceled() => Result.TrySetResult(null);
    }
}
