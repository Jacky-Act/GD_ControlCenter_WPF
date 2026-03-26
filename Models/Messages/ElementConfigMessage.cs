using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.Xml.Linq;
using static OfficeOpenXml.ExcelErrorValue;

namespace GD_ControlCenter_WPF.Models.Messages
{
    // 波长数据模型
    public partial class WavelengthItemModel : ObservableObject
    {
        [ObservableProperty]
        private double _value; // 纯数值，UI 会自动格式化带上 "nm"

        // 预留：每个波长下可以有多条拟合曲线
        public ObservableCollection<object> FittingCurves { get; set; } = new();

        public WavelengthItemModel(double val)
        {
            Value = val;
        }
    }

    // 元素数据模型
    public partial class ElementItemModel : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        // 该元素拥有的特征波长集合
        public ObservableCollection<WavelengthItemModel> Wavelengths { get; set; } = new();

        public ElementItemModel(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// 已选分析配置的数据实体（对应 UI 右侧 DataGrid 的每一行）
    /// </summary>
    public partial class AnalysisConfigItem : ObservableObject
    {
        [ObservableProperty] private string _elementName = string.Empty;
        [ObservableProperty] private double _wavelength;

        // 拟合曲线信息（如果没有测曲线，就给个默认提示）[ObservableProperty] private string _equationText = "暂无曲线";
        [ObservableProperty] private string _rSquaredText = "-";

        // 绑定的专属硬件参数
        [ObservableProperty] private string _currentConfigText = "60";
        [ObservableProperty] private string _speedConfigText = "30";
        [ObservableProperty] private string _sampleCountText = "10";
        [ObservableProperty] private string _sampleIntervalText = "1";
    }

    /// <summary>
    /// 全局分析配置变更消息。
    /// 当在元素配置页点击“加入”或“删除”时发出，通知流动注射页面同步更新。
    /// </summary>
    public class ActiveConfigsChangedMessage : ValueChangedMessage<List<AnalysisConfigItem>>
    {
        public ActiveConfigsChangedMessage(List<AnalysisConfigItem> value) : base(value) { }
    }
}