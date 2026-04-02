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
    }

    private void OnStateChanged() => Refresh();

    private void Refresh()
    {
        if (_state is null) return;
        RefreshVitals();
        RefreshTimeBars();
        RefreshIndicators();
        RefreshSpell();
    }

    private void RefreshVitals()
    {
        SetVital(HealthBar,  HealthText,  _state!.Health);
        SetVital(ManaBar,    ManaText,    _state.Mana);
        SetVital(StaminaBar, StaminaText, _state.Stamina);
        SetVital(SpiritBar,  SpiritText,  _state.Spirit);
        SetVital(ConcBar,    ConcText,    _state.Concentration);
    }

    private static void SetVital(ProgressBar bar, TextBlock label, int value)
    {
        if (value < 0)
        {
            bar.Value = 100;
            label.Text = "--";
        }
        else
        {
            bar.Value = value;
            label.Text = value.ToString();
        }
    }

    private void RefreshTimeBars()
    {
        if (_state is null) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Roundtime
        var rt = _state.RoundTimeEpoch > 0
            ? Math.Max(0L, _state.RoundTimeEpoch - now)
            : 0L;

        RoundtimePanel.IsVisible = rt > 0;
        if (rt > 0)
        {
            RoundtimeBar.Maximum = Math.Max(rt, 1);
            RoundtimeBar.Value   = rt;
            RoundtimeText.Text   = $"{rt}s";
        }

        // Cast time
        var ct = _state.CastTimeEpoch > 0
            ? Math.Max(0L, _state.CastTimeEpoch - now)
            : 0L;

        CastTimePanel.IsVisible = ct > 0;
        if (ct > 0)
        {
            CastTimeBar.Maximum = Math.Max(ct, 1);
            CastTimeBar.Value   = ct;
            CastTimeText.Text   = $"{ct}s";
        }

        TimeSeparator.IsVisible = rt > 0 || ct > 0;
    }

    private void RefreshIndicators()
    {
        var s = _state!;

        // Position — show exactly one
        IndicatorStanding.IsVisible = s.Standing  && !s.Sitting && !s.Kneeling && !s.Prone;
        IndicatorSitting.IsVisible  = s.Sitting;
        IndicatorKneeling.IsVisible = s.Kneeling;
        IndicatorProne.IsVisible    = s.Prone;

        // Ailments
        IndicatorStunned.IsVisible   = s.Stunned;
        IndicatorDead.IsVisible      = s.Dead;
        IndicatorBleeding.IsVisible  = s.Bleeding;
        IndicatorPoisoned.IsVisible  = s.Poisoned;
        IndicatorDiseased.IsVisible  = s.Diseased;
        IndicatorWebbed.IsVisible    = s.Webbed;
        IndicatorHidden.IsVisible    = s.Hidden;
        IndicatorInvisible.IsVisible = s.Invisible;
        IndicatorJoined.IsVisible    = s.Joined;
    }

    private void RefreshSpell()
    {
        var spell = _state!.PreparedSpell;
        SpellPanel.IsVisible = !string.IsNullOrEmpty(spell);
        SpellText.Text = spell;
    }
}
