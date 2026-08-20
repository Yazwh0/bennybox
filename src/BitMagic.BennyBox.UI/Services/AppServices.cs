namespace BitMagic.BennyBox.UI.Services;

// Both heads' App class exposes its own static IServiceProvider (BitMagic.BennyBox.App.Services,
// BitMagic.BennyBox.Android.App.Services) - this is a second, shared handle to the same instance for
// platform-neutral UI-project code (e.g. ChannelLogoImage) that can't reference either concrete App
// type. Each head sets this alongside its own static Services property at startup.
public static class AppServices
{
    public static IServiceProvider? Current { get; set; }
}
