using AFMDataClient.Core;
using AlbionDataAvalonia.Network.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Desktop settings adapter for the shared AFM sharing API.</summary>
public sealed class AFMUploader
{
    private readonly PrivateOrderSharingClient sharing;

    public AFMUploader(DesktopClientCore client) => sharing = new(client.Core);

    public Task<PrivateOrderSharesResponse?> GetPrivateOrderSharesAsync(CancellationToken cancellationToken = default)
        => sharing.GetAsync(cancellationToken);

    public Task<SavePrivateOrderSharesResponse?> SavePrivateOrderSharesAsync(IEnumerable<string> sharedUsers, CancellationToken cancellationToken = default)
        => sharing.SaveAsync(sharedUsers, cancellationToken);
}
