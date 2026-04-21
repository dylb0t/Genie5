using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Aliases;
using Genie4.Core.Classes;
using Genie4.Core.Gags;
using Genie4.Core.Highlights;
using Genie4.Core.Layout;
using Genie4.Core.Macros;
using Genie4.Core.Presets;
using Genie4.Core.Substitutes;
using Genie4.Core.Triggers;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class FormConfig : Window
{
    public FormConfig(AliasEngine aliases, TriggerEngineFinal triggers,
                      HighlightEngine highlights, NameHighlightEngine names,
                      PresetEngine presets, WindowSettingsStore windowSettings,
                      VariableStore variables,
                      SubstituteEngine substitutes, GagEngine gags, MacroEngine macros,
                      ClassEngine classes,
                      Func<string> namesConfigPath,
                      Action onVariablesChanged,
                      Action onNamesChanged,
                      Action onPresetsChanged,
                      Action onSubstitutesChanged,
                      Action onGagsChanged,
                      Action onMacrosChanged,
                      Action onClassesChanged)
    {
        InitializeComponent();
        AliasesPanelCtrl.Initialize(aliases);
        TriggersPanelCtrl.Initialize(triggers);
        HighlightsPanelCtrl.Initialize(highlights, names, presets,
                                       namesConfigPath,
                                       onNamesChanged,
                                       onPresetsChanged);
        SubstitutesPanelCtrl.Initialize(substitutes, onSubstitutesChanged);
        GagsPanelCtrl.Initialize(gags, onGagsChanged);
        MacrosPanelCtrl.Initialize(macros, onMacrosChanged);
        ClassesPanelCtrl.Initialize(classes, onClassesChanged);
        VariablesPanelCtrl.Initialize(variables, onVariablesChanged);
        LayoutPanelCtrl.Initialize(windowSettings);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
