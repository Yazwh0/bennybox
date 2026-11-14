using Android.Speech.Tts;

namespace BitMagic.BennyBox.Android.Services;

// Speaks a short label (a tile's name) with the phone's own text-to-speech voice - used by simple
// picture mode's press-and-hold, for people who can recognise a picture but find reading the name
// hard. Created on first use rather than at start-up: the TTS engine takes a moment to bind, and most
// people never use simple mode.
public static class ReadAloud
{
    private static TextToSpeech? _tts;
    private static bool _ready;
    private static string? _pending;

    public static void Speak(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_tts is null)
        {
            _pending = text;
            _tts = new TextToSpeech(global::Android.App.Application.Context, new InitListener());
            return;
        }

        if (!_ready)
        {
            _pending = text;
            return;
        }

        _tts.Speak(text, QueueMode.Flush, null, "bennybox-read-aloud");
    }

    private sealed class InitListener : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status)
        {
            _ready = status == OperationResult.Success;
            if (_ready && _pending is { } text)
            {
                _pending = null;
                _tts!.Speak(text, QueueMode.Flush, null, "bennybox-read-aloud");
            }
        }
    }
}
