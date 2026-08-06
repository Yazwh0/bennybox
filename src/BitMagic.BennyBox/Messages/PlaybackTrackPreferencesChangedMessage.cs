namespace BitMagic.BennyBox.Messages;

// Sent by SettingsViewModel whenever the preferred audio/subtitle language is edited, so
// PlayerViewModel (a long-lived singleton, unlike the transient SettingsViewModel) picks up the new
// value immediately instead of only on next app launch.
public sealed class PlaybackTrackPreferencesChangedMessage;
