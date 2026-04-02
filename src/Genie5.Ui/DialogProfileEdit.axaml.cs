using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Profiles;
using Genie4.Core.Sge;

namespace Genie5.Ui;

public partial class DialogProfileEdit : Window
{
    private readonly Guid? _existingId;

    public string ResultName      { get; private set; } = string.Empty;
    public bool   ResultIsSimu    { get; private set; }
    public string ResultGameCode  { get; private set; } = string.Empty;
    public string ResultCharacter { get; private set; } = string.Empty;
    public string ResultHost      { get; private set; } = string.Empty;
    public int    ResultPort      { get; private set; }
    public string ResultAccount   { get; private set; } = string.Empty;
    public string ResultPassword  { get; private set; } = string.Empty;

    public DialogProfileEdit()
    {
        InitializeComponent();
        Title = "New Profile";
        PopulateGameBox();
        PortBox.Text = "4000";
    }

    public DialogProfileEdit(ConnectionProfile profile, ProfileStore store)
    {
        InitializeComponent();
        Title      = "Edit Profile";
        _existingId = profile.Id;
        PopulateGameBox();

        NameBox.Text     = profile.Name;
        AccountBox.Text  = profile.AccountName;

        if (profile.IsSimutronics)
        {
            SimuRadio.IsChecked = true;
            var idx = Array.FindIndex(SgeClient.Games.ToArray(), g => g.Code == profile.GameCode);
            GameBox.SelectedIndex = idx >= 0 ? idx : 0;
            CharacterBox.Text = profile.CharacterName;
        }
        else
        {
            DirectRadio.IsChecked = true;
            HostBox.Text = profile.Host;
            PortBox.Text = profile.Port.ToString();
        }
    }

    private void PopulateGameBox()
    {
        GameBox.ItemsSource   = SgeClient.Games.Select(g => g.DisplayName).ToList();
        GameBox.SelectedIndex = 0;
    }

    private void OnTypeChanged(object? sender, RoutedEventArgs e)
    {
        var isSimu = SimuRadio.IsChecked == true;
        SimuFields.IsVisible   = isSimu;
        CharRow.IsVisible      = isSimu;
        DirectFields.IsVisible = !isSimu;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var name    = NameBox.Text?.Trim()    ?? string.Empty;
        var account = AccountBox.Text?.Trim() ?? string.Empty;
        var isSimu  = SimuRadio.IsChecked == true;

        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Profile name is required."; return; }

        if (isSimu)
        {
            var idx = GameBox.SelectedIndex;
            if (idx < 0) { StatusText.Text = "Select a game."; return; }
            ResultGameCode  = SgeClient.Games[idx].Code;
            ResultCharacter = CharacterBox.Text?.Trim() ?? string.Empty;
            ResultIsSimu    = true;
        }
        else
        {
            var host = HostBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(host)) { StatusText.Text = "Host is required."; return; }
            if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
                { StatusText.Text = "Port must be 1–65535."; return; }
            ResultHost    = host;
            ResultPort    = port;
            ResultIsSimu  = false;
        }

        ResultName     = name;
        ResultAccount  = account;
        ResultPassword = PasswordBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
