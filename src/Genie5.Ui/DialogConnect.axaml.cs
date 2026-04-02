using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Genie5.Ui;

public partial class DialogConnect : Window
{
    public string? ResultHost { get; private set; }
    public int ResultPort { get; private set; }

    public DialogConnect(string currentHost, int currentPort)
    {
        InitializeComponent();
        HostBox.Text = currentHost;
        PortBox.Text = currentPort.ToString();
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(host)) { StatusText.Text = "Host is required."; return; }
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
            { StatusText.Text = "Port must be 1–65535."; return; }

        ResultHost = host;
        ResultPort = port;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
