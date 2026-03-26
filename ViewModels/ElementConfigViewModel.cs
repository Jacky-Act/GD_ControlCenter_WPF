using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
        
        // =========================================================
        // 右侧的“已选分析配置”列表
        // =========================================================
        [ObservableProperty]
        private ObservableCollection<AnalysisConfigItem> _activeConfigs = new();

        // =========================================================
        // 右侧/下方的“已选分析配置”列表 (必须叫 SelectedConfigs 才能和 XAML 匹配)
        // =========================================================
        [ObservableProperty]
        private ObservableCollection<AnalysisConfigItem> _selectedConfigs = new();

        // =========================================================
        // 点击“加入分析配置”按钮触发
        // =========================================================
        [RelayCommand]
        private void SelectCurrentConfig()
        {
            // 1. 防呆：必须选了元素和波长才能加入
            if (SelectedElement == null)
            {
                MessageBox.Show("请先在左侧选择要加入的元素！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedWavelength == null)
            {
                MessageBox.Show("请先选择该元素对应的特征波长！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 查重：防止同一个 (元素+波长) 组合被重复添加到已选列表
            if (SelectedConfigs.Any(x => x.ElementName == SelectedElement.Name && x.Wavelength == SelectedWavelength.Value))
            {
                MessageBox.Show($"组合[{SelectedElement.Name} - {SelectedWavelength.Value} nm] 已在分析配置中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 3. 打包数据实体 (使用之前在 Message 中建好的 AnalysisConfigItem)
            var newItem = new AnalysisConfigItem
            {
                ElementName = SelectedElement.Name,
                Wavelength = SelectedWavelength.Value,


                // 默认的硬件参数（电流、转速、采样次数、间隔）
                CurrentConfigText = "60",
                SpeedConfigText = "30",
                SampleCountText = "10",
                SampleIntervalText = "1"
            };

            // 4. 添加到界面绑定的表格中 (UI 会瞬间刷出一行)
            SelectedConfigs.Add(newItem);

            // 5. 【跨页面核心】通过消息总线，把最新出炉的分析列表广播给全软件！
            // (注意：消息类 ActiveConfigsChangedMessage 我们之前已经建好了，不用改)
            WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
        }
        
        // =========================================================
        // 绑定下方表格当前被选中的那一行
        // =========================================================
        [ObservableProperty]
        private AnalysisConfigItem? _currentSelectedConfig;

        // =========================================================
        // 点击“删除信息”按钮触发
        // =========================================================
        [RelayCommand]
        private void RemoveConfig()
        {
            // 1. 防呆：判断用户是否选中了表格里的一行
            if (CurrentSelectedConfig == null)
            {
                MessageBox.Show("请先在下方的【已选分析配置】列表中选中要删除的行！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 确认删除提示（防止误触）
            var result = MessageBox.Show($"确定要删除 [{CurrentSelectedConfig.ElementName} - {CurrentSelectedConfig.Wavelength} nm] 的配置吗？",
                                         "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 3. 从列表中移除选中的项
                SelectedConfigs.Remove(CurrentSelectedConfig);

                // 4. 【跨页面核心】再次通过消息总线广播最新的列表！
                // 这样流动注射等其他页面也会瞬间同步，把删掉的元素也剔除掉。
                WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
            }
        }
    }
}