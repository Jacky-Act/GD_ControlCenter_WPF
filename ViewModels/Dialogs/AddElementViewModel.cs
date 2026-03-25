using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class AddElementViewModel : ObservableObject
    {
        private readonly Action<string> _applyAction;
        private readonly Action _closeAction;

        [ObservableProperty]
        private string _elementName = string.Empty;

        public AddElementViewModel(Action<string> applyAction, Action closeAction)
        {
            _applyAction = applyAction;
            _closeAction = closeAction;
        }

        [RelayCommand]
        private void Apply()
        {
            if (!string.IsNullOrWhiteSpace(ElementName))
            {
                _applyAction?.Invoke(ElementName.Trim());
            }
            _closeAction?.Invoke();
        }
        [RelayCommand]
        private void Cancel() => _closeAction?.Invoke();
    }
}