using AlbionDataAvalonia.Network.Services;

namespace AlbionDataAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(NetworkListenerService networkListener)
    {
        networkListener.Run();

    }
    public string Greeting => "Welcome to Avalonia!";
}
