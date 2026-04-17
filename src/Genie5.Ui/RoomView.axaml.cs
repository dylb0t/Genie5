using Avalonia.Controls;

namespace Genie5.Ui;

public partial class RoomView : UserControl
{
    public RoomView()
    {
        InitializeComponent();

        SizeChanged += (_, e) =>
        {
            var dp = this.FindControl<DockPanel>("RoomDockPanel");
            if (dp is not null)
            {
                dp.Width  = e.NewSize.Width;
                dp.Height = e.NewSize.Height;
            }
        };
    }
}
