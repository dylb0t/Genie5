using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Profiles;
using Genie4.Core.Sge;

namespace Genie5.Ui;

public partial class DialogConnect : Window
{
    // Shown in the dropdown; null GameCode means Custom (direct TCP)
    private record GameEntry(string DisplayName, string? GameCode);

    private static readonly GameEntry[] Entries =
    [
        ..SgeClient.Games.Select(g => new GameEntry(g.DisplayName, g.Code)),
        new("─────────────", null),   // visual separator (non-selectable)
        new("Custom (direct TCP)", null),
    ];

    // Index of the Custom entry
    private static readonly int CustomIndex = Entries.Length - 1;

    private readonly ProfileStore? _profileStore;
    private readonly string?       _profilesPath;

    // Results read by caller after ShowDialog<bool> returns true
    public bool   IsSimutronics   { get; private set; }
    public string ResultGameCode  { get; private set; } = string.Empty;
    public string ResultCharacter { get; private set; } = string.Empty;
    public string ResultHost      { get; private set; } = string.Empty;
    public int    ResultPort      { get; private set; }
    public string ResultAccount   { get; private set; } = string.Empty;
    public string ResultPassword  { get; private set; } = string.Empty;

    public DialogConnect(string? currentGameCode = "DR", string? currentHost = "",
                         int currentPort = 4000,
                         ProfileStore? profileStore = null, string? profilesPath = null)
    {
        InitializeComponent();

        _profileStore = profileStore;
        _profilesPath = profilesPath;

        GameBox.ItemsSource = Entries.Select(e => e.DisplayName).ToList();

        // Find matching entry
        var match = currentGameCode is not null
            ? Array.FindIndex(Entries, e => e.GameCode == currentGameCode)
            : -1;

        GameBox.SelectedIndex = match >= 0 ? match : CustomIndex;

        if (match < 0)
        {
            HostBox.Text = currentHost;
            PortBox.Text = currentPort.ToString();
        }
    }

    private void OnGameChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = GameBox.SelectedIndex;
        if (idx < 0 || idx >= Entries.Length) return;

        // Prevent selecting the separator
        if (Entries[idx].GameCode is null && idx != CustomIndex)
        {
            GameBox.SelectedIndex = CustomIndex;
            return;
        }

        var isCustom = idx == CustomIndex;
        HostRow.IsVisible      = isCustom;
        CharacterRow.IsVisible = !isCustom;

        if (!isCustom && SaveProfileCheck.IsChecked == true
            && string.IsNullOrWhiteSpace(ProfileNameBox.Text))
            ProfileNameBox.Text = Entries[idx].DisplayName;
    }

    private void OnSaveProfileChanged(object? sender, RoutedEventArgs e)
    {
        var saving = SaveProfileCheck.IsChecked == true;
        ProfileNameBox.IsEnabled = saving;

        if (saving && string.IsNullOrWhiteSpace(ProfileNameBox.Text))
        {
            var idx = GameBox.SelectedIndex;
            if (idx >= 0 && idx < Entries.Length && Entries[idx].GameCode is not null)
                ProfileNameBox.Text = Entries[idx].DisplayName;
        }
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
    {
        var idx = GameBox.SelectedIndex;
        if (idx < 0 || idx >= Entries.Length)
            { StatusText.Text = "Select a game."; return; }

        var entry    = Entries[idx];
        var account  = AccountBox.Text?.Trim()   ?? string.Empty;
        var password = PasswordBox.Text           ?? string.Empty;
        var isCustom = idx == CustomIndex;

        if (isCustom)
        {
            var host = HostBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(host)) { StatusText.Text = "Host is required."; return; }
            if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
                { StatusText.Text = "Port must be 1–65535."; return; }

            ResultHost     = host;
            ResultPort     = port;
            IsSimutronics  = false;
        }
        else
        {
            if (string.IsNullOrEmpty(account))  { StatusText.Text = "Account name is required."; return; }
            if (string.IsNullOrEmpty(password)) { StatusText.Text = "Password is required."; return; }

            ResultGameCode  = entry.GameCode!;
            ResultCharacter = CharacterBox.Text?.Trim() ?? string.Empty;
            IsSimutronics   = true;
        }

        ResultAccount  = account;
        ResultPassword = password;

        // Optionally save profile
        if (SaveProfileCheck.IsChecked == true && _profileStore is not null && _profilesPath is not null)
        {
            var profileName = ProfileNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(profileName)) { StatusText.Text = "Enter a profile name to save."; return; }

            var profile = _profileStore.Add(
                profileName,
                isCustom ? ResultHost : string.Empty,
                isCustom ? ResultPort : 0,
                account, password);
            profile.IsSimutronics = !isCustom;
            profile.GameCode      = ResultGameCode;
            profile.CharacterName = ResultCharacter;
            _profileStore.Save(_profilesPath);
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
