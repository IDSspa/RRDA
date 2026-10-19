// Adapted from Stopbyte/WPF-Numeric-Spinner-NumericUpDown (MIT License):
// https://github.com/Stopbyte/WPF-Numeric-Spinner-NumericUpDown
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RRDA.RepImp.Controls;

public partial class NumericSpinner : UserControl
{
    private bool _updatingText;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(0, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(int.MinValue, OnBoundsChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(int.MaxValue, OnBoundsChanged));

    public event EventHandler? ValueChanged;

    public NumericSpinner()
    {
        InitializeComponent();
        UpdateText();
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (NumericSpinner)dependencyObject;
        var clampedValue = spinner.Clamp((int)e.NewValue);
        if (clampedValue != (int)e.NewValue)
        {
            spinner.SetCurrentValue(ValueProperty, clampedValue);
            return;
        }

        spinner.UpdateText();
        spinner.ValueChanged?.Invoke(spinner, EventArgs.Empty);
    }

    private static void OnBoundsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (NumericSpinner)dependencyObject;
        if (spinner.Minimum > spinner.Maximum)
            spinner.Maximum = spinner.Minimum;

        spinner.Value = spinner.Clamp(spinner.Value);
    }

    private int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    private void UpdateText()
    {
        if (ValueTextBox is null)
            return;

        _updatingText = true;
        ValueTextBox.Text = Value.ToString();
        _updatingText = false;
    }

    private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingText && int.TryParse(ValueTextBox.Text, out var value))
            Value = Clamp(value);
    }

    private void ValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateText();

    private void ValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            ChangeValue(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ChangeValue(-1);
            e.Handled = true;
        }
    }

    private void Increase_Click(object sender, RoutedEventArgs e) => ChangeValue(1);

    private void Decrease_Click(object sender, RoutedEventArgs e) => ChangeValue(-1);

    private void ChangeValue(int delta)
    {
        if ((delta > 0 && Value < Maximum) || (delta < 0 && Value > Minimum))
            Value += delta;
    }
}
