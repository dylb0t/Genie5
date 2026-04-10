using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Genie4.Core.Profiles;

namespace Genie5.Ui;

public partial class DialogProfileConnect : Window
{
    private readonly ProfileStore _store;
    private readonly string _storePath;

    public ConnectionProfile? SelectedProfile { get; private set; }

    public DialogProfileConnect(ProfileStore store, string storePath)
    {
        InitializeComponent();
        _store     = store;
        _storePath = storePath;
        Refresh();
    }

    private void Refresh()
    {
        ProfileList.ItemsSource = _store.Profiles
            .Select(p => p.IsSimutronics
                ? $"{p.Name}  [{p.GameCode}] {p.CharacterName}"
                : $"{p.Name}  ({p.Host}:{p.Port})")
            .ToList();
    }

    private ConnectionProfile? SelectedItem()
    {
        var idx = ProfileList.SelectedIndex;
        if (idx < 0 || idx >= _store.Profiles.Count) return null;
        return _store.Profiles[idx];
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogProfileEdit();
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok) return;

        _store.Add(dialog.ResultName, dialog.ResultHost, dialog.ResultPort,
                   dialog.ResultAccount, dialog.ResultPassword,
                   dialog.ResultIsSimu, dialog.ResultGameCode,
                   dialog.ResultCharacter, dialog.ResultAutoConnect);
        _store.Save(_storePath);
        Refresh();
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        var profile = SelectedItem();
        if (profile is null) return;

        var dialog = new DialogProfileEdit(profile, _store);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok) return;

        _store.Update(profile.Id,
            dialog.ResultName, dialog.ResultIsSimu,
            dialog.ResultGameCode, dialog.ResultCharacter,
            dialog.ResultHost, dialog.ResultPort,
            dialog.ResultAccount, dialog.ResultPassword,
            dialog.ResultAutoConnect);
        _store.Save(_storePath);
        Refresh();
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        var profile = SelectedItem();
        if (profile is null) return;
        _store.Remove(profile.Id);
        _store.Save(_storePath);
        Refresh();
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
    {
        var profile = SelectedItem();
        if (profile is null) return;
        SelectedProfile = profile;
        Close(true);
    }

    private void OnProfileDoubleTapped(object? sender, TappedEventArgs e)
    {
        var profile = SelectedItem();
        if (profile is null) return;
        SelectedProfile = profile;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
