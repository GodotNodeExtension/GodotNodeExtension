using System;
using System.Globalization;
using Godot;

namespace GodotNodeExtension.Component.DynamicNumberLabel;

/// <summary>
/// DynamicNumberLabel is a custom Godot Label node designed to display numbers with optional prefix, suffix,
/// and formatting. It allows for dynamic updating of the displayed value, including the use of delimiters for
/// better readability of large numbers. Supports animated transitions with customizable duration and easing.
/// <para>
/// Only clamped values are ever stored or rendered: <see cref="MaxDigits"/> bounds what the label accepts
/// (an out-of-range value is clamped with a warning) and shorter numbers are padded with leading zeros so the
/// label keeps a stable width.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class DynamicNumberLabel : Label
{
    /// <summary>Digits needed by the whole <see cref="int"/> range; a wider limit cannot clamp anything.</summary>
    private const int FullIntRangeDigits = 10;

    /// <summary>Update interval used when <see cref="RandomUpdateInterval"/> is not positive.</summary>
    private const float DefaultRandomUpdateInterval = 0.05f;

    /// <summary>Text placed before the formatted number.</summary>
    [Export]
    public string Prefix { get; set; } = "";
    
    /// <summary>Text placed after the formatted number.</summary>
    [Export]
    public string Suffix { get; set; } = "";
    
    /// <summary>
    /// Value to display. Assigning it only stores the value: the text is refreshed by StartAnimation,
    /// AnimateToValue, SetValueInstant or when the node enters the tree. Values outside <see cref="MaxDigits"/>
    /// are clamped. See <see cref="AnimateToValue"/> for the signal that reports a change.
    /// </summary>
    [Export]
    public int Value { get; set; }

    /// <summary>Whether thousands separators are inserted into the formatted number.</summary>
    [Export]
    public bool UseDelimiter { get; set; } = true;

    /// <summary>Thousands separator used when <see cref="UseDelimiter"/> is enabled.</summary>
    [Export]
    public string Delimiter { get; set; } = ",";

    /// <summary>Duration of the value animation in seconds.</summary>
    [Export]
    public float AnimationDuration { get; set; } = 1.0f;

    /// <summary>Easing applied to the value animation.</summary>
    [Export]
    public Tween.EaseType EaseType { get; set; } = Tween.EaseType.Out;

    /// <summary>Transition curve applied to the value animation.</summary>
    [Export]
    public Tween.TransitionType TransitionType { get; set; } = Tween.TransitionType.Cubic;

    /// <summary>Whether the value animates up from zero when the node enters the tree.</summary>
    [Export]
    public bool AnimateOnReady { get; set; }

    /// <summary>When true, StartAnimation picks a randomized interpolation instead of a linear one. It does not refresh on its own; values only randomize while an animation is running.</summary>
    [Export]
    public bool RandomMode { get; set; }

    /// <summary>
    /// Seconds between random value updates in random mode. A non-positive value falls back to
    /// <see cref="DefaultRandomUpdateInterval"/>: a Godot timer rejects a wait time of zero.
    /// </summary>
    [Export]
    public float RandomUpdateInterval { get; set; } = DefaultRandomUpdateInterval;

    /// <summary>
    /// Maximum number of digits rendered (keeps the label width stable): values outside the range are clamped
    /// and shorter ones padded with leading zeros. 0 disables the limit; 10 or more covers every int value and
    /// therefore only pads.
    /// </summary>
    [Export]
    public int MaxDigits { get; set; }

    private Tween? _tween;
    private Timer? _randomTimer;
    private int _currentDisplayValue;
    private int _targetValue;
    private int _startValue;
    private float _animationProgress;
    private readonly Random _random = new Random();

    /// <inheritdoc />
    public override void _Ready()
    {
        // A scene can carry a value the digit limit forbids: only the clamped one is kept.
        Value = ClampToMaxDigits(Value);
        _targetValue = Value;

        // Don't create tween in _Ready, create it when needed
        if (AnimateOnReady)
        {
            StartAnimation();
        }
        else
        {
            _currentDisplayValue = Value;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Start the number animation from 0 to the target Value
    /// </summary>
    public void StartAnimation()
    {
        StartAnimation(Value);
    }

    /// <summary>
    /// Start the number animation from 0 to the specified target value
    /// </summary>
    /// <param name="targetValue">The target value to animate to</param>
    public void StartAnimation(int targetValue)
    {
        StartAnimation(0, targetValue);
    }

    /// <summary>
    /// Start the number animation from startValue to targetValue
    /// </summary>
    /// <param name="startValue">The starting value for animation</param>
    /// <param name="targetValue">The target value to animate to</param>
    /// <remarks>
    /// Both values are clamped to <see cref="MaxDigits"/>. Outside the SceneTree there is nothing to drive a
    /// tween, so the target value is applied immediately instead of leaving an animation that never runs.
    /// </remarks>
    public void StartAnimation(int startValue, int targetValue)
    {
        _targetValue = ClampToMaxDigits(targetValue);
        _startValue = ClampToMaxDigits(startValue);
        Value = _targetValue;

        CancelAnimation();

        if (!IsInsideTree())
        {
            _currentDisplayValue = _targetValue;
            UpdateDisplay();
            return;
        }

        // Show the clamped start value; the tween (or the random timer) takes over from here.
        _currentDisplayValue = _startValue;
        UpdateDisplay();

        if (RandomMode)
        {
            StartRandomAnimation();
        }
        else
        {
            StartLinearAnimation();
        }
    }

    private void StartLinearAnimation()
    {
        // Create a new tween only when needed. The default (bound) pause mode keeps the animation in
        // step with the node, so pausing the tree pauses the counter instead of letting it run on.
        _tween = CreateTween();
        _tween.SetEase(EaseType);
        _tween.SetTrans(TransitionType);

        // Start animation
        _tween.TweenMethod(Callable.From<float>(OnTweenUpdate), (float)_startValue, (float)_targetValue, Mathf.Max(0f, AnimationDuration));
        _tween.TweenCallback(Callable.From(OnAnimationComplete));
    }

    private void StartRandomAnimation()
    {
        _animationProgress = 0.0f;

        // Create timer for random updates
        _randomTimer = new Timer
        {
            WaitTime = RandomUpdateInterval > 0f ? RandomUpdateInterval : DefaultRandomUpdateInterval,
        };
        _randomTimer.Timeout += OnRandomTimerTimeout;
        AddChild(_randomTimer);
        _randomTimer.Start();

        // Create tween for overall progress
        _tween = CreateTween();
        _tween.SetEase(EaseType);
        _tween.SetTrans(TransitionType);

        _tween.TweenMethod(Callable.From<float>(OnRandomProgressUpdate), 0.0f, 1.0f, Mathf.Max(0f, AnimationDuration));
        _tween.TweenCallback(Callable.From(OnAnimationComplete));
    }

    private void OnRandomTimerTimeout()
    {
        if (_animationProgress >= 1.0f) return;
        
        // Calculate current target based on progress
        float progressValue = Mathf.Lerp(_startValue, _targetValue, _animationProgress);
        
        // Calculate random value within bounds (startValue to targetValue only)
        int minBound = Mathf.Min(_startValue, _targetValue);
        int maxBound = Mathf.Max(_startValue, _targetValue);
        
        // Add random variation, but keep it within the min-max bounds
        // The closer to completion, the closer to the target value
        float randomRange = (maxBound - minBound) * (1.0f - _animationProgress * 0.7f); // Reduce randomness as we approach target
        float randomOffset = (float)(_random.NextDouble() - 0.5) * 2.0f * randomRange;
        
        int randomValue = Mathf.RoundToInt(progressValue + randomOffset);
        
        // Strictly clamp to min-max bounds (no values outside startValue-targetValue range)
        randomValue = Mathf.Clamp(randomValue, minBound, maxBound);
        
        _currentDisplayValue = randomValue;
        UpdateDisplay();
    }

    private void OnRandomProgressUpdate(float progress)
    {
        _animationProgress = progress;
    }

    /// <summary>
    /// Set value immediately without animation, cancelling any animation that is running.
    /// </summary>
    /// <param name="value">The value to set</param>
    public void SetValueInstant(int value)
    {
        CancelAnimation();

        value = ClampToMaxDigits(value);
        Value = value;
        _targetValue = value;
        _startValue = value;
        _currentDisplayValue = value;

        UpdateDisplay();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // A running tween is engine-owned and holds Callables into this assembly: leaving the tree (a
        // scene reload in the editor, for example) has to release it, otherwise the tween keeps firing
        // into a detached node and keeps the old assembly referenced across a hot reload (godot#78513).
        CancelAnimation();
    }

    /// <summary>
    /// Stop current animation and set to target value
    /// </summary>
    public void StopAnimation()
    {
        CancelAnimation();

        int target = ClampToMaxDigits(Value);
        Value = target;
        _targetValue = target;
        _currentDisplayValue = target;

        UpdateDisplay();
    }

    /// <summary>
    /// Check if animation is currently playing
    /// </summary>
    public bool IsAnimating()
    {
        return _tween != null && _tween.IsValid() && _tween.IsRunning();
    }

    /// <summary>
    /// Cancels a running animation: its tween is killed and the random update timer released.
    /// </summary>
    private void CancelAnimation()
    {
        if (_tween != null && _tween.IsValid())
        {
            _tween.Kill();
        }

        _tween = null;
        ReleaseRandomTimer();
    }

    /// <summary>
    /// Releases the random update timer synchronously: <c>QueueFree()</c> would defer the deletion to the
    /// end of the frame, leaving two timers updating the label after a restart.
    /// </summary>
    private void ReleaseRandomTimer()
    {
        if (_randomTimer == null) return;

        _randomTimer.Stop();
        _randomTimer.Timeout -= OnRandomTimerTimeout;
        if (_randomTimer.GetParent() == this)
        {
            RemoveChild(_randomTimer);
        }

        _randomTimer.Free();
        _randomTimer = null;
    }

    private void OnTweenUpdate(float value)
    {
        _currentDisplayValue = Mathf.RoundToInt(value);
        UpdateDisplay();
    }

    private void OnAnimationComplete()
    {
        // The tween is already finished (and therefore invalid) when this callback runs; only the
        // random update timer is left to release.
        _tween = null;
        ReleaseRandomTimer();

        _currentDisplayValue = _targetValue;
        UpdateDisplay();
        EmitSignal(SignalName.AnimationFinished);
    }

    private void UpdateDisplay()
    {
        string numberText = FormatNumber(_currentDisplayValue);
        Text = $"{Prefix}{numberText}{Suffix}";
    }

    /// <summary>
    /// Clamps a value to the range <see cref="MaxDigits"/> can render. A clamp is reported with a warning:
    /// silently showing a different number than the caller asked for would be worse than a log line.
    /// </summary>
    private int ClampToMaxDigits(int value)
    {
        // No limit when MaxDigits is 0 or negative. 10 digits already cover every int value, and a
        // wider bound is impossible to express: 10 ^ MaxDigits overflows an int long before that.
        if (MaxDigits <= 0 || MaxDigits >= FullIntRangeDigits) return value;

        int maxValue = (int)Pow10(MaxDigits) - 1;
        if (value > maxValue)
        {
            GD.PushWarning($"DynamicNumberLabel: {value} does not fit in {MaxDigits} digits, clamped to {maxValue}.");
            return maxValue;
        }

        // Negative values reserve one digit for the minus sign.
        int minValue = -(int)Pow10(Math.Max(1, MaxDigits - 1)) + 1;
        if (value < minValue)
        {
            GD.PushWarning($"DynamicNumberLabel: {value} does not fit in {MaxDigits} digits, clamped to {minValue}.");
            return minValue;
        }

        return value;
    }

    /// <summary>10 raised to <paramref name="digits"/>; callers keep it small enough to fit in an int.</summary>
    private static long Pow10(int digits)
    {
        long value = 1;
        for (int i = 0; i < digits; i++)
        {
            value *= 10;
        }

        return value;
    }

    private string FormatNumber(int number)
    {
        // An empty delimiter means "no grouping"; it must never reach string.Replace, which throws on it.
        if (!UseDelimiter || string.IsNullOrEmpty(Delimiter))
        {
            return FormatWithPadding(number);
        }

        // "N0" groups with the separator of the current culture; formatting with the invariant culture
        // keeps the comma that Delimiter replaces, whatever the machine's locale is.
        string formattedNumber = number.ToString("N0", CultureInfo.InvariantCulture).Replace(",", Delimiter);
        return ApplyPadding(formattedNumber, number);
    }

    private string FormatWithPadding(int number)
    {
        if (MaxDigits <= 0) return number.ToString(CultureInfo.InvariantCulture);

        // For positive numbers, pad with zeros
        if (number >= 0)
        {
            return number.ToString($"D{MaxDigits}", CultureInfo.InvariantCulture);
        }

        // For negative numbers, pad the absolute value and add the minus sign. Math.Abs(int) throws on
        // int.MinValue, so the absolute value is computed in long.
        string absString = Math.Abs((long)number).ToString($"D{Math.Max(1, MaxDigits - 1)}", CultureInfo.InvariantCulture);
        return "-" + absString;
    }

    private string ApplyPadding(string formattedNumber, int originalNumber)
    {
        if (MaxDigits <= 0) return formattedNumber;

        // Remove delimiters temporarily to count digits
        string digitsOnly = string.IsNullOrEmpty(Delimiter) ? formattedNumber : formattedNumber.Replace(Delimiter, "");
        bool isNegative = originalNumber < 0;

        if (isNegative)
        {
            digitsOnly = digitsOnly.Replace("-", "");
        }

        int currentDigits = digitsOnly.Length;
        int targetDigits = isNegative ? Math.Max(1, MaxDigits - 1) : MaxDigits;

        if (currentDigits < targetDigits)
        {
            // Add leading zeros
            int zerosToAdd = targetDigits - currentDigits;
            string zeros = new string('0', zerosToAdd);
            
            if (isNegative)
            {
                // Insert zeros after the minus sign
                return formattedNumber.Replace("-", "-" + zeros);
            }
            else
            {
                return zeros + formattedNumber;
            }
        }

        return formattedNumber;
    }

    // Signals
    /// <summary>Raised when an animation reached its target. Cancelling one (StopAnimation, SetValueInstant or a restart) does not raise it.</summary>
    [Signal]
    public delegate void AnimationFinishedEventHandler();

    /// <summary>
    /// Raised by <see cref="AnimateToValue"/> before the new animation starts; carries the previous target
    /// value and the new (clamped) one. Other paths that change the displayed value (StartAnimation,
    /// SetValueInstant, _Ready) do not emit it.
    /// </summary>
    [Signal]
    public delegate void ValueChangedEventHandler(int oldValue, int newValue);

    /// <summary>
    /// Set animation parameters
    /// </summary>
    /// <param name="duration">Animation duration in seconds</param>
    /// <param name="easeType">Easing type</param>
    /// <param name="transitionType">Transition type</param>
    /// <param name="randomMode">Enable random mode</param>
    /// <param name="randomUpdateInterval">Update interval for random mode</param>
    public void SetAnimationSettings(float duration, Tween.EaseType easeType, Tween.TransitionType transitionType, bool randomMode = false, float randomUpdateInterval = DefaultRandomUpdateInterval)
    {
        AnimationDuration = duration;
        EaseType = easeType;
        TransitionType = transitionType;
        RandomMode = randomMode;
        RandomUpdateInterval = randomUpdateInterval;
    }

    /// <summary>
    /// Animate to a new value from current displayed value
    /// </summary>
    /// <param name="newValue">The new target value, clamped to <see cref="MaxDigits"/></param>
    /// <remarks>
    /// The animation starts from the value that is currently on screen. <see cref="ValueChangedEventHandler"/>
    /// reports the previous target value and the clamped new one, and is raised even when the target does not
    /// actually change.
    /// </remarks>
    public void AnimateToValue(int newValue)
    {
        int oldValue = Value;
        newValue = ClampToMaxDigits(newValue);
        StartAnimation(_currentDisplayValue, newValue);
        EmitSignal(SignalName.ValueChanged, oldValue, newValue);
    }
}