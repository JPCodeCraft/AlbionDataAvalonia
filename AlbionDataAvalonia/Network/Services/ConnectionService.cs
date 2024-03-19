using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using System;
using System.Net.Http;

namespace AlbionDataAvalonia.Network.Services;

public class ConnectionService : IDisposable
{
    public HttpClient httpClient = new HttpClient();
    private readonly PlayerState _playerState;

    public ConnectionService(PlayerState playerState)
    {
        _playerState = playerState;
        InitialSetup();
        _playerState.OnPlayerStateChanged += OnPlayerStateChanged;
    }

    private void OnPlayerStateChanged(object? sender, PlayerStateEventArgs e)
    {
        if (e.AlbionServer != null)
        {
            if (e.AlbionServer.UploadUrl != null &&
                (httpClient.BaseAddress == null ||
                !httpClient.BaseAddress.Equals(new Uri(e.AlbionServer.UploadUrl))))
            {
                httpClient.Dispose();
                httpClient = new HttpClient();
                InitialSetup();
            }
        }
    }

    protected void InitialSetup()
    {
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("albion-data-sharp");
        if (_playerState.AlbionServer != null)
        {
            httpClient.BaseAddress = new Uri(_playerState.AlbionServer.UploadUrl);
        }
    }

    public void Dispose()
    {
        _playerState.OnPlayerStateChanged -= OnPlayerStateChanged;
        httpClient.Dispose();
    }
}
