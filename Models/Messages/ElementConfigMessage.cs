using CommunityToolkit.Mvvm.ComponentModel;
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
}