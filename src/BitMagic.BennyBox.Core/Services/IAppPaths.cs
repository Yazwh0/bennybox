namespace BitMagic.BennyBox.Core.Services;

public interface IAppPaths
{
    string DatabasePath { get; }

    string DownloadsRoot { get; }

    string LogDirectory { get; }

    string LogoCacheDirectory { get; }
}
