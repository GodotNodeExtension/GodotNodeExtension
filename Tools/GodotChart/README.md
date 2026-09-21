# GodotChart tools - the documentation images

`Doc/GodotChart/assets/*.png` are the pictures the component's documentation shows. They are
generated rather than drawn by hand, so they can be regenerated whenever the charts change:

```bash
python Tools/GodotChart/capture.py            # regenerate every image
python Tools/GodotChart/capture.py --check    # only verify what is on disk
```

## What produces them

`capture.py` runs `Test/GodotChart/Support/DocsCapture.tscn` **with a real rendering device** (not
headless - a headless run would save empty surfaces). That scene walks
`Example/GodotChart/BasicsDemo.tscn`, which holds one cell per chart kind plus one per variant (a
grouped bar, a stacked area, a line chart with reference lines), and for each cell:

1. takes its `ChartView` out of the example browser's grid,
2. re-parents it into a plain `Control` of the size the picture wants,
3. waits for the entry animation to stop changing the surface,
4. saves the surface as `Doc/GodotChart/assets/<file>.png`.

The plan - one `("Cell", "file", width, height)` tuple per image - lives in that scene's source, and
`capture.py --check` reads the expected sizes from there, so the two cannot drift apart.

Capturing a demo cell where it sits does not work: a cell is a third of the example browser's grid, so
the picture inherits whatever aspect the browser gives it. That is exactly how an earlier batch of
these images ended up taller than wide.

## Canvas sizes

| Charts | Canvas | Why |
| --- | --- | --- |
| Cartesian (bar, line, area, scatter, range area, box, candlestick, heatmap, violin, treemap, sankey, waffle, timeline, lollipop, milestone, reference lines) | **960 x 600** | A landscape frame is the shape these charts read in: a wide axis, room for tick labels and a legend |
| Polar and relational (pie, donut, radar, sunburst, chord, gauge, funnel) | **720 x 720** | Their marks declare a square content shape (the chart fills the plot's short edge), so a landscape frame would only add empty margins |

Every image uses the same colour theme as the component's default (dark), because the documentation
is read on the default theme.

## Why the pieces live where they do

A development tool must never sit under `Component/`: the installer ships `Component/<Name>/`,
`Doc/<Name>/` and `Example/<Name>/` to the user, so anything there is delivered with the component.
The driver is therefore here (`Tools/GodotChart/`, which is not shipped), and the capture scene lives
under `Test/` - it has to be compiled, and `Test/` is not shipped either.
