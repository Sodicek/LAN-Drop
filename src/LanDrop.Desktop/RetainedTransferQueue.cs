using System;
using System.Linq;

namespace LanDrop.Desktop;

// In-memory only. ConfirmCurrent is called exclusively after a verified server receipt.
// Retrying snapshots the unconfirmed suffix; ambiguous lost responses remain in the suffix.
sealed class RetainedTransferQueue
{
    private string[] paths = [];
    public string[] Paths => paths.ToArray();
    public string? Recipient { get; private set; }
    public void Begin(string[] sources,string recipient)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrEmpty(recipient);
        paths = sources.ToArray(); Recipient = recipient;
    }
    public void ConfirmCurrent()
    {
        if(paths.Length == 0) throw new InvalidOperationException("No unconfirmed source remains.");
        paths = paths.Skip(1).ToArray();
    }
    public void Clear()
    {
        paths = []; Recipient = null;
    }
}
