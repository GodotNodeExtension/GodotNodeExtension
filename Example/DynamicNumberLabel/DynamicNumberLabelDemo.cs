using System;
using Godot;

namespace GodotNodeExtension.Example.DynamicNumberLabel;

public partial class DynamicNumberLabelDemo : Control
{
    // Export references to UI controls from tscn file
    [Export] public SpinBox ValueSpinBox { get; set; } = null!;
    [Export] public SpinBox DurationSpinBox { get; set; } = null!;
    [Export] public OptionButton EasingOption { get; set; } = null!;
    [Export] public CheckBox RandomModeCheckBox { get; set; } = null!;
    [Export] public Button StartButton { get; set; } = null!;
    [Export] public Button ResetButton { get; set; } = null!;
    [Export] public Component.DynamicNumberLabel.DynamicNumberLabel ScoreNumber { get; set; } = null!;
    [Export] public Component.DynamicNumberLabel.DynamicNumberLabel HealthNumber { get; set; } = null!;
    [Export] public Component.DynamicNumberLabel.DynamicNumberLabel MoneyNumber { get; set; } = null!;

    public override void _Ready()
    {
        ConnectSignals();
    }

    private void ConnectSignals()
    {
        StartButton.Pressed += OnStartPressed;
        ResetButton.Pressed += OnResetPressed;
        RandomModeCheckBox.Toggled += OnRandomModeToggled;
    }

    public override void _ExitTree()
    {
        StartButton.Pressed -= OnStartPressed;
        ResetButton.Pressed -= OnResetPressed;
        RandomModeCheckBox.Toggled -= OnRandomModeToggled;
    }

    private void OnStartPressed()
    {
        var targetValue = (int)ValueSpinBox.Value;
        var duration = (float)DurationSpinBox.Value;
        var easingType = GetSelectedEasingType();
        var transitionType = GetSelectedTransitionType();

        // Update all labels with new settings
        ScoreNumber.AnimationDuration = duration;
        ScoreNumber.EaseType = easingType;
        ScoreNumber.TransitionType = transitionType;
        ScoreNumber.StartAnimation(targetValue);

        HealthNumber.AnimationDuration = duration;
        HealthNumber.EaseType = easingType;
        HealthNumber.TransitionType = transitionType;
        HealthNumber.MaxDigits = 3; // Limit health to 3 digits
        HealthNumber.StartAnimation(Math.Min(targetValue, 999)); // Respect max digits

        MoneyNumber.Suffix = " $"; // Add currency suffix
        MoneyNumber.AnimationDuration = duration * 1.5f; // Slightly longer for money
        MoneyNumber.EaseType = easingType;
        MoneyNumber.TransitionType = transitionType;
        MoneyNumber.StartAnimation(targetValue);
    }

    private void OnResetPressed()
    {
        ScoreNumber.SetValueInstant(0);
        HealthNumber.SetValueInstant(0);
        MoneyNumber.SetValueInstant(0);
    }

    private void OnRandomModeToggled(bool pressed)
    {
        MoneyNumber.RandomMode = pressed;
    }

    private Tween.EaseType GetSelectedEasingType()
    {
        return EasingOption.Selected switch
        {
            0 => Tween.EaseType.In, // Linear uses In for simplicity
            1 => Tween.EaseType.In,
            2 => Tween.EaseType.Out,
            3 => Tween.EaseType.InOut,
            4 => Tween.EaseType.Out, // Bounce
            5 => Tween.EaseType.Out, // Elastic
            _ => Tween.EaseType.Out
        };
    }

    private Tween.TransitionType GetSelectedTransitionType()
    {
        return EasingOption.Selected switch
        {
            0 => Tween.TransitionType.Linear,
            1 => Tween.TransitionType.Cubic,
            2 => Tween.TransitionType.Cubic,
            3 => Tween.TransitionType.Cubic,
            4 => Tween.TransitionType.Bounce,
            5 => Tween.TransitionType.Elastic,
            _ => Tween.TransitionType.Cubic
        };
    }
}
