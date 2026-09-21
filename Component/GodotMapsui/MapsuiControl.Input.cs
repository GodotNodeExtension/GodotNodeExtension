// Copyright (c) GodotNodeExtension contributors.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Godot;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Projections;

namespace GodotNodeExtension.Component.GodotMapsui;

/// <summary>
/// Partial class handling Godot input events and translating them to Mapsui Navigator operations.
/// <para>
/// Two rules keep the behaviour predictable: every pointer position is mapped through
/// <c>ToMapPosition</c>, because Mapsui works in texture pixels and the texture can be larger than the
/// control (<see cref="MapsuiControl.PixelDensity"/>); and the map only treats a press-release as a tap
/// when the pointer barely moved, and never after a multi-touch gesture.
/// </para>
/// </summary>
public partial class MapsuiControl
{
    /// <summary>Movement in control pixels below which a press and release counts as a tap.</summary>
    private const float TapSlopPixels = 4f;

    private bool _isDragging;
    private Vector2 _lastDragPosition;
    private Vector2 _flingVelocity;
    private float _dragDistance;
    private DateTime _lastDragTime;

    // Touch/pinch state
    private bool _isTouchActive;
    private bool _multiTouchGesture;
    private readonly Dictionary<int, Vector2> _activeTouches = [];

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (!EnableInteraction || _map == null) return;

        switch (@event)
        {
            case InputEventMouseButton mouseButton:
                HandleMouseButton(mouseButton);
                break;
            case InputEventMouseMotion mouseMotion:
                HandleMouseMotion(mouseMotion);
                break;
            case InputEventScreenTouch screenTouch:
                HandleScreenTouch(screenTouch);
                break;
            case InputEventScreenDrag screenDrag:
                HandleScreenDrag(screenDrag);
                break;
            case InputEventMagnifyGesture magnify:
                HandleMagnifyGesture(magnify);
                break;
            case InputEventPanGesture pan:
                HandlePanGesture(pan);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (_map == null) return;

        var pos = mouseButton.Position;

        if (mouseButton.ButtonIndex == MouseButton.Left)
        {
            // Double click zooms in one level around the pointer, the convention on every map control.
            if (mouseButton is { Pressed: true, DoubleClick: true })
            {
                ZoomBy(1, pos, 200);
                AcceptEvent();
                return;
            }

            if (mouseButton.Pressed)
            {
                // Start dragging
                _isDragging = true;
                _lastDragPosition = pos;
                _flingVelocity = Vector2.Zero;
                _dragDistance = 0f;
                _lastDragTime = DateTime.UtcNow;
                AcceptEvent();
            }
            else
            {
                // End dragging
                if (_isDragging)
                {
                    _isDragging = false;

                    bool negligibleMovement = _dragDistance <= TapSlopPixels;

                    // Apply fling if enabled and the pointer was actually moving
                    if (EnableFling && !negligibleMovement && _flingVelocity.LengthSquared() > 100)
                    {
                        float scale = RenderScale;
                        _map.Navigator.Fling(_flingVelocity.X * scale, _flingVelocity.Y * scale, 1000);
                    }

                    // A click is a tap; a drag is not (it used to be decided by the release velocity,
                    // so a slow drag over half the map still counted as a tap).
                    if (negligibleMovement)
                    {
                        EmitMapTapped(pos);
                    }

                    AcceptEvent();
                }
            }
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            _map.Navigator.MouseWheelZoom(1, ToMapPosition(pos));
            _needsRedraw = true;
            AcceptEvent();
        }
        else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            _map.Navigator.MouseWheelZoom(-1, ToMapPosition(pos));
            _needsRedraw = true;
            AcceptEvent();
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (_map == null || !_isDragging) return;

        var currentPos = mouseMotion.Position;
        var previousScreenPos = ToMapPosition(_lastDragPosition);
        var currentScreenPos = ToMapPosition(currentPos);

        // Use Mapsui's Manipulation for drag
        var manipulation = new Manipulation(
            currentScreenPos,
            previousScreenPos,
            ScaleFactor: 1,
            RotationChange: 0,
            TotalRotationChange: 0
        );

        _map.Navigator.Manipulate(manipulation);

        // Track the travelled distance (for the tap decision) and the velocity (for the fling)
        var movement = currentPos - _lastDragPosition;
        _dragDistance += movement.Length();

        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastDragTime).TotalSeconds;
        if (timeDelta > 0)
        {
            _flingVelocity = movement / (float)timeDelta;
        }

        _lastDragPosition = currentPos;
        _lastDragTime = now;
        _needsRedraw = true;

        AcceptEvent();
    }

    private void HandleScreenTouch(InputEventScreenTouch screenTouch)
    {
        if (_map == null) return;

        if (screenTouch.Pressed)
        {
            _activeTouches[screenTouch.Index] = screenTouch.Position;
            if (_activeTouches.Count == 1)
            {
                // A fresh single-finger gesture: reset the tap bookkeeping
                _dragDistance = 0f;
                _multiTouchGesture = false;
            }
            else
            {
                // Two fingers or more: this gesture is a pinch/pan, never a tap
                _multiTouchGesture = true;
            }
        }
        else
        {
            _activeTouches.Remove(screenTouch.Index);

            // A single-finger tap: no movement and no second finger during the gesture.
            if (_activeTouches.Count == 0 && !_multiTouchGesture && _dragDistance <= TapSlopPixels)
            {
                EmitMapTapped(screenTouch.Position);
            }
        }

        _isTouchActive = _activeTouches.Count > 0;
        if (!_isTouchActive)
        {
            _multiTouchGesture = false;
            _pinchStarted = false;
        }
        AcceptEvent();
    }

    private void HandleScreenDrag(InputEventScreenDrag screenDrag)
    {
        if (_map == null || _activeTouches.Count == 0) return;

        _activeTouches[screenDrag.Index] = screenDrag.Position;
        _dragDistance += screenDrag.Relative.Length();

        if (_activeTouches.Count == 1)
        {
            // Single finger drag = pan
            var prev = ToMapPosition(screenDrag.Position - screenDrag.Relative);
            var current = ToMapPosition(screenDrag.Position);

            var manipulation = new Manipulation(
                current,
                prev,
                ScaleFactor: 1,
                RotationChange: 0,
                TotalRotationChange: 0
            );

            _map.Navigator.Manipulate(manipulation);
            _needsRedraw = true;
        }
        else if (_activeTouches.Count == 2)
        {
            // Two fingers: pinch and pan together
            HandleTwoFingerPinch();
        }

        AcceptEvent();
    }

    private Vector2 _lastPinchCenter;
    private float _lastPinchDistance;
    private bool _pinchStarted;

    private void HandleTwoFingerPinch()
    {
        if (_map == null || _activeTouches.Count != 2) return;

        var touches = new List<Vector2>(_activeTouches.Values);
        var center = (touches[0] + touches[1]) * 0.5f;
        var distance = touches[0].DistanceTo(touches[1]);

        if (!_pinchStarted)
        {
            _lastPinchCenter = center;
            _lastPinchDistance = distance;
            _pinchStarted = true;
            return;
        }

        if (_lastPinchDistance > 0 && distance > 0)
        {
            var scaleFactor = distance / _lastPinchDistance;
            var manipulation = new Manipulation(
                ToMapPosition(center),
                ToMapPosition(_lastPinchCenter),
                ScaleFactor: scaleFactor,
                RotationChange: 0,
                TotalRotationChange: 0
            );

            _map.Navigator.Manipulate(manipulation);
            _needsRedraw = true;
        }

        _lastPinchCenter = center;
        _lastPinchDistance = distance;
    }

    private void HandleMagnifyGesture(InputEventMagnifyGesture magnify)
    {
        if (_map == null) return;

        _map.Navigator.MouseWheelZoomContinuous(magnify.Factor, ToMapPosition(magnify.Position));
        _needsRedraw = true;
        AcceptEvent();
    }

    /// <summary>
    /// Trackpad two-finger pan (and the equivalent on some touch devices). Without this the gesture was
    /// silently ignored - only drags moved the map.
    /// </summary>
    private void HandlePanGesture(InputEventPanGesture pan)
    {
        if (_map == null) return;

        var delta = pan.Delta;
        if (delta == Vector2.Zero) return;

        var manipulation = new Manipulation(
            ToMapPosition(pan.Position),
            ToMapPosition(pan.Position - delta),
            ScaleFactor: 1,
            RotationChange: 0,
            TotalRotationChange: 0
        );

        _map.Navigator.Manipulate(manipulation);
        _needsRedraw = true;
        AcceptEvent();
    }

    private void EmitMapTapped(Vector2 screenPosition)
    {
        if (_map == null || _mapRenderer == null) return;

        var screenPos = ToMapPosition(screenPosition);
        var worldPos = _map.Navigator.Viewport.ScreenToWorld(screenPos);
        var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);

        // Create GetMapInfo delegates for Mapsui's event system
        MapInfo LocalGetMapInfo(ScreenPosition sp, IEnumerable<ILayer> layers) =>
            _mapRenderer.GetMapInfo(sp, _map.Navigator.Viewport, layers, _map.RenderService);

        System.Threading.Tasks.Task<MapInfo> LocalGetRemoteMapInfoAsync(
            ScreenPosition sp, Mapsui.Viewport vp, IEnumerable<ILayer> layers) =>
            System.Threading.Tasks.Task.FromResult(
                _mapRenderer.GetMapInfo(sp, vp, layers, _map.RenderService));

        // Trigger Map's Tapped event so Mapsui widgets (callouts etc.) can respond
        var worldPoint = new MPoint(worldPos.X, worldPos.Y);
        _map.OnTapped(new MapEventArgs(
            screenPos,
            worldPoint,
            GestureType.SingleTap,
            _map,
            LocalGetMapInfo,
            LocalGetRemoteMapInfoAsync));

        // Perform feature hit-test on all layers
        var mapInfo = LocalGetMapInfo(screenPos, _map.Layers);

        if (mapInfo.Feature != null)
        {
            EmitSignal(SignalName.MapFeatureTapped,
                mapInfo.Layer != null ? mapInfo.Layer.Name : "",
                mapInfo.Feature.Id,
                lonLat.lat, lonLat.lon);
        }
        else
        {
            EmitSignal(SignalName.MapBackgroundTapped, lonLat.lat, lonLat.lon);
        }

        // Always emit the generic tap signal for backward compatibility
        EmitSignal(SignalName.MapTapped, lonLat.lat, lonLat.lon);
    }
}
