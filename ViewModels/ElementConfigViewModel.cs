using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.ViewModels.Dialogs;
using GD_ControlCenter_WPF.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ElementConfigViewModel : ObservableObject
    {
        // 1. 最左侧的“检测元素”列表
        [ObservableProperty]
        private ObservableCollection<ElementItemModel> _elements = new();

        // 当用户在左侧选中某一个元素时，触发中间波长列表的刷新
        private ElementItemModel? _selectedElement;
        public ElementItemModel? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (SetProperty(ref _selectedElement, value))
                {
                    // 联动更新：中间的波长列表显示当前选中元素的专属波长
                    if (value != null)
                    {
                        Wavelengths = value.Wavelengths;
                    }
                    else
                    {
                        Wavelengths = new ObservableCollection<WavelengthItemModel>();
                    }
                }
            }
        }

        // 2. 中间的“目标波长”列表 (受 SelectedElement 动态控制)
        [ObservableProperty]
        private ObservableCollection<WavelengthItemModel> _wavelengths = new();

        [ObservableProperty]
        private WavelengthItemModel? _selectedWavelength;

        public ElementConfigViewModel()
        {
            // 初始化一些测试数据，方便您直接看到效果（可选）
            var pb = new ElementItemModel("Pb");
            pb.Wavelengths.Add(new WavelengthItemModel(283.3));
            pb.Wavelengths.Add(new WavelengthItemModel(220.3));

            var cd = new ElementItemModel("Cd");
            cd.Wavelengths.Add(new WavelengthItemModel(228.8));

            Elements.Add(pb);
            Elements.Add(cd);
        }

        // --- 按钮绑定的命令 ---

        [RelayCommand]
        private void AddElement()
        {
            var window = new AddElementWindow();
            var vm = new AddElementViewModel(
                applyAction: (name) =>
                {
                    // 防止重复添加相同元素
                    if (!Elements.Any(e => e.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        var newElement = new ElementItemModel(name);
                        Elements.Add(newElement);

                        // 自动选中刚添加的元素
                        SelectedElement = newElement;
                    }
                },
                closeAction: () => window.Close()
            );

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }
        [RelayCommand]
        private void AddWavelength()
        {
            if (SelectedElement == null)
            {
                MessageBox.Show("请先在左侧选择一个元素！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new AddWavelengthWindow();
            var vm = new AddWavelengthViewModel(
                applyAction: (val) =>
                {
                    // 防止在同一个元素下重复添加相同波长
                    if (!SelectedElement.Wavelengths.Any(w => w.Value == val))
                    {
                        var newWl = new WavelengthItemModel(val);
                        SelectedElement.Wavelengths.Add(newWl);

                        // 自动选中刚添加的波长
                        SelectedWavelength = newWl;
                    }
                },
                closeAction: () => window.Close()
            );

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }
    }
}