namespace BitMagic.BennyBox.Core.Services;

public interface ICredentialProtector
{
    string? Protect(string? plaintext);

    string? Unprotect(string? encrypted);
}
