using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Aliases;
using Genie4.Core.Highlights;
using Genie4.Core.Presets;
using Genie4.Core.Triggers;

namespace Genie5.Ui;

public partial class FormConfig : Window
{
    public FormConfig(AliasEngine aliases, TriggerEngineFinal triggers, HighlightEngine highlights, PresetEngine presets)
    {
        InitializeComponent();
        AliasesPanelCtrl.Initialize(aliases);
        TriggersPanelCtrl.Initialize(triggers);
        HighlightsPanelCtrl.Initialize(highlights);
        PresetsPanelCtrl.Initialize(presets);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
