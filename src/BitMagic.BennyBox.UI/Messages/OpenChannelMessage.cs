using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Messages;

// Sent to open a channel's own page (schedule and catch-up, see ChannelScheduleViewModel) from
// somewhere other than Live TV - currently the Guide, by clicking a channel's name. The shell switches
// to Live TV and opens it there.
public sealed record OpenChannelMessage(Channel Channel);
