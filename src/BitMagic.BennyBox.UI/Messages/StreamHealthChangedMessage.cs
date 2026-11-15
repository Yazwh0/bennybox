namespace BitMagic.BennyBox.Messages;

// A stream has just started or stopped being remembered as "didn't work last time" (StreamHealth).
public sealed record StreamHealthChangedMessage(string StreamUrl);
