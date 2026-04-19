using Avalonia.Controls;
using Avalonia.Threading;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

public partial class StatusBarView : UserControl
{
    private GslGameState? _state;
    private readonly DispatcherTimer _timer;

    public StatusBarView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => RefreshTimeBars();
        _timer.Start();
    }

    public void Attach(GslGameState state)
    {
        if (_state is not null)
            _state.StateChanged -= OnStateChanged;

        _state = state;
        _state.StateChanged += OnStateChanged;

        Compass.Attach(state);
        BodyDiagram.Attach(state);
        StatusIcons.Attach(state);
    }

    private void OnStateChanged() => Refresh();

    private void Refresh()
    {
        if (_state is null) return;
        RefreshVitals();
        RefreshTimeBars();
        RefreshIndicators();
        RefreshHands();
    }

    private void RefreshVitals()
    {
        SetVital(HealthBar,  HealthText,  "Health",        _state!.Health);
        SetVital(ManaBar,    ManaText,    "Mana",          _state.Mana);
        SetVital(ConcBar,    ConcText,    "Concentration", _state.Concentration);
        SetVital(StaminaBar, StaminaText, "Fatigue",       _state.Stamina);
        SetVital(SpiritBar,  SpiritText,  "Spirit",        _state.Spirit);
    }

    private static void SetVital(ProgressBar bar, TextBlock label, string name, int value)
    {
        if (value < 0)
        {
            bar.Value  = 100;
            label.Text = name + " --";
        }
        else
        {
            bar.Value  = value;
            label.Text = $"{name} {value}%";
        }
    }

    // Max seconds seen during the current countdown — used to scale the fill bar
    // so it visibly drains from full to empty as the timer ticks down.
    private long _rtMaxSeen;
    private long _ctMaxSeen;

    private void RefreshTimeBars()
    {
        if (_state is null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var rt = _state.RoundTimeEpoch > 0
            ? Math.Max(0L, _state.RoundTimeEpoch - now)
            : 0L;

        UpdateTimerBox(rt, ref _rtMaxSeen, RoundtimeBox, RoundtimeFill, RoundtimeText, string.Empty);

        var ct = _state.CastTimeEpoch > 0
            ? Math.Max(0L, _state.CastTimeEpoch - now)
            : 0L;

        CastTimeBar.IsVisible = ct > 0;
        if (ct > 0)
        {
            if (ct > _ctMaxSeen) _ctMaxSeen = ct;
            CastTimeBar.Maximum = _ctMaxSeen;
            CastTimeBar.Value   = ct;
            CastTimeText.Text   = $"CT {ct}s";
        }
        else
        {
            _ctMaxSeen = 0;
            CastTimeText.Text = string.Empty;
        }
    }

    private static void UpdateTimerBox(long remaining, ref long maxSeen,
        Border box, Avalonia.Controls.Shapes.Rectangle fill, TextBlock label, string prefix)
    {
        if (remaining <= 0)
        {
            maxSeen = 0;
            fill.Width = 0;
            label.Text = string.Empty;
            return;
        }

        if (remaining > maxSeen) maxSeen = remaining;

        var inner = box.Bounds.Width - box.BorderThickness.Left - box.BorderThickness.Right;
        if (inner < 0) inner = 0;
        fill.Width = inner * ((double)remaining / maxSeen);
        label.Text = string.IsNullOrEmpty(prefix) ? remaining.ToString() : $"{prefix} {remaining}";
    }

    private void RefreshIndicators()
    {
        var s = _state!;

        // Posture, stunned/bleeding/invisible/hidden/joined/webbed, and dead
        // are surfaced by BodyView + StatusIconBar. Only states without an
        // icon remain as text here.
        IndicatorPoisoned.IsVisible  = s.Poisoned;
        IndicatorDiseased.IsVisible  = s.Diseased;
    }

    private void RefreshHands()
    {
        var s = _state!;
        LeftHandText.Text  = string.IsNullOrEmpty(s.LeftHand)  ? "Empty" : s.LeftHand;
        RightHandText.Text = string.IsNullOrEmpty(s.RightHand) ? "Empty" : s.RightHand;
        SpellText.Text     = string.IsNullOrEmpty(s.PreparedSpell) ? "None" : s.PreparedSpell;
    }
}
