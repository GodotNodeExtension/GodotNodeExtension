using System.Collections.Generic;

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
        foreach (Channel ch in _encodes.Channels)
        {
            if (_scales.Has(ch))  continue; // Skip if manually set

            if (_encodes.TryGet(ch) is not FieldEncode fe) continue;

            var values = new List<object>();
            foreach (var row in renderData)
            {
                if (row.Has(fe.FieldName))
                    values.Add(row.Get(fe.FieldName));
            }
            if (values.Count == 0) continue;

            // Auto-select scale based on value type
            IScale autoScale = IsNumeric(values[0])
                ? new LinearScale()
                : ch == Channel.Color ? new ColorScale { Palette = _theme.Palette } : new OrdinalScale();

            autoScale.Fit(values);
            _scales.Set(ch, autoScale);
            _autoFittedChannels.Add(ch);
        }

        // Auto-fit Y2 scale from marks that use Y2 channel
        if (!_scales.Has(Channel.Y2))
        {
            var y2Values = new List<object>();
            foreach (var mark in _marks)
            {
                if (mark.YChannel != Channel.Y2) continue;
                var data = mark.Data ?? renderData;
                // Use the mark's local Y encode field, or fall back to chart-level Y encode
                string? fieldName = mark.GetLocalEncodeField(Channel.Y)
                    ?? (_encodes.TryGet(Channel.Y) is FieldEncode yfe ? yfe.FieldName : null);
                if (fieldName == null) continue;
                foreach (var row in data)
                {
                    if (row.Has(fieldName))
                        y2Values.Add(row.Get(fieldName));
                }
            }
            if (y2Values.Count > 0 && IsNumeric(y2Values[0]))
            {
                var y2Scale = new LinearScale();
                y2Scale.Fit(y2Values);
                _scales.Set(Channel.Y2, y2Scale);
                _autoFittedChannels.Add(Channel.Y2);
            }
        }
    }

    private static bool IsNumeric(object v)
        => v is int or float or double or long or decimal;
}
