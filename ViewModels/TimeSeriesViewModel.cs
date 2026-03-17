using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services; // 确保引入消息命名空间

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class TimeSeriesViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RecordText))]
        [NotifyPropertyChangedFor(nameof(RecordIcon))]
        [NotifyPropertyChangedFor(nameof(RecordButtonColor))]
        private bool _isRecording;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ViewModeText))]
        [NotifyPropertyChangedFor(nameof(ViewModeIcon))]
        private bool _isMatrixView; // False 为列表视图，True 为矩阵视图

        // 增加配置服务字段
        private readonly JsonConfigService _configService;

        // --- 动态 UI 绑定属性 ---
        public string RecordText => IsRecording ? "停止记录" : "开始记录";
        public string RecordIcon => IsRecording ? "StopCircle" : "PlayCircle";
        public string RecordButtonColor => IsRecording ? "#f44336" : "#673ab7";// 录制中变红，空闲时为主题紫

        public string ViewModeText => IsMatrixView ? "列表视图" : "矩阵视图";
        public string ViewModeIcon => IsMatrixView ? "ViewSequential" : "ViewGrid";

        public TimeSeriesViewModel(JsonConfigService configService)
        {
            _configService = configService; // 保存实例

            IsRecording = false;
            IsMatrixView = false;
        }

        [RelayCommand]
        private void ToggleRecording()
        {
            IsRecording = !IsRecording;
            // TODO: 调用底层硬件记录服务
        }

        [RelayCommand]
        private void ToggleViewMode()
        {
            IsMatrixView = !IsMatrixView;
            // 通知 View 切换 ScottPlot 的 Multiplot 布局
            WeakReferenceMessenger.Default.Send(new ChangeTimeSeriesViewMessage(IsMatrixView));
        }

        [RelayCommand]
        private void OpenSamplingConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.TimeSeriesSamplingConfigWindow();

            // 现在这里可以使用 _configService 了
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.TimeSeriesSamplingConfigViewModel(
                _configService,
                () => window.Close()
            );

            // 如果主界面当前已经找到了特征峰，可以在这里传进去
            // vm.AvailablePeaks = new ObservableCollection<double>(您的特征峰数组);

            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }
    }
}
