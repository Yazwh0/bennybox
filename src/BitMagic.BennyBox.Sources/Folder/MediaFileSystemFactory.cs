using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

public class MediaFileSystemFactory : IMediaFileSystemFactory
{
    private readonly ICredentialProtector _credentialProtector;

    public MediaFileSystemFactory(ICredentialProtector credentialProtector)
    {
        _credentialProtector = credentialProtector;
    }

    public IMediaFileSystem? Create(ProfileSource profile, MediaKind kind) => profile.SourceType switch
    {
        ProfileSourceType.LocalFolder => CreateLocal(profile, kind),
        ProfileSourceType.Sftp => CreateSftp(profile, kind),
        _ => throw new InvalidOperationException($"{profile.SourceType} has no media file system.")
    };

    private static LocalFileSystem? CreateLocal(ProfileSource profile, MediaKind kind)
    {
        var path = kind switch
        {
            MediaKind.Movies => profile.LocalMoviesPath,
            MediaKind.Series => profile.LocalSeriesPath,
            _ => profile.LocalClipsPath
        };
        return string.IsNullOrWhiteSpace(path) ? null : new LocalFileSystem(path);
    }

    private SftpFileSystem? CreateSftp(ProfileSource profile, MediaKind kind)
    {
        var path = kind switch
        {
            MediaKind.Movies => profile.SftpMoviesRemotePath,
            MediaKind.Series => profile.SftpSeriesRemotePath,
            _ => profile.SftpClipsRemotePath
        };
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(profile.SftpHost) || string.IsNullOrWhiteSpace(profile.SftpUsername))
        {
            throw new InvalidOperationException("Profile has no SFTP host/username configured.");
        }

        var password = _credentialProtector.Unprotect(profile.SftpPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no SFTP password configured.");

        return new SftpFileSystem(profile.SftpHost, profile.SftpPort ?? 22, profile.SftpUsername, password, path);
    }
}
