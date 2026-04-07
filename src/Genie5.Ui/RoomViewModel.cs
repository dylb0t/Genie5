using Dock.Model.Mvvm.Controls;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

public sealed class RoomViewModel : Document
{
    private string _roomTitle       = string.Empty;
    private string _roomDescription = string.Empty;
    private string _roomObjects     = string.Empty;
    private string _roomPlayers     = string.Empty;

    public string RoomTitle
    {
        get => _roomTitle;
        private set => SetProperty(ref _roomTitle, value);
    }

    public string RoomDescription
    {
        get => _roomDescription;
        private set => SetProperty(ref _roomDescription, value);
    }

    public string RoomObjects
    {
        get => _roomObjects;
        private set => SetProperty(ref _roomObjects, value);
    }

    public string RoomPlayers
    {
        get => _roomPlayers;
        private set => SetProperty(ref _roomPlayers, value);
    }

    public RoomViewModel()
    {
        Id       = "Room";
        Title    = "Room";
        CanClose = true;
        CanPin   = false;
    }

    public void Attach(GslGameState state)
    {
        state.StateChanged += () =>
        {
            RoomTitle       = state.RoomTitle;
            RoomDescription = state.RoomDescription;
            RoomObjects     = state.RoomObjects;
            RoomPlayers     = state.RoomPlayers;
        };
    }
}
