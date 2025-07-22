# DynamicNumberLabel

A custom Godot Label node designed for animated number display with customizable formatting, prefixes, suffixes, and smooth transitions.

## Features

- **Animated Number Transitions**: Smooth animations from any value to another with customizable duration and easing
- **Formatting Options**: Support for number delimiters, prefixes, and suffixes
- **Multiple Animation Modes**: Start from zero, animate between values, or set instantly
- **Customizable Easing**: Choose from various easing types and transition curves
- **Signal Support**: Get notified when animations complete or values change

## Properties

### Display Settings
- `Value` (int): The target value to display
- `Prefix` (string): Text to display before the number (e.g., "Score: ")
- `Suffix` (string): Text to display after the number (e.g., " pts")
- `UseDelimiter` (bool): Whether to use delimiters for large numbers
- `Delimiter` (string): Character to use as delimiter (default: ",")
- `MaxDigits` (int): Maximum number of digits allowed (0 = no limit, auto-pads with zeros)

### Animation Settings
- `AnimationDuration` (float): Duration of the animation in seconds (default: 1.0)
- `EaseType` (Tween.EaseType): Easing curve type (In/Out/InOut)
- `TransitionType` (Tween.TransitionType): Transition curve (Linear/Sine/Cubic/etc.)
- `AnimateOnReady` (bool): Whether to start animation automatically when ready
- `RandomMode` (bool): Enable random number fluctuation instead of linear progression
- `RandomUpdateInterval` (float): Update frequency for random mode in seconds (default: 0.05)

## Methods

### Animation Control
```csharp
// Start animation from 0 to current Value
StartAnimation()

// Start animation from 0 to specified target
StartAnimation(int targetValue)

// Start animation from startValue to targetValue
StartAnimation(int startValue, int targetValue)

// Animate from current displayed value to new value
AnimateToValue(int newValue)

// Set value instantly without animation
SetValueInstant(int value)

// Stop current animation
StopAnimation()

// Check if animation is playing
bool IsAnimating()
```

### Configuration
```csharp
// Set animation parameters
SetAnimationSettings(float duration, Tween.EaseType easeType, Tween.TransitionType transitionType)
```

## Signals

- `AnimationFinished()`: Emitted when an animation completes
- `ValueChanged(int oldValue, int newValue)`: Emitted when the value changes

## Usage Examples

### Basic Setup
```csharp
// Get the node
var scoreLabel = GetNode<DynamicNumberLabel>("ScoreLabel");

// Configure display
scoreLabel.Prefix = "Score: ";
scoreLabel.Suffix = " pts";
scoreLabel.UseDelimiter = true;
scoreLabel.Delimiter = ",";

// Configure animation
scoreLabel.AnimationDuration = 1.5f;
scoreLabel.EaseType = Tween.EaseType.Out;
scoreLabel.TransitionType = Tween.TransitionType.Cubic;
```

### Animation Examples
```csharp
// Animate score from 0 to 1500
scoreLabel.StartAnimation(1500);

// Animate from current value to new value
scoreLabel.AnimateToValue(2000);

// Animate from 100 to 500
scoreLabel.StartAnimation(100, 500);

// Set value instantly (no animation)
scoreLabel.SetValueInstant(1000);
```

### Signal Handling
```csharp
// Connect to signals
scoreLabel.AnimationFinished += OnScoreAnimationFinished;
scoreLabel.ValueChanged += OnScoreValueChanged;

private void OnScoreAnimationFinished()
{
    GD.Print("Score animation completed!");
}

private void OnScoreValueChanged(int oldValue, int newValue)
{
    GD.Print($"Score changed from {oldValue} to {newValue}");
}
```

### Common Use Cases

#### Game Score Display
```csharp
var scoreLabel = GetNode<DynamicNumberLabel>("UI/ScoreLabel");
scoreLabel.Prefix = "Score: ";
scoreLabel.AnimationDuration = 1.0f;
scoreLabel.EaseType = Tween.EaseType.Out;

// Player scores points
scoreLabel.AnimateToValue(currentScore + points);
```

#### Currency Counter
```csharp
var goldLabel = GetNode<DynamicNumberLabel>("UI/GoldLabel");
goldLabel.Prefix = "$";
goldLabel.UseDelimiter = true;
goldLabel.AnimationDuration = 0.8f;
goldLabel.TransitionType = Tween.TransitionType.Bounce;

// Player earns gold
goldLabel.AnimateToValue(playerGold + earnedGold);
```

#### Experience Bar
```csharp
var expLabel = GetNode<DynamicNumberLabel>("UI/ExpLabel");
expLabel.Suffix = " XP";
expLabel.AnimationDuration = 2.0f;
expLabel.EaseType = Tween.EaseType.InOut;

// Level up animation
expLabel.StartAnimation(0, newExpAmount);
```

#### Health/Damage Display
```csharp
var healthLabel = GetNode<DynamicNumberLabel>("UI/HealthLabel");
healthLabel.Prefix = "HP: ";
healthLabel.Suffix = "/100";
healthLabel.AnimationDuration = 0.5f;
healthLabel.TransitionType = Tween.TransitionType.Sine;

// Take damage
healthLabel.AnimateToValue(currentHealth - damage);
```

#### Random Mode Examples
```csharp
// Slot machine effect - random numbers before settling
var slotLabel = GetNode<DynamicNumberLabel>("UI/SlotLabel");
slotLabel.RandomMode = true;
slotLabel.RandomUpdateInterval = 0.02f; // Fast updates
slotLabel.AnimationDuration = 3.0f;
slotLabel.StartAnimation(0, finalWinAmount);

// Loading progress with random fluctuation
var loadingLabel = GetNode<DynamicNumberLabel>("UI/LoadingLabel");
loadingLabel.Suffix = "%";
loadingLabel.RandomMode = true;
loadingLabel.RandomUpdateInterval = 0.1f;
loadingLabel.AnimationDuration = 5.0f;
loadingLabel.StartAnimation(0, 100);

// Damage counter with chaotic numbers
var damageLabel = GetNode<DynamicNumberLabel>("UI/DamageLabel");
damageLabel.RandomMode = true;
damageLabel.RandomUpdateInterval = 0.03f;
damageLabel.AnimationDuration = 1.5f;
damageLabel.StartAnimation(0, totalDamage);
```

#### Maximum Digits Examples
```csharp
// Timer display with fixed 2 digits (00-99)
var timerLabel = GetNode<DynamicNumberLabel>("UI/TimerLabel");
timerLabel.MaxDigits = 2;
timerLabel.Suffix = "s";
timerLabel.AnimateToValue(5); // Displays "05s"

// Score counter with 6 digits max
var scoreLabel = GetNode<DynamicNumberLabel>("UI/ScoreLabel");
scoreLabel.MaxDigits = 6;
scoreLabel.UseDelimiter = true;
scoreLabel.AnimateToValue(12345); // Displays "012,345"

// Health bar with 3 digits (000-999)
var healthLabel = GetNode<DynamicNumberLabel>("UI/HealthLabel");
healthLabel.MaxDigits = 3;
healthLabel.Prefix = "HP: ";
healthLabel.AnimateToValue(75); // Displays "HP: 075"

// Overflow handling with warning
var limitedLabel = GetNode<DynamicNumberLabel>("UI/LimitedLabel");
limitedLabel.MaxDigits = 4;
limitedLabel.AnimateToValue(99999); // Displays "9999" + logs warning
```

### Advanced Animation Settings
```csharp
// Configure both linear and random mode parameters
scoreLabel.SetAnimationSettings(
    duration: 2.0f,
    easeType: Tween.EaseType.Out,
    transitionType: Tween.TransitionType.Bounce,
    randomMode: true,
    randomUpdateInterval: 0.05f
);
```
### Tips and Best Practices

1. **Performance**: Use shorter animation durations for frequently updated values
2. **User Experience**: Choose appropriate easing for the context (bounce for positive events, smooth for neutral)
3. **Readability**: Use delimiters for large numbers (>1000)
4. **Feedback**: Connect to `AnimationFinished` signal for chaining animations or triggering effects
5. **Responsiveness**: Use `SetValueInstant()` when you need immediate updates (e.g., loading saved data)

## Integration with Godot Editor

This component is fully integrated with the Godot editor:
- Appears in "Create Node" dialog under "DynamicNumberLabel"
- All properties are exposed in the Inspector
- Changes in the editor are reflected immediately
- Supports undo/redo operations

## Requirements

- Godot 4.x
- Microsoft.Extensions.Logging
- .NET support enabled in project

## License

This component is part of the GodotNodeExtension project.
