using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Genie5.Ui;

public partial class DialogMapperScriptSettings : Window
{
    public bool   ResultUseScript  { get; private set; }
    public string ResultScriptName { get; private set; } = "automapper";

    public DialogMapperScriptSettings(bool useScript, string scriptName)
    {
        InitializeComponent();
        UseScriptCheckBox.IsChecked = useScript;
        ScriptNameBox.Text = scriptName;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ResultUseScript  = UseScriptCheckBox.IsChecked == true;
        ResultScriptName = string.IsNullOrWhiteSpace(ScriptNameBox.Text)
            ? "automapper" : ScriptNameBox.Text.Trim();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
