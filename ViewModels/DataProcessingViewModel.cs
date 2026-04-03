using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class DataProcessingViewModel : ObservableObject
    {
        [ObservableProperty] private string _statusInfo = "数据处理模块已就绪";
    }
}