using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — automatic scale inference and fitting.
/// </summary>
public partial class Chart
{
    // ── Scale auto-inference ────────────────────────────────────
    private void AutoFitScales()
    {
        var renderData = GetRenderData();

        foreach (var (channel, values) in CollectEncodeValues(renderData))
        {
            if (_scales.Has(channel)) continue; // manually configured scale wins
            if (values.Count == 0) continue;

            // A colour channel is categorical by nature: mapping numeric ids through a linear
            // scale would drop the legend and paint every element with the same colour. The shape
            // channel is categorical for the same reason (G2 treats it the same way).
            bool numericChannel = channel != Channel.Color && channel != Channel.Shape && IsNumeric(values[0]);
            IScale autoScale = channel switch
            {
                // Values that are colours themselves are the colours to draw with (G2's identity scale);
                // anything else is a category and gets a palette colour, exactly as before.
                Channel.Color when values.All(value => ColorValues.TryParse(value, out _)) =>
                    new IdentityColorScale { Palette = (Color[])_theme.Palette.Clone() },
                Channel.Color => new ColorScale { Palette = (Color[])_theme.Palette.Clone() },
                Channel.Shape => new ShapeScale(),
                _ => numericChannel
                    // A numeric category axis (sample index, timestamp, frequency...) describes where the
                    // data sits, not how far it is from zero: forcing zero would squeeze a rolling window
                    // into one corner of the plot. The same goes for the size channel, where a bubble's
                    // radius encodes the magnitude *inside the column* - with zero included every bubble
                    // would sit near the top of the range and `PointSizeMin` would never be reached. The
                    // value and opacity axes keep the zero baseline (0 value = baseline, 0 opacity =
                    // invisible).
                    ? new LinearScale { IncludeZero = channel is not (Channel.X or Channel.Size) }
                    : new OrdinalScale(),
            };

            autoScale.Fit(FittableValues(values, numericChannel));
            _scales.Set(channel, autoScale);
            _autoFittedChannels.Add(channel);
            RecordBaseDomain(channel, autoScale);
        }

        // Auto-fit Y2 scale from marks that use Y2 channel
        if (!_scales.Has(Channel.Y2))
        {
            var y2Values = new List<object>();
            foreach (var mark in _marks)
            {
                if (mark.YChannel != Channel.Y2) continue;
                var data = mark.Data ?? renderData;
                // Use the mark's local Y encode field, or fall back to chart-level Y encode. The lookup
                // runs once: it is asked per mark and per frame, and a second call for the null test was
                // pure overhead.
                string? localField = mark.GetLocalEncodeField(Channel.Y);
                string? fieldName = localField ?? (_encodes.TryGet(Channel.Y) is FieldEncode yfe ? yfe.FieldName : null);
                if (fieldName == null) continue;
                foreach (var row in data)
                {
                    if (row.Has(fieldName) && row.Get(fieldName) is { } value)
                        y2Values.Add(value);
                }
            }
            if (y2Values.Count > 0 && IsNumeric(y2Values[0]))
            {
                var y2Scale = new LinearScale();
                y2Scale.Fit(FittableValues(y2Values, numeric: true));
                _scales.Set(Channel.Y2, y2Scale);
                _autoFittedChannels.Add(Channel.Y2);
            }
        }

        ApplyStickyAutoScale();
        ApplyOneSidedLimits();
        ApplyDomainLocks();
        RememberBaseDomains();
        ApplyZoomDomains();
    }

    // ── Zoom / pan ──────────────────────────────────────────────

    /// <summary>
    /// The domain a zoom has to stay inside: the one the chart fitted (or the host pinned) for that channel.
    /// Recorded on every fit, so a data change re-clamps an existing zoom instead of letting it drift into
    /// empty space.
    /// </summary>
    private readonly Dictionary<Channel, (double Min, double Max)> _baseDomains = [];

    /// <summary>The interactive window of a channel, layered over <see cref="_domainLocks"/>.</summary>
    private readonly Dictionary<Channel, (double Min, double Max)> _zoomDomains = [];

    /// <summary>
    /// Smallest number of data steps a zoomed window keeps in view: six, so the axis it produces still has the
    /// floor of labels <see cref="ChartTheme.MinTickCount"/> asks for.
    /// </summary>
    private const int MinDataStepsPerWindow = 6;

    /// <summary>
    /// Smallest positive difference between neighbouring fitted values - the data's own resolution. Zero when
    /// every value is equal (nothing to measure) or there are fewer than two of them.
    /// </summary>
    private static double SmallestStep(IReadOnlyList<double> values)
    {
        double smallest = 0;
        for (int i = 1; i < values.Count; i++)
        {
            double gap = values[i] - values[i - 1];
            if (gap > 0 && (smallest == 0 || gap < smallest)) smallest = gap;
        }
        return smallest;
    }

    /// <summary>
    /// Smallest window a zoom is allowed to reach, as a fraction of the base domain width.
    /// <para>
    /// It is deliberately large enough to be reached: at 1e-4 a reader scrolling a 54-year axis would stop
    /// after roughly 90 notches, at a window of five thousandths of a year where every tick label reads the
    /// same and the line is a straight segment - indistinguishable from "the zoom never ends". A twentieth
    /// of the data stops it after a handful of notches, at a window that still shows structure (about three
    /// years of an annual series).
    /// </para>
    /// </summary>
    private const double MinZoomWidthFraction = 1.0 / 20.0;

    /// <summary>
    /// Zoom one channel around a point of the current window. <paramref name="factor"/> below 1 zooms in,
    /// above 1 zooms out (it multiplies the width), and <paramref name="focus"/> is where inside the window
    /// the value under the pointer stays put - 0.5 keeps the middle, which is what a double click or a
    /// key-driven zoom wants.
    /// <para>
    /// The window never leaves the base domain (the fitted data range, or the one the host pinned with
    /// <see cref="ScaleDomain(Channel, double, double)"/>), so panning or zooming cannot lose the data and
    /// <see cref="ResetZoom()"/> always has somewhere sensible to return to. Only linear channels zoom:
    /// a category axis has no numeric window to narrow.
    /// </para>
    /// </summary>
    /// <param name="channel">Channel whose scale is zoomed (X, Y, Y2, Size, Opacity).</param>
    /// <param name="factor">Multiplier for the current domain width; below 1 zooms in.</param>
    /// <param name="focus">Position inside the current window that stays fixed, 0 (low end) to 1 (high end).</param>
    /// <returns>False when the channel has no numeric window to zoom.</returns>
    public bool ZoomDomain(Channel channel, double factor, double focus = 0.5)
    {
        if (!TryGetDomain(channel, out double min, out double max)) return false;
        if (!double.IsFinite(factor) || factor <= 0) return false;

        var (baseMin, baseMax) = BaseDomain(channel, min, max);
        double baseWidth = baseMax - baseMin;
        // Defensive, unreachable by contract: BaseDomain only records a domain whose Max > Min.
        if (!(baseWidth > 0)) return false;

        double width = max - min;
        double smallest = baseWidth * MinZoomWidthFraction;

        // ... and never below a few data steps. A window narrower than the data's own resolution cannot be
        // labelled: with one value per year, a two-year window holds two real years, so "at least six ticks"
        // would need ticks that are not values the table has. Keeping MinDataStepsPerWindow steps in view is
        // what makes the floor meaningful to a reader instead of a row of identical labels.
        if (_scales.TryGet(channel) is LinearScale linear && linear.FittedValues.Count > MinDataStepsPerWindow)
        {
            // Only when the table really has that many distinct values (a two-point series has nothing to
            // keep in view), and never wider than the base domain - a floor above the ceiling would make
            // Math.Clamp below throw.
            double steps = MinDataStepsPerWindow * SmallestStep(linear.FittedValues);
            smallest = Math.Min(baseWidth, Math.Max(smallest, steps));
        }

        double newWidth = Math.Clamp(width * factor, smallest, baseWidth);

        focus = Math.Clamp(double.IsFinite(focus) ? focus : 0.5, 0, 1);
        double anchor = min + width * focus;
        SetZoomWindow(channel, anchor - newWidth * focus, newWidth, baseMin, baseMax);
        return true;
    }

    /// <summary>
    /// Move one channel's window sideways by a fraction of its own width (positive moves towards larger
    /// values), clamped to the base domain. This is what a drag gesture calls, once per pointer move.
    /// </summary>
    /// <param name="channel">Channel whose scale is panned.</param>
    /// <param name="fraction">Shift as a fraction of the current window width.</param>
    /// <returns>False when the channel has no numeric window to pan.</returns>
    public bool PanDomain(Channel channel, double fraction)
    {
        if (!TryGetDomain(channel, out double min, out double max)) return false;
        if (!double.IsFinite(fraction) || fraction == 0) return false;

        var (baseMin, baseMax) = BaseDomain(channel, min, max);
        SetZoomWindow(channel, min + (max - min) * fraction, max - min, baseMin, baseMax);
        return true;
    }

    /// <summary>
    /// The channel's current window: the zoom one when the user zoomed, otherwise the fitted (or pinned)
    /// domain. False for a channel without a numeric window, which is what a category or colour axis is.
    /// </summary>
    /// <param name="channel">Channel to read.</param>
    /// <param name="min">Lower bound of the window.</param>
    /// <param name="max">Upper bound of the window.</param>
    /// <returns>True when the channel has a numeric window.</returns>
    public bool TryGetDomain(Channel channel, out double min, out double max)
    {
        if (_zoomDomains.TryGetValue(channel, out var zoom))
        {
            (min, max) = zoom;
            return true;
        }

        if (_scales.TryGet(channel) is LinearScale linear && linear.Max > linear.Min)
        {
            (min, max) = (linear.Min, linear.Max);
            return true;
        }

        (min, max) = (0, 0);
        return false;
    }

    /// <summary>
    /// Drop the zoom windows, so every channel shows its fitted (or pinned) domain again. Domains pinned
    /// with <see cref="ScaleDomain(Channel, double, double)"/> are untouched - they are the host's decision,
    /// not a gesture.
    /// </summary>
    public void ResetZoom()
    {
        if (_zoomDomains.Count == 0) return;

        foreach (var channel in new List<Channel>(_zoomDomains.Keys)) RestoreBaseDomain(channel, 0, 0);
        _zoomDomains.Clear();
        _layoutVersion++;
    }

    /// <summary>Drop one channel's zoom window; see <see cref="ResetZoom()"/>.</summary>
    /// <param name="channel">Channel to reset.</param>
    public void ResetZoom(Channel channel)
    {
        if (!_zoomDomains.Remove(channel)) return;

        RestoreBaseDomain(channel, 0, 0);
        _layoutVersion++;
    }

    /// <summary>The window a zoom has to stay inside for a channel, falling back to the current one.</summary>
    private (double Min, double Max) BaseDomain(Channel channel, double min, double max)
        => _baseDomains.TryGetValue(channel, out var recorded) && recorded.Max > recorded.Min
            ? recorded
            : (min, max);

    /// <summary>
    /// Put a channel's scale back on its base domain (the range the data or the pin reaches), which is what
    /// dropping a zoom window means. Passing a zero-width range looks the base up instead - the two callers
    /// that drop windows on purpose (a window as wide as the base, and <see cref="ResetZoom()"/>) do not have
    /// a useful range at hand.
    /// </summary>
    private void RestoreBaseDomain(Channel channel, double min, double max)
    {
        if (_scales.TryGet(channel) is not LinearScale linear) return;

        var (baseMin, baseMax) = (max > min) ? (min, max) : BaseDomain(channel, linear.Min, linear.Max);
        if (baseMax > baseMin) linear.SetDomain(baseMin, baseMax);
    }

    /// <summary>Store a window, shifted back inside the base domain when it stuck out.</summary>
    private void SetZoomWindow(Channel channel, double min, double width, double baseMin, double baseMax)
    {
        double max = min + width;
        if (min < baseMin) (min, max) = (baseMin, baseMin + width);
        if (max > baseMax) (min, max) = (baseMax - width, baseMax);

        // A window as wide as the base domain is not a zoom at all: keep the layer empty instead of storing a
        // no-op entry that ResetZoom would have to undo. The scale has to be put back on the base domain here,
        // otherwise a zoom-out would leave it on the window it just left.
        if (width >= baseMax - baseMin)
        {
            _zoomDomains.Remove(channel);
            RestoreBaseDomain(channel, baseMin, baseMax);
            _layoutVersion++;
            return;
        }

        _zoomDomains[channel] = (min, max);
        _layoutVersion++;
    }

    /// <summary>Record a channel's base domain: what the data (or the host's pin) reaches.</summary>
    private void RecordBaseDomain(Channel channel, IScale? scale)
    {
        if (scale is LinearScale linear && linear.Max > linear.Min)
            _baseDomains[channel] = (linear.Min, linear.Max);
    }

    /// <summary>
    /// Fill in a base domain for the channels a fit did not produce one for (a scale the host installed by
    /// hand, for instance), and only for those: an entry that exists already is the data's range and must not
    /// be overwritten by whatever the scale shows right now.
    /// <para>
    /// This is the reason the base is recorded where the domain is <i>fitted</i> instead of read back here.
    /// A rebuild only fits the channels whose scales it dropped, so reading the scales at this point would
    /// record a window a zoom already narrowed - and then every zoom re-bases itself on its own window: the
    /// zoom looks unbounded going in and zooming back out becomes a no-op, because the window it has to stay
    /// inside is itself.
    /// </para>
    /// </summary>
    private void RememberBaseDomains()
    {
        foreach (var channel in new[] { Channel.X, Channel.Y, Channel.Y2, Channel.Size, Channel.Opacity })
        {
            if (_baseDomains.ContainsKey(channel)) continue;
            RecordBaseDomain(channel, _scales.TryGet(channel));
        }
    }

    /// <summary>
    /// Re-apply the interactive windows after a fit. They are clamped to the freshly recorded base domains,
    /// so a data update that narrows the range cannot leave the viewport parked outside the data.
    /// </summary>
    private void ApplyZoomDomains()
    {
        if (_zoomDomains.Count == 0) return;

        foreach (var channel in new List<Channel>(_zoomDomains.Keys))
        {
            var (min, max) = _zoomDomains[channel];
            var (baseMin, baseMax) = BaseDomain(channel, min, max);
            double width = Math.Min(max - min, baseMax - baseMin);
            if (width <= 0 || _scales.TryGet(channel) is not LinearScale linear || width >= baseMax - baseMin)
            {
                _zoomDomains.Remove(channel);
                RestoreBaseDomain(channel, baseMin, baseMax);
                continue;
            }

            SetZoomWindow(channel, min, width, baseMin, baseMax);
            var (zoomMin, zoomMax) = _zoomDomains[channel];
            linear.SetDomain(zoomMin, zoomMax);
        }
    }

    /// <summary>
    /// The domain each channel's sticky auto-scaling is currently holding, and the margin it was fitted with.
    /// Kept apart from <see cref="_baseDomains"/> (the data's range) and <see cref="_zoomDomains"/> (a gesture's
    /// window): this is the automatic axis' own decision, and either of the other two outranks it.
    /// </summary>
    private readonly Dictionary<Channel, (double Min, double Max)> _stickyDomains = [];

    /// <summary>
    /// Sticky auto-scaling (<see cref="AxisConfig.AutoScaleMargin"/>) and the nice domain
    /// (<see cref="AxisConfig.NiceDomain"/>) for the value axes. A feed that wobbles inside the margin keeps the
    /// axis it has - the ticks stay put instead of jittering with every row - and a value that leaves it refits
    /// the axis, jumping to the next {1, 2, 5} x 10^n step when asked instead of drifting by a pixel.
    /// <para>
    /// A pinned domain or a gesture outranks it: an explicit decision is never overridden by the automatic one.
    /// Runs after the fit (so the scales hold the data's own range here) and before the locks and the zoom
    /// windows are applied.
    /// </para>
    /// </summary>
    private void ApplyStickyAutoScale()
    {
        ApplyStickyAutoScale(Channel.Y, _yAxisConfig);
        ApplyStickyAutoScale(Channel.Y2, _y2AxisConfig);
    }

    /// <summary>Sticky auto-scaling for one value axis; see <see cref="ApplyStickyAutoScale()"/>.</summary>
    private void ApplyStickyAutoScale(Channel channel, AxisConfig? config)
    {
        if (config is null) return;
        if (_domainLocks.ContainsKey(channel) || _zoomDomains.ContainsKey(channel)) return;
        if (_scales.TryGet(channel) is not LinearScale scale) return;

        float? margin = config.AutoScaleMargin is { } value && value > 0f ? value : null;
        if (margin is null && !config.NiceDomain) return;

        double dataMin = scale.Min, dataMax = scale.Max;

        // Still inside the band the axis is showing (plus its margin): leave the axis exactly where it is.
        if (margin is { } keep && _stickyDomains.TryGetValue(channel, out var held))
        {
            double span = held.Max - held.Min;
            if (span > 0 && dataMin >= held.Min - keep * span && dataMax <= held.Max + keep * span)
            {
                scale.SetDomain(held.Min, held.Max);
                return;
            }
        }

        double newMin = dataMin, newMax = dataMax;
        if (!(newMax > newMin))
        {
            // A flat series has no range to fit: give it one so the axis is not degenerate.
            double pad = Math.Max(Math.Abs(newMax) * 0.1, 1.0);
            newMin -= pad;
            newMax += pad;
        }

        if (margin is { } pad2)
        {
            // The band is seeded with the margin, so the next rows can wobble without moving the axis again.
            double span = newMax - newMin;
            newMin -= pad2 * span;
            newMax += pad2 * span;
        }

        if (config.NiceDomain)
        {
            double step = NiceDomainStep(newMax - newMin);
            newMin = Math.Floor(newMin / step) * step;
            newMax = Math.Ceiling(newMax / step) * step;
        }

        _stickyDomains[channel] = (newMin, newMax);
        scale.SetDomain(newMin, newMax);
    }

    /// <summary>
    /// The {1, 2, 5} x 10^n step a domain of this width should be rounded out to: the smallest such step that is
    /// not finer than the width divided by ten, so a domain ends up with a handful of clean divisions.
    /// </summary>
    private static double NiceDomainStep(double span)
    {
        if (!(span > 0) || !double.IsFinite(span)) return 1.0;

        double raw = span / 10.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double scaled = raw / magnitude;
        double factor = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
        return factor * magnitude;
    }

    /// <summary>
    /// One-sided pins (<see cref="AxisConfig.MinLimit"/> / <see cref="AxisConfig.MaxLimit"/>): the caller fixes one
    /// end of a value axis and leaves the other to the data - "cap the axis, let it grow", which is what a quote
    /// feed wants when it must never be flattened by a long trend. Applied after the fit (so the free end is the
    /// fitted one, sticky margin included) and skipped for a channel the host pinned two-sided instead.
    /// </summary>
    private void ApplyOneSidedLimits()
    {
        ApplyOneSidedLimit(Channel.Y, _yAxisConfig);
        ApplyOneSidedLimit(Channel.Y2, _y2AxisConfig);
    }

    /// <summary>One value axis' one-sided pins; see <see cref="ApplyOneSidedLimits()"/>.</summary>
    private void ApplyOneSidedLimit(Channel channel, AxisConfig? config)
    {
        if (config is null || (config.MinLimit is null && config.MaxLimit is null)) return;
        if (_domainLocks.ContainsKey(channel) || _zoomDomains.ContainsKey(channel)) return;
        if (_scales.TryGet(channel) is not LinearScale scale) return;

        double min = config.MinLimit ?? scale.Min;
        double max = config.MaxLimit ?? scale.Max;
        if (!(max > min)) return;

        scale.SetDomain(min, max);
        _baseDomains[channel] = (min, max);
    }

    /// <summary>
    /// Apply the pinned domains (<see cref="ScaleDomain(Channel, double, double)"/>). This runs after the
    /// fit, so it also locks a scale the caller installed by hand - and a channel that turned out to be
    /// categorical simply has no linear scale to pin, which is why a lock needs no knowledge of the field
    /// behind the channel.
    /// </summary>
    private void ApplyDomainLocks()
    {
        foreach (var (channel, (min, max)) in _domainLocks)
        {
            if (_scales.TryGet(channel) is LinearScale linear)
            {
                linear.SetDomain(min, max);
                // A pinned domain is the host's idea of the full range, so it is what a zoom is clamped to.
                _baseDomains[channel] = (min, max);
            }
        }
    }

    /// <summary>
    /// Collect the values every channel is encoded with: the chart-level encodes plus each mark's
    /// own (mark-level) encodes, using that mark's data when it carries its own.
    /// Null field values are skipped — they mean "missing" and must not make a numeric column look
    /// like a categorical one.
    /// </summary>
    private List<(Channel Channel, List<object> Values)> CollectEncodeValues(List<DataRow> renderData)
    {
        var collected = new Dictionary<Channel, List<object>>();

        void AddValues(Channel channel, IEncodeValue? encode, List<DataRow> data)
        {
            if (encode is not FieldEncode fieldEncode) return;
            if (!collected.TryGetValue(channel, out var list))
                collected[channel] = list = [];
            foreach (var row in data)
            {
                if (row.Has(fieldEncode.FieldName) && row.Get(fieldEncode.FieldName) is { } value)
                    list.Add(value);
            }
        }

        foreach (var channel in _encodes.Channels)
            AddValues(channel, _encodes.TryGet(channel), renderData);

        foreach (var mark in _marks)
        {
            var data = mark.Data ?? renderData;
            foreach (var channel in mark.LocalEncodeChannels)
                AddValues(channel, mark.GetLocalEncode(channel), data);
        }

        var result = new List<(Channel, List<object>)>(collected.Count);
        foreach (var entry in collected)
            result.Add((entry.Key, entry.Value));
        return result;
    }

    private static bool IsNumeric(object v)
        => v is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal;

    /// <summary>
    /// Values that can actually be fed to the inferred scale. A numeric column may still contain a
    /// stray non-numeric value (a failed parse, a placeholder string); passing it to
    /// <see cref="LinearScale.Fit"/> would throw and lose the whole frame, so such entries are
    /// dropped just like nulls. Non-finite numbers are dropped as well - they cannot define a
    /// range.
    /// </summary>
    private static List<object> FittableValues(List<object> values, bool numeric)
    {
        if (!numeric) return values;

        var result = new List<object>(values.Count);
        foreach (var value in values)
        {
            if (!IsNumeric(value)) continue;
            if (!double.IsFinite(ScaleConvert.ToDouble(value, nameof(Chart)))) continue;
            result.Add(value);
        }
        return result;
    }
}
