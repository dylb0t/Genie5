using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Aliases;
using Genie4.Core.Highlights;
using Genie4.Core.Layout;
using Genie4.Core.Presets;
using Genie4.Core.Triggers;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class FormConfig : Window
{
    public FormConfig(AliasEngine aliases, TriggerEngineFinal triggers, HighlightEngine highlights,
                      PresetEngine presets, WindowSettingsStore windowSettings,
                      VariableStore variables, Action onVariablesChanged)
    {
        InitializeComponent();
        AliasesPanelCtrl.Initialize(aliases);
        TriggersPanelCtrl.Initialize(triggers);
        HighlightsPanelCtrl.Initialize(highlights);
        PresetsPanelCtrl.Initialize(presets);
        VariablesPanelCtrl.Initialize(variables, onVariablesChanged);
        LayoutPanelCtrl.Initialize(windowSettings);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
