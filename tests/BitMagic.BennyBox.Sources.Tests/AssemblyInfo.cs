using System.Runtime.Versioning;

// Mirrors src/BitMagic.BennyBox/AssemblyInfo.cs - this test project exercises Windows-only APIs
// (CredentialProtector, XtreamSeriesSource - see their own [SupportedOSPlatform("windows")]) and,
// like the app itself, only ever runs on Windows, so the platform-compatibility analyzer (CA1416)
// doesn't need to flag those calls as unsafe here.
[assembly: SupportedOSPlatform("windows")]
