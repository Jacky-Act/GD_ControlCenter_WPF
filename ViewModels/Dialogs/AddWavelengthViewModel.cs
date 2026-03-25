using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class AddWavelengthViewModel : ObservableObject
    {
        private readonly Action<double> _applyAction;
        private readonly Action _closeAction;

        [ObservableProperty]
        private string _wavelengthValue = string.Empty;

        public AddWavelengthViewModel(Action<double> applyAction, Action closeAction)
        {
            _applyAction = applyAction;
            _closeAction = closeAction;
        }

        [RelayCommand]
        private void Apply()
        {
            if (double.TryParse(WavelengthValue, out double val))
            {
                _applyAction?.Invoke(val);
                _closeAction?.Invoke();
            }
            else
            {
                MessageBox.Show("波长必须是有效的数字！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void Cancel() => _closeAction?.Invoke();
    }
}