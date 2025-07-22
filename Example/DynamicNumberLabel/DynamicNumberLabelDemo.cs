using System;
using Godot;

namespace GodotNodeExtension.Example.DynamicNumberLabel;

public partial class DynamicNumberLabelDemo : Control
{
    private GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel _scoreLabel;
    private GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel _goldLabel;
    private GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel _expLabel;
    private GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel _healthLabel;
    
    private Button _scoreButton;
    private Button _goldButton;
    private Button _expButton;
    private Button _damageButton;
    private Button _resetButton;
    
    private SpinBox _scoreSpinBox;
    private SpinBox _durationSpinBox;
    private OptionButton _easeTypeOption;
    private OptionButton _transitionTypeOption;
    private CheckBox _randomModeCheckBox;
    private SpinBox _randomIntervalSpinBox;
    
    private int _currentScore = 0;
    private int _currentGold = 0;
    private int _currentExp = 0;
    private int _currentHealth = 100;

    public override void _Ready()
    {
        SetupUI();
        ConnectSignals();
        GD.Print("DynamicNumberLabel Demo Ready!");
    }

    private void SetupUI()
    {
        // Create main container
        var vbox = new VBoxContainer();
        AddChild(vbox);
        
        // Title
        var titleLabel = new Label();
        titleLabel.Text = "DynamicNumberLabel Demo";
        titleLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
        vbox.AddChild(titleLabel);
        
        vbox.AddChild(new HSeparator());
        
        // Demo labels section
        var labelsContainer = new VBoxContainer();
        vbox.AddChild(labelsContainer);
        
        var labelsTitle = new Label();
        labelsTitle.Text = "Demo Labels:";
        labelsContainer.AddChild(labelsTitle);
        
        // Score Label
        _scoreLabel = new GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel();
        _scoreLabel.Prefix = "Score: ";
        _scoreLabel.Suffix = " pts";
        _scoreLabel.UseDelimiter = true;
        _scoreLabel.AnimationDuration = 1.0f;
        _scoreLabel.EaseType = Tween.EaseType.Out;
        _scoreLabel.TransitionType = Tween.TransitionType.Cubic;
        _scoreLabel.AddThemeColorOverride("font_color", Colors.White);
        labelsContainer.AddChild(_scoreLabel);
        
        // Gold Label
        _goldLabel = new GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel();
        _goldLabel.Prefix = "Gold: $";
        _goldLabel.UseDelimiter = true;
        _goldLabel.AnimationDuration = 0.8f;
        _goldLabel.EaseType = Tween.EaseType.Out;
        _goldLabel.TransitionType = Tween.TransitionType.Bounce;
        _goldLabel.AddThemeColorOverride("font_color", Colors.Gold);
        labelsContainer.AddChild(_goldLabel);
        
        // Experience Label
        _expLabel = new GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel();
        _expLabel.Prefix = "EXP: ";
        _expLabel.Suffix = " / 1000";
        _expLabel.AnimationDuration = 2.0f;
        _expLabel.EaseType = Tween.EaseType.InOut;
        _expLabel.TransitionType = Tween.TransitionType.Sine;
        _expLabel.AddThemeColorOverride("font_color", Colors.Cyan);
        labelsContainer.AddChild(_expLabel);
        
        // Health Label
        _healthLabel = new GodotNodeExtension.DynamicNumberLabel.DynamicNumberLabel();
        _healthLabel.Prefix = "HP: ";
        _healthLabel.Suffix = " / 100";
        _healthLabel.AnimationDuration = 0.5f;
        _healthLabel.EaseType = Tween.EaseType.Out;
        _healthLabel.TransitionType = Tween.TransitionType.Sine;
        _healthLabel.AddThemeColorOverride("font_color", Colors.LimeGreen);
        labelsContainer.AddChild(_healthLabel);
        
        vbox.AddChild(new HSeparator());
        
        // Controls section
        var controlsContainer = new VBoxContainer();
        vbox.AddChild(controlsContainer);
        
        var controlsTitle = new Label();
        controlsTitle.Text = "Controls:";
        controlsContainer.AddChild(controlsTitle);
        
        // Buttons
        var buttonsContainer = new GridContainer();
        buttonsContainer.Columns = 3;
        controlsContainer.AddChild(buttonsContainer);
        
        _scoreButton = new Button();
        _scoreButton.Text = "Add Score (+500)";
        buttonsContainer.AddChild(_scoreButton);
        
        _goldButton = new Button();
        _goldButton.Text = "Earn Gold (+250)";
        buttonsContainer.AddChild(_goldButton);
        
        _expButton = new Button();
        _expButton.Text = "Gain EXP (+100)";
        buttonsContainer.AddChild(_expButton);
        
        _damageButton = new Button();
        _damageButton.Text = "Take Damage (-25)";
        buttonsContainer.AddChild(_damageButton);
        
        _resetButton = new Button();
        _resetButton.Text = "Reset All";
        buttonsContainer.AddChild(_resetButton);
        
        vbox.AddChild(new HSeparator());
        
        // Settings section
        var settingsContainer = new VBoxContainer();
        vbox.AddChild(settingsContainer);
        
        var settingsTitle = new Label();
        settingsTitle.Text = "Animation Settings:";
        settingsContainer.AddChild(settingsTitle);
        
        var settingsGrid = new GridContainer();
        settingsGrid.Columns = 2;
        settingsContainer.AddChild(settingsGrid);
        
        // Score value input
        settingsGrid.AddChild(new Label { Text = "Score Value:" });
        _scoreSpinBox = new SpinBox();
        _scoreSpinBox.MinValue = 0;
        _scoreSpinBox.MaxValue = 999999;
        _scoreSpinBox.Step = 100;
        _scoreSpinBox.Value = 1000;
        settingsGrid.AddChild(_scoreSpinBox);
        
        // Duration input
        settingsGrid.AddChild(new Label { Text = "Duration:" });
        _durationSpinBox = new SpinBox();
        _durationSpinBox.MinValue = 0.1;
        _durationSpinBox.MaxValue = 5.0;
        _durationSpinBox.Step = 0.1;
        _durationSpinBox.Value = 1.0;
        settingsGrid.AddChild(_durationSpinBox);
        
        // Ease type
        settingsGrid.AddChild(new Label { Text = "Ease Type:" });
        _easeTypeOption = new OptionButton();
        _easeTypeOption.AddItem("In");
        _easeTypeOption.AddItem("Out");
        _easeTypeOption.AddItem("InOut");
        _easeTypeOption.Selected = 1; // Out
        settingsGrid.AddChild(_easeTypeOption);
        
        // Transition type
        settingsGrid.AddChild(new Label { Text = "Transition:" });
        _transitionTypeOption = new OptionButton();
        _transitionTypeOption.AddItem("Linear");
        _transitionTypeOption.AddItem("Sine");
        _transitionTypeOption.AddItem("Cubic");
        _transitionTypeOption.AddItem("Quart");
        _transitionTypeOption.AddItem("Quint");
        _transitionTypeOption.AddItem("Expo");
        _transitionTypeOption.AddItem("Circ");
        _transitionTypeOption.AddItem("Back");
        _transitionTypeOption.AddItem("Elastic");
        _transitionTypeOption.AddItem("Bounce");
        _transitionTypeOption.Selected = 2; // Cubic
        settingsGrid.AddChild(_transitionTypeOption);
        
        // Random mode checkbox
        settingsGrid.AddChild(new Label { Text = "Random Mode:" });
        _randomModeCheckBox = new CheckBox();
        _randomModeCheckBox.Text = "Enable Random Animation";
        settingsGrid.AddChild(_randomModeCheckBox);
        
        // Random interval input
        settingsGrid.AddChild(new Label { Text = "Random Interval:" });
        _randomIntervalSpinBox = new SpinBox();
        _randomIntervalSpinBox.MinValue = 0.01;
        _randomIntervalSpinBox.MaxValue = 0.5;
        _randomIntervalSpinBox.Step = 0.01;
        _randomIntervalSpinBox.Value = 0.05;
        settingsGrid.AddChild(_randomIntervalSpinBox);
        
        // Custom test button
        var customTestButton = new Button();
        customTestButton.Text = "Test Custom Animation";
        customTestButton.Pressed += OnCustomTestPressed;
        settingsGrid.AddChild(customTestButton);
        
        // Random mode demo buttons
        var randomDemoContainer = new HBoxContainer();
        settingsGrid.AddChild(new Label { Text = "Random Demos:" });
        settingsGrid.AddChild(randomDemoContainer);
        
        var slotMachineButton = new Button();
        slotMachineButton.Text = "Slot Machine";
        slotMachineButton.Pressed += OnSlotMachinePressed;
        randomDemoContainer.AddChild(slotMachineButton);
        
        var loadingButton = new Button();
        loadingButton.Text = "Random Loading";
        loadingButton.Pressed += OnRandomLoadingPressed;
        randomDemoContainer.AddChild(loadingButton);
        
        var chaosButton = new Button();
        chaosButton.Text = "Chaos Counter";
        chaosButton.Pressed += OnChaosCounterPressed;
        randomDemoContainer.AddChild(chaosButton);
    }

    private void ConnectSignals()
    {
        _scoreButton.Pressed += OnScoreButtonPressed;
        _goldButton.Pressed += OnGoldButtonPressed;
        _expButton.Pressed += OnExpButtonPressed;
        _damageButton.Pressed += OnDamageButtonPressed;
        _resetButton.Pressed += OnResetButtonPressed;
        
        // Connect animation finished signals
        _scoreLabel.AnimationFinished += () => GD.Print("Score animation finished!");
        _goldLabel.AnimationFinished += () => GD.Print("Gold animation finished!");
        _expLabel.AnimationFinished += () => GD.Print("EXP animation finished!");
        _healthLabel.AnimationFinished += () => GD.Print("Health animation finished!");
        
        // Connect value changed signals
        _scoreLabel.ValueChanged += (oldValue, newValue) => 
            GD.Print($"Score changed from {oldValue} to {newValue}");
        _goldLabel.ValueChanged += (oldValue, newValue) => 
            GD.Print($"Gold changed from {oldValue} to {newValue}");
    }

    private void OnScoreButtonPressed()
    {
        _currentScore += 500;
        _scoreLabel.AnimateToValue(_currentScore);
    }

    private void OnGoldButtonPressed()
    {
        _currentGold += 250;
        _goldLabel.AnimateToValue(_currentGold);
    }

    private void OnExpButtonPressed()
    {
        _currentExp = Mathf.Min(_currentExp + 100, 1000);
        _expLabel.AnimateToValue(_currentExp);
    }

    private void OnDamageButtonPressed()
    {
        _currentHealth = Mathf.Max(_currentHealth - 25, 0);
        _healthLabel.AnimateToValue(_currentHealth);
        
        // Change color based on health
        if (_currentHealth <= 30)
            _healthLabel.AddThemeColorOverride("font_color", Colors.Red);
        else if (_currentHealth <= 60)
            _healthLabel.AddThemeColorOverride("font_color", Colors.Orange);
        else
            _healthLabel.AddThemeColorOverride("font_color", Colors.LimeGreen);
    }

    private void OnResetButtonPressed()
    {
        _currentScore = 0;
        _currentGold = 0;
        _currentExp = 0;
        _currentHealth = 100;
        
        _scoreLabel.SetValueInstant(0);
        _goldLabel.SetValueInstant(0);
        _expLabel.SetValueInstant(0);
        _healthLabel.SetValueInstant(100);
        _healthLabel.AddThemeColorOverride("font_color", Colors.LimeGreen);
        
        GD.Print("All values reset!");
    }

    private void OnCustomTestPressed()
    {
        var duration = (float)_durationSpinBox.Value;
        var easeType = (Tween.EaseType)_easeTypeOption.Selected;
        var transitionType = (Tween.TransitionType)_transitionTypeOption.Selected;
        var targetValue = (int)_scoreSpinBox.Value;
        var randomMode = _randomModeCheckBox.ButtonPressed;
        var randomInterval = (float)_randomIntervalSpinBox.Value;
        
        _scoreLabel.SetAnimationSettings(duration, easeType, transitionType, randomMode, randomInterval);
        _scoreLabel.StartAnimation(0, targetValue);
        _currentScore = targetValue;
        
        GD.Print($"Testing animation: Duration={duration}, Ease={easeType}, Transition={transitionType}, Target={targetValue}, Random={randomMode}, Interval={randomInterval}");
    }

    private void OnSlotMachinePressed()
    {
        // Slot machine effect - fast random updates, longer duration
        _goldLabel.SetAnimationSettings(3.0f, Tween.EaseType.Out, Tween.TransitionType.Cubic, true, 0.02f);
        var winAmount = 777 + (new Random().Next(0, 1000));
        _goldLabel.StartAnimation(0, winAmount);
        _currentGold = winAmount;
        
        GD.Print($"Slot machine spinning to {winAmount}!");
    }

    private void OnRandomLoadingPressed()
    {
        // Loading progress with random fluctuation
        _expLabel.Prefix = "Loading: ";
        _expLabel.Suffix = "%";
        _expLabel.SetAnimationSettings(5.0f, Tween.EaseType.InOut, Tween.TransitionType.Sine, true, 0.1f);
        _expLabel.StartAnimation(0, 100);
        _currentExp = 100;
        
        GD.Print("Random loading progress started!");
    }

    private void OnChaosCounterPressed()
    {
        // Chaotic damage counter
        _healthLabel.Prefix = "Damage: ";
        _healthLabel.Suffix = " HP";
        _healthLabel.SetAnimationSettings(2.0f, Tween.EaseType.Out, Tween.TransitionType.Bounce, true, 0.03f);
        var damage = new Random().Next(50, 300);
        _healthLabel.StartAnimation(0, damage);
        _healthLabel.AddThemeColorOverride("font_color", Colors.Red);
        
        GD.Print($"Chaos damage counter: {damage} HP!");
    }
}
