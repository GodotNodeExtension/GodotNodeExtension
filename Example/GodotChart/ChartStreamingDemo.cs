using System;
using System.Globalization;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Two live charts that deliberately run at <b>different rates</b>, each with a bounded buffer:
/// <list type="number">
/// <item>an <b>oscilloscope</b> sampling at 60 Hz through a fixed-size <b>ring buffer</b> re-drawn with
/// <see cref="ChartView.SetData(DataRow[])"/> - the category axis stays pinned at 0..N, so the trace slides
/// smoothly;</item>
/// <item>a <b>price feed</b> that appends one 10 second candle every 10 seconds with
/// <see cref="ChartView.AddRow"/> + <see cref="ChartView.WindowSize"/> (the library drops the bars that
/// leave the window). Its X axis is a time label, an ordinal axis, so nothing wobbles.</item>
/// </list>
/// <para>
/// Space pauses, <c>1</c> / <c>2</c> halve or double the overall speed (both feeds), <c>C</c> clears.
/// </para>
/// </summary>
public partial class ChartStreamingDemo : Control
{
    private const int ScopeWindow = 240;       // samples kept = 4 s of trace at 60 Hz
    private const int QuoteWindow = 60;        // candles kept = 10 minutes of 10 s bars
    private const double QuoteSeconds = 10.0;  // one candle every 10 seconds, like a slow quote feed
    private const double ScopeHz = 60.0;       // the scope samples far faster than the feed ticks

    private readonly DataRow[] _ring = new DataRow[ScopeWindow];
    private readonly double[] _samples = new double[ScopeWindow];

    // This frame's new samples, staged before they go into the window: at 16x speed a 60 Hz scope produces
    // up to ~16 of them per frame, and the whole 240-row window shifts once per frame instead of once per
    // sample. The buffer holds a whole window, so the shift below can never miss a staged sample.
    private readonly double[] _staged = new double[ScopeWindow];

    // Node references are wired in ChartStreamingDemo.tscn (node_paths + NodePath), never looked up by path.
    [Export] public ChartView Scope { get; set; } = null!;
    [Export] public ChartView Quotes { get; set; } = null!;
    [Export] public ChartView Gauge { get; set; } = null!;
    [Export] public Label Readout { get; set; } = null!;
    [Export] public Button PauseButton { get; set; } = null!;
    [Export] public Button SlowerButton { get; set; } = null!;
    [Export] public Button FasterButton { get; set; } = null!;
    [Export] public Button ClearButton { get; set; } = null!;

    private double _time;
    private double _nextSample;
    private double _nextQuote;
    private double _speed = 1.0;
    private double _lastClose = 120.0;
    private TimeSpan _quoteClock = TimeSpan.FromHours(9.5);   // a market session starts at 09:30
    private bool _paused;
    private int _stagedCount;
    private int _frames;
    private double _fpsTime;

    /// <summary>A noisy sine plus a slow drift, like a real probe signal.</summary>
    private static double Signal(double t)
        => 50 + 28 * Math.Sin(t * 3.1) + 6 * Math.Sin(t * 0.37) + Random.Shared.NextDouble() * 2 - 1;

    /// <inheritdoc />
    public override void _Ready()
    {
        // The scene declares the three ChartView nodes (kind, fields, window) and the page's minimum size -
        // this script only feeds them data, which is the pattern to copy for a live chart.
        PauseButton.Pressed += TogglePause;
        SlowerButton.Pressed += () => _speed = Math.Max(0.25, _speed / 2);
        FasterButton.Pressed += () => _speed = Math.Min(16.0, _speed * 2);
        ClearButton.Pressed += Clear;

        for (int i = 0; i < ScopeWindow; i++)
        {
            _ring[i] = new DataRow(2).Set("t", i).Set("value", 50.0);
            _samples[i] = 50.0;
        }
        Scope.SetData(_ring);

        for (int i = 0; i < QuoteWindow; i++)
            Quotes.AddRow(NextCandle());
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (!_paused) return;

        // Freeze both schedule clocks with the accumulated time, otherwise resuming would replay every
        // sample that was skipped while paused (a long pause at high speed stutters hard).
        _nextSample = _time;
        _nextQuote = _time;
    }

    private void Clear()
    {
        Array.Fill(_samples, 50.0);
        _stagedCount = 0;
        // Only the value is rewritten here: the "t" category of a row is its slot index, written once when
        // the rows were built, and nothing moves a row out of its slot.
        for (int i = 0; i < _ring.Length; i++)
            _ring[i].Set("value", 50.0);
        Scope.SetData(_ring);

        Quotes.Clear();
        _lastClose = 120.0;
        _quoteClock = TimeSpan.FromHours(9.5);
        for (int i = 0; i < QuoteWindow; i++)
            Quotes.AddRow(NextCandle());

        _time = 0;
        _nextSample = 0;
        _nextQuote = 0;
    }

    /// <summary>
    /// Stage one sample of this frame, keeping at most <see cref="ScopeWindow"/> of them: anything older
    /// would be pushed out again by the shift that puts them into the window, so it can be dropped here.
    /// </summary>
    private void AddStaged(double value)
    {
        if (_stagedCount == _staged.Length) Array.Copy(_staged, 1, _staged, 0, _staged.Length - 1);
        else _stagedCount++;

        _staged[_stagedCount - 1] = value;
    }

    /// <summary>The next 10 second candle of a random walk, stamped with the session clock.</summary>
    private DataRow NextCandle()
    {
        double open = _lastClose;
        double close = Math.Max(5.0, open + (Random.Shared.NextDouble() - 0.48) * 2.4);
        double high = Math.Max(open, close) + Random.Shared.NextDouble() * 0.8;
        double low = Math.Min(open, close) - Random.Shared.NextDouble() * 0.8;
        _lastClose = close;

        _quoteClock = _quoteClock.Add(TimeSpan.FromSeconds(QuoteSeconds));
        var row = new DataRow(5)
            .Set("time", _quoteClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture))
            .Set("open", Math.Round(open, 2))
            .Set("high", Math.Round(high, 2))
            .Set("low", Math.Round(low, 2))
            .Set("close", Math.Round(close, 2));
        return row;
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: _speed = Math.Max(0.25, _speed / 2); break;
            case Key.Key2: _speed = Math.Min(16.0, _speed * 2); break;
            case Key.C: Clear(); break;
            default: return;                                   // an unhandled key stays with the GUI
        }

        // Handled here, so the GUI must not also act on it: _Input runs before the GUI, and the example
        // browser's tree reads a plain letter as type-ahead (the same reason the other pages read their keys
        // here instead of in _UnhandledKeyInput).
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The clocks advance with the simulated time only: while paused nothing is scheduled, so the
        // catch-up loops below never queue a backlog.
        if (!_paused) _time += delta;
        _frames++;
        _fpsTime += delta;

        if (!_paused)
        {
            // 1. The scope: fast, continuous. Collect this frame's samples first, then slide the window once
            // - the old version did an Array.Copy plus 240 DataRow.Set calls per sample, at 16x that is a
            // 16-fold repetition of the same work every frame.
            while (_time >= _nextSample)
            {
                AddStaged(Math.Round(Signal(_nextSample), 2));
                _nextSample += 1.0 / (ScopeHz * _speed);
            }

            if (_stagedCount > 0)
            {
                // One shift for the whole frame, then the new samples into the freed slots: the row objects
                // keep their slot, and with it the "t" category, so only the values move.
                int count = Math.Min(_stagedCount, ScopeWindow);
                Array.Copy(_samples, count, _samples, 0, ScopeWindow - count);
                Array.Copy(_staged, _stagedCount - count, _samples, ScopeWindow - count, count);
                for (int i = 0; i < _ring.Length; i++)
                    _ring[i].Set("value", _samples[i]);

                Gauge.SetData(new DataRow(2).Set("label", "live")
                    .Set("value", Math.Round(_samples[^1], 1)));

                Scope.SetData(_ring);   // same row objects, new values: no per-frame allocation
                _stagedCount = 0;
            }

            // 2. The feed: slow, irregular in spirit, one candle at a time. WindowSize drops the oldest.
            while (_time >= _nextQuote)
            {
                Quotes.AddRow(NextCandle());
                _nextQuote += QuoteSeconds / _speed;
            }
        }

        if (_fpsTime >= 0.5)
        {
            int bars = Quotes.DataRows.Count;
            Readout.Text = $"{(_paused ? "paused" : "running")} · scope {ScopeHz * _speed:F0} Hz · " +
                            $"feed every {QuoteSeconds / _speed:F1} s ({bars}/{QuoteWindow} candles · last {_lastClose:F2}) · " +
                            $"{_frames / _fpsTime:F0} fps";
            _fpsTime = 0;
            _frames = 0;
        }
    }
}
