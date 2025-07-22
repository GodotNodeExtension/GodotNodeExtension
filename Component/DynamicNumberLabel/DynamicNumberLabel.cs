using System;
using Godot;
using Microsoft.Extensions.Logging;

namespace GodotNodeExtension.DynamicNumberLabel;

/// <summary>
/// DynamicNumberLabel is a custom Godot Label node designed to display numbers with optional prefix, suffix,
/// and formatting. It allows for dynamic updating of the displayed value, including the use of delimiters for
/// better readability of large numbers. Supports animated transitions with customizable duration and easing.
/// </summary>
[Tool]
[GlobalClass]
public partial class DynamicNumberLabel : Label
{
    [Export]
    public string Prefix { get; set; } = "";
    
    [Export]
    public string Suffix { get; set; } = "";
    
    [Export]
    public int Value { get; set; }

    [Export]
    public bool UseDelimiter { get; set; } = true;

    [Export]
    public string Delimiter { get; set; } = ",";

    [Export]
    public float AnimationDuration { get; set; } = 1.0f;

    [Export]
    public Tween.EaseType EaseType { get; set; } = Tween.EaseType.Out;

    [Export]
    public Tween.TransitionType TransitionType { get; set; } = Tween.TransitionType.Cubic;

    [Export]
    public bool AnimateOnReady { get; set; }

    [Export]
    public bool RandomMode { get; set; }

    [Export]
    public float RandomUpdateInterval { get; set; } = 0.05f;

    [Export]
    public int MaxDigits { get; set; } = 0;

    private Tween _tween;
    private Timer _randomTimer;
    private int _currentDisplayValue;
    private int _targetValue;
    private int _startValue;
    private float _animationProgress;
    private readonly Random _random = new Random();
    private static ILogger Logger;

    public override void _Ready()
    {
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
    public void StartAnimation(int startValue, int targetValue)
    {
        _targetValue = ClampToMaxDigits(targetValue);
        _startValue = ClampToMaxDigits(startValue);
        Value = _targetValue;
        
        // Kill existing tween if it exists
        if (_tween != null && _tween.IsValid())
        {
            _tween.Kill();
        }
        
        // Clean up existing random timer
        if (_randomTimer != null)
        {
            _randomTimer.QueueFree();
            _randomTimer = null;
        }
        
        // Set initial value
        _currentDisplayValue = startValue;
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
        // Create new tween only when needed
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.SetEase(EaseType);
        _tween.SetTrans(TransitionType);
        
        // Start animation
        _tween.TweenMethod(Callable.From<float>(OnTweenUpdate), (float)_startValue, (float)_targetValue, AnimationDuration);
        _tween.TweenCallback(Callable.From(OnAnimationComplete));
    }

    private void StartRandomAnimation()
    {
        _animationProgress = 0.0f;
        
        // Create timer for random updates
        _randomTimer = new Timer();
        _randomTimer.WaitTime = RandomUpdateInterval;
        _randomTimer.Timeout += OnRandomTimerTimeout;
        AddChild(_randomTimer);
        _randomTimer.Start();
        
        // Create tween for overall progress
        _tween = CreateTween();
        _tween.SetPauseMode(Tween.TweenPauseMode.Process);
        _tween.SetEase(EaseType);
        _tween.SetTrans(TransitionType);
        
        _tween.TweenMethod(Callable.From<float>(OnRandomProgressUpdate), 0.0f, 1.0f, AnimationDuration);
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
    /// Set value immediately without animation
    /// </summary>
    /// <param name="value">The value to set</param>
    public void SetValueInstant(int value)
    {
        value = ClampToMaxDigits(value);
        Value = value;
        _currentDisplayValue = value;
        _targetValue = value;
        
        // Kill existing tween
        if (_tween != null && _tween.IsValid())
        {
            _tween.Kill();
        }
        
        UpdateDisplay();
    }

    /// <summary>
    /// Stop current animation and set to target value
    /// </summary>
    public void StopAnimation()
    {
        if (_tween != null && _tween.IsValid())
        {
            _tween.Kill();
        }
        
        if (_randomTimer != null)
        {
            _randomTimer.QueueFree();
            _randomTimer = null;
        }
        
        _currentDisplayValue = _targetValue;
        UpdateDisplay();
    }

    /// <summary>
    /// Check if animation is currently playing
    /// </summary>
    public bool IsAnimating()
    {
        return (_tween != null && _tween.IsValid() && _tween.IsRunning()) || 
               (_randomTimer != null && !_randomTimer.IsStopped());
    }

    private void OnTweenUpdate(float value)
    {
        _currentDisplayValue = Mathf.RoundToInt(value);
        UpdateDisplay();
    }

    private void OnAnimationComplete()
    {
        // Clean up random timer
        if (_randomTimer != null)
        {
            _randomTimer.QueueFree();
            _randomTimer = null;
        }
        
        _currentDisplayValue = _targetValue;
        UpdateDisplay();
        EmitSignal(SignalName.AnimationFinished);
    }

    private void UpdateDisplay()
    {
        string numberText = FormatNumber(_currentDisplayValue);
        Text = $"{Prefix}{numberText}{Suffix}";
    }

    private int ClampToMaxDigits(int value)
    {
        if (MaxDigits <= 0) return value; // No limit when MaxDigits is 0 or negative

        // Calculate maximum value for the specified number of digits
        int maxValue = (int)Math.Pow(10, MaxDigits) - 1;
        
        if (value > maxValue)
        {
            Logger.LogWarning("Value {Value} exceeds maximum digits limit ({MaxDigits} digits, max value: {MaxValue}). Clamping to maximum.", 
                value, MaxDigits, maxValue);
            return maxValue;
        }
        
        // For negative values, ensure they don't exceed the digit limit when formatted
        if (value < 0)
        {
            int minValue = -(int)Math.Pow(10, Math.Max(1, MaxDigits - 1)) + 1; // Reserve one digit for minus sign
            if (value < minValue)
            {
                Logger.LogWarning("Negative value {Value} exceeds maximum digits limit ({MaxDigits} digits, min value: {MinValue}). Clamping to minimum.", 
                    value, MaxDigits, minValue);
                return minValue;
            }
        }
        
        return value;
    }

    private string FormatNumber(int number)
    {
        if (!UseDelimiter)
        {
            return FormatWithPadding(number);
        }

        string formattedNumber = number.ToString("N0").Replace(",", Delimiter);
        return ApplyPadding(formattedNumber, number);
    }

    private string FormatWithPadding(int number)
    {
        if (MaxDigits <= 0) return number.ToString();

        // For positive numbers, pad with zeros
        if (number >= 0)
        {
            return number.ToString($"D{MaxDigits}");
        }
        
        // For negative numbers, pad the absolute value and add minus sign
        string absString = Math.Abs(number).ToString($"D{Math.Max(1, MaxDigits - 1)}");
        return "-" + absString;
    }

    private string ApplyPadding(string formattedNumber, int originalNumber)
    {
        if (MaxDigits <= 0) return formattedNumber;

        // Remove delimiters temporarily to count digits
        string digitsOnly = formattedNumber.Replace(Delimiter, "");
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
    [Signal]
    public delegate void AnimationFinishedEventHandler();

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
    public void SetAnimationSettings(float duration, Tween.EaseType easeType, Tween.TransitionType transitionType, bool randomMode = false, float randomUpdateInterval = 0.05f)
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
    /// <param name="newValue">The new target value</param>
    public void AnimateToValue(int newValue)
    {
        int oldValue = Value;
        newValue = ClampToMaxDigits(newValue);
        StartAnimation(_currentDisplayValue, newValue);
        EmitSignal(SignalName.ValueChanged, oldValue, newValue);
    }
}