using AFMDataClient.Core;
using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Keeps credentials in the desktop host while the core owns authenticated requests.</summary>
public sealed class DesktopUploadAuthSession : IUploadAuthSession, IDisposable
{
    private readonly AuthService auth;
    private string? lastAccountId;

    public DesktopUploadAuthSession(AuthService auth)
    {
        this.auth = auth;
        lastAccountId = auth.FirebaseUserId;
        auth.FirebaseUserChanged += OnFirebaseUserChanged;
    }

    public string? AccountId => auth.FirebaseUserId;
    public event Action? AccountChanged;

    public async Task<string?> GetTokenAsync(string expectedAccountId, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!string.Equals(expectedAccountId, AccountId, StringComparison.Ordinal)) return null;
        if (!await auth.EnsureValidTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false)) return null;
        return string.Equals(expectedAccountId, AccountId, StringComparison.Ordinal)
            ? auth.CurrentFirebaseUser?.IdToken
            : null;
    }

    private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
    {
        if (string.Equals(lastAccountId, user?.LocalId, StringComparison.Ordinal)) return;
        lastAccountId = user?.LocalId;
        AccountChanged?.Invoke();
    }

    public void Dispose() => auth.FirebaseUserChanged -= OnFirebaseUserChanged;
}
