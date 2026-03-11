using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class SpecParamSettingViewModel : ObservableObject
    {
        private readonly Action _closeAction;
        private readonly Action<string> _applyAction;

        [ObservableProperty] private string _windowTitle; // 窗口左上角的标题
        [ObservableProperty] private string _headerTitle; // 内容区的大字标题
        [ObservableProperty] private double _inputValue;
        [ObservableProperty] private double _minValue;
        [ObservableProperty] private double _maxValue;
        [ObservableProperty] private double _stepValue;

        public SpecParamSettingViewModel(string windowTitle, string headerTitle, double currentValue, double min, double max, double step, Action<string> applyAction, Action closeAction)
        {
            WindowTitle = windowTitle;
            HeaderTitle = headerTitle;
            InputValue = currentValue;
            MinValue = min;
            MaxValue = max;
            StepValue = step;
            _applyAction = applyAction;
            _closeAction = closeAction;
        }

        [RelayCommand]
        private void Increase()
        {
            if (InputValue + StepValue <= MaxValue) InputValue += StepValue;
            else InputValue = MaxValue;
        }

        [RelayCommand]
        private void Decrease()
        {
            if (InputValue - StepValue >= MinValue) InputValue -= StepValue;
            else InputValue = MinValue;
        }

        [RelayCommand]
        private void Apply()
        {
            _applyAction?.Invoke(InputValue.ToString());
            _closeAction?.Invoke();
        }
    }
}