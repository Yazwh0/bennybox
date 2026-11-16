namespace BitMagic.BennyBox.Android.Views;

// One chip in Live TV's favourite category bar (see LiveTvView.axaml).
public sealed record FavouriteCategoryChip(string Name, SimplePicture Picture, bool IsSelected);
