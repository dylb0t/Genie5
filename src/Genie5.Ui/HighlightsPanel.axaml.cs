using Avalonia.Controls;
using Genie4.Core.Highlights;
using Genie4.Core.Presets;

namespace Genie5.Ui;

public partial class HighlightsPanel : UserControl
{
    public HighlightsPanel() => InitializeComponent();

    public void Initialize(
        HighlightEngine      highlights,
        NameHighlightEngine  names,
        PresetEngine         presets,
        Func<string>         namesConfigPath,
        Action?              onNamesChanged   = null,
        Action?              onPresetsChanged = null)
    {
        StringsPanelCtrl.Initialize(highlights);
        NamesPanelCtrl.Initialize(names, namesConfigPath, onNamesChanged);
        PresetsPanelCtrl.Initialize(presets, onPresetsChanged);
    }
}
