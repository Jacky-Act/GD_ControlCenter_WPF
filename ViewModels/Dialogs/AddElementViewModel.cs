using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class AddElementViewModel : ObservableObject
    {
        private readonly Action<string> _applyAction;
        private readonly Action _closeAction;

        // 绑定的输入/选中值
        [ObservableProperty]
        private string _elementName = string.Empty;

        // ================= 新增：预设的常见元素库 =================
        public ObservableCollection<string> ElementLibrary { get; } = new()
        {
            "Pb", "Cd", "Hg", "As", "Cr",
            "Cu", "Zn", "Fe", "Mn", "Ni",
            "K",  "Na", "Ca", "Mg", "Al"
        };
        // ==========================================================

        public AddElementViewModel(Action<string> applyAction, Action closeAction)
        {
            _applyAction = applyAction;
            _closeAction = closeAction;
        }
        [RelayCommand]
        private void Apply()
        {
            // 不管是手打的还是下拉选的，最后都会通过 ElementName 传进来
            if (!string.IsNullOrWhiteSpace(ElementName))
            {
                // 自动去除前后空格，防止误输
                _applyAction?.Invoke(ElementName.Trim());
            }
            _closeAction?.Invoke();
        }

        [RelayCommand]
        private void Cancel() => _closeAction?.Invoke();
    }
}