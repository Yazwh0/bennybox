namespace BitMagic.BennyBox.Messages;

// Broadcast whenever a movie/episode's watched status changes somewhere the current page's own
// bound ViewModel instances wouldn't otherwise find out about it - most importantly PlayerViewModel's
// auto-mark-on-completion, which only has a content key/profile Id to work with, not a reference back
// to whichever MovieListItemViewModel/EpisodeListItemViewModel is currently on screen for it.
public sealed class WatchedStatusUpdatedMessage;
