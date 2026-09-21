[English](README.md) | **中文**

# DynamicNumberLabel

Godot 自定义 Label 节点，用于带动画效果的数字显示，支持自定义格式、前缀、后缀和平滑过渡。

## 功能特性

- **数字动画过渡**：从任意值到另一个值的平滑动画，可自定义持续时间和缓动曲线
- **格式化选项**：支持数字分隔符、前缀和后缀
- **多种动画模式**：从零开始、在值之间动画或立即设置
- **自定义缓动**：可选择多种缓动类型和过渡曲线
- **信号支持**：动画完成或值变化时获得通知

## 属性

### 显示设置
- `Value` (int)：要显示的目标值。直接赋值只保存数值：文本由 `StartAnimation()`、`AnimateToValue()`、`SetValueInstant()` 或节点入树时刷新
- `Prefix` (string)：数字前显示的文本（如"分数: "）
- `Suffix` (string)：数字后显示的文本（如" 分"）
- `UseDelimiter` (bool)：是否使用分隔符显示大数字
- `Delimiter` (string)：分隔符字符（默认: ","）。设为空字符串表示不分组
- `MaxDigits` (int)：最多渲染的位数：不足补前导零，超出则被截断到上限（并打印警告），负值会预留一位给负号。0 = 无限制；10 位及以上已覆盖全部 `int` 取值

### 动画设置
- `AnimationDuration` (float)：动画持续时间（秒，默认: 1.0）
- `EaseType` (Tween.EaseType)：缓动曲线类型（In/Out/InOut）
- `TransitionType` (Tween.TransitionType)：过渡曲线（Linear/Sine/Cubic 等）
- `AnimateOnReady` (bool)：节点就绪时是否自动开始动画
- `RandomMode` (bool)：启用随机数字波动而非线性递增
- `RandomUpdateInterval` (float)：随机模式更新频率（秒，默认: 0.05）。非正值会回退到默认值

## 方法

### 动画控制
```csharp
// 从 0 到当前 Value 开始动画
StartAnimation()

// 从 0 到指定目标开始动画
StartAnimation(int targetValue)

// 从 startValue 到 targetValue 开始动画
StartAnimation(int startValue, int targetValue)

// 从当前显示值动画到新值
AnimateToValue(int newValue)

// 立即设置值（无动画，会取消正在播放的动画）
SetValueInstant(int value)

// 停止当前动画并显示目标值
StopAnimation()

// 检查动画是否播放中
bool IsAnimating()
```

传给动画入口的数值都会被 `MaxDigits` 截断。节点尚未进入场景树时（此时没有 Tween 可运行）调用动画入口，会立即显示目标值。

### 配置
```csharp
// 设置动画参数
SetAnimationSettings(float duration, Tween.EaseType easeType, Tween.TransitionType transitionType,
                     bool randomMode = false, float randomUpdateInterval = 0.05f)
```

## 信号

- `AnimationFinished()`：动画到达目标值时触发。取消动画（再次 `StartAnimation()`、`StopAnimation()`、`SetValueInstant()`）不会触发
- `ValueChanged(int oldValue, int newValue)`：仅由 `AnimateToValue()` 在新动画开始前触发，携带旧的目标值与新的（已截断的）目标值；其他入口不会触发

## 使用示例

### 基础设置
```csharp
// 获取节点
var scoreLabel = GetNode<DynamicNumberLabel>("ScoreLabel");

// 配置显示
scoreLabel.Prefix = "分数: ";
scoreLabel.Suffix = " 分";
scoreLabel.UseDelimiter = true;
scoreLabel.Delimiter = ",";

// 配置动画
scoreLabel.AnimationDuration = 1.5f;
scoreLabel.EaseType = Tween.EaseType.Out;
scoreLabel.TransitionType = Tween.TransitionType.Cubic;
```

### 动画示例
```csharp
// 从 0 动画到 1500
scoreLabel.StartAnimation(1500);

// 从当前值动画到新值
scoreLabel.AnimateToValue(2000);

// 从 100 动画到 500
scoreLabel.StartAnimation(100, 500);

// 立即设置值（无动画）
scoreLabel.SetValueInstant(1000);
```

### 信号处理
```csharp
// 连接信号
scoreLabel.AnimationFinished += OnScoreAnimationFinished;
scoreLabel.ValueChanged += OnScoreValueChanged;

private void OnScoreAnimationFinished()
{
    GD.Print("分数动画完成！");
}

private void OnScoreValueChanged(int oldValue, int newValue)
{
    GD.Print($"分数从 {oldValue} 变为 {newValue}");
}
```

### 常见用例

#### 游戏分数显示
```csharp
var scoreLabel = GetNode<DynamicNumberLabel>("UI/ScoreLabel");
scoreLabel.Prefix = "分数: ";
scoreLabel.AnimationDuration = 1.0f;
scoreLabel.EaseType = Tween.EaseType.Out;

// 玩家得分
scoreLabel.AnimateToValue(currentScore + points);
```

#### 货币计数器
```csharp
var goldLabel = GetNode<DynamicNumberLabel>("UI/GoldLabel");
goldLabel.Prefix = "¥";
goldLabel.UseDelimiter = true;
goldLabel.AnimationDuration = 0.8f;
goldLabel.TransitionType = Tween.TransitionType.Bounce;

// 玩家获得金币
goldLabel.AnimateToValue(playerGold + earnedGold);
```

#### 经验条
```csharp
var expLabel = GetNode<DynamicNumberLabel>("UI/ExpLabel");
expLabel.Suffix = " 经验";
expLabel.AnimationDuration = 2.0f;
expLabel.EaseType = Tween.EaseType.InOut;

// 升级动画
expLabel.StartAnimation(0, newExpAmount);
```

#### 生命值/伤害显示
```csharp
var healthLabel = GetNode<DynamicNumberLabel>("UI/HealthLabel");
healthLabel.Prefix = "HP: ";
healthLabel.Suffix = "/100";
healthLabel.AnimationDuration = 0.5f;
healthLabel.TransitionType = Tween.TransitionType.Sine;

// 受到伤害
healthLabel.AnimateToValue(currentHealth - damage);
```

#### 随机模式示例
```csharp
// 老虎机效果 - 固定前的随机数字
var slotLabel = GetNode<DynamicNumberLabel>("UI/SlotLabel");
slotLabel.RandomMode = true;
slotLabel.RandomUpdateInterval = 0.02f;
slotLabel.AnimationDuration = 3.0f;
slotLabel.StartAnimation(0, finalWinAmount);

// 带随机波动的加载进度
var loadingLabel = GetNode<DynamicNumberLabel>("UI/LoadingLabel");
loadingLabel.Suffix = "%";
loadingLabel.RandomMode = true;
loadingLabel.RandomUpdateInterval = 0.1f;
loadingLabel.AnimationDuration = 5.0f;
loadingLabel.StartAnimation(0, 100);

// 伤害数字的混乱跳动
var damageLabel = GetNode<DynamicNumberLabel>("UI/DamageLabel");
damageLabel.RandomMode = true;
damageLabel.RandomUpdateInterval = 0.03f;
damageLabel.AnimationDuration = 1.5f;
damageLabel.StartAnimation(0, totalDamage);
```

#### 最大位数示例
```csharp
// 固定 2 位的计时器显示（00-99）
var timerLabel = GetNode<DynamicNumberLabel>("UI/TimerLabel");
timerLabel.MaxDigits = 2;
timerLabel.Suffix = "秒";
timerLabel.AnimateToValue(5); // 显示 "05秒"

// 最多 6 位的分数计数器
var scoreLabel = GetNode<DynamicNumberLabel>("UI/ScoreLabel");
scoreLabel.MaxDigits = 6;
scoreLabel.UseDelimiter = true;
scoreLabel.AnimateToValue(12345); // 显示 "012,345"

// 3 位数的生命值（000-999）
var healthLabel = GetNode<DynamicNumberLabel>("UI/HealthLabel");
healthLabel.MaxDigits = 3;
healthLabel.Prefix = "HP: ";
healthLabel.AnimateToValue(75); // 显示 "HP: 075"

// 超出上限时的截断与警告
var limitedLabel = GetNode<DynamicNumberLabel>("UI/LimitedLabel");
limitedLabel.MaxDigits = 4;
limitedLabel.AnimateToValue(99999); // 显示 "9999" 并打印警告
```

### 高级动画设置
```csharp
// 同时配置线性与随机模式参数
scoreLabel.SetAnimationSettings(
    duration: 2.0f,
    easeType: Tween.EaseType.Out,
    transitionType: Tween.TransitionType.Bounce,
    randomMode: true,
    randomUpdateInterval: 0.05f
);
```

### 提示和最佳实践

1. **性能**：频繁更新的值使用较短的动画时长
2. **用户体验**：根据场景选择合适的缓动（正面事件用弹跳，中性事件用平滑）
3. **可读性**：大数字（>1000）使用分隔符
4. **反馈**：连接 `AnimationFinished` 信号以链接动画或触发效果
5. **响应性**：需要立即更新时使用 `SetValueInstant()`（如加载存档）
6. **暂停行为**：动画绑定在节点上，暂停场景树时动画会一起暂停，不会在暂停菜单后面继续累加

## Godot 编辑器集成

- 在"创建节点"对话框中显示为"DynamicNumberLabel"
- 所有导出属性在属性面板中可见
- 节点 `_Ready` 时完成一次格式化，因此场景在编辑器中加载后即可看到前缀、后缀与位数的效果
- 属性修改和其他节点属性一样支持撤销/重做

## 编辑器热重载

这里要注意的是**运行中的动画**：`Tween` 由引擎持有（随机模式还会再挂一个子 `Timer`），C# 重建之后
仍会回调进节点。当处于这种状态的标签就在编辑器**当前编辑的那个场景**里时，一次构建可能以引擎报出
「无法卸载程序集、放弃程序集重载」结束。

`_ExitTree()` 会走取消动画的路径，杀掉 Tween 并释放随机模式的定时器，所以移除节点（或关闭它所在的场景）
就够了，无需手动调用；重复取消也是安全的。迭代 C# 时尽量让当前编辑的场景里不含该标签；万一还是出现该
提示，重启编辑器即可。

## 依赖

- Godot 4.x + .NET 10.0+
- 无需额外第三方包

## 许可证

本组件属于 GodotNodeExtension 项目。
