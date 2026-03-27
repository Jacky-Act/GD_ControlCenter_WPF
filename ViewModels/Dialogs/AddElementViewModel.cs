using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    /// <summary>
    /// 元素周期表单元格数据模型
    /// </summary>
    public class PeriodicElement
    {
        public int AtomicNumber { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public int Row { get; set; }     // 0-9 (前7行为主表，后2行为镧/锕系)
        public int Column { get; set; }  // 0-17 (共18族)
        public string HexColor { get; set; } = "#E0E0E0"; // 默认底色

        public PeriodicElement(int num, string symbol, int row, int col, string color)
        {
            AtomicNumber = num; Symbol = symbol; Row = row; Column = col; HexColor = color;
        }
    }

    public partial class AddElementViewModel : ObservableObject
    {
        private readonly Action<string> _applyAction;
        private readonly Action _closeAction;

        // 绑定到 UI 的元素集合
        public ObservableCollection<PeriodicElement> PeriodicElements { get; } = new();

        public AddElementViewModel(Action<string> applyAction, Action closeAction)
        {
            _applyAction = applyAction;
            _closeAction = closeAction;
            LoadPeriodicTable();
        }

        // 当用户在弹窗的周期表上点击某个元素时触发
        [RelayCommand]
        private void SelectElement(string symbol)
        {
            // 将选中的元素符号 (如 "Fe") 传回给主界面，然后关闭弹窗
            _applyAction?.Invoke(symbol);
            _closeAction?.Invoke();
        }

        [RelayCommand]
        private void Cancel() => _closeAction?.Invoke();

        /// <summary>
        /// 加载周期表坐标数据 (颜色对应不同族类)
        /// </summary>
        private void LoadPeriodicTable()
        {
            string nonMetal = "#B2DFDB"; // 非金属 (绿)
            string nobleGas = "#B39DDB"; // 稀有气体 (紫)
            string alkali = "#FFCC80";   // 碱金属 (橙)
            string alkaline = "#FFE082"; // 碱土金属 (黄)
            string transition = "#BBDEFB"; // 过渡金属 (蓝)
            string basicMetal = "#CFD8DC"; // 主族金属 (灰蓝)
            string metalloid = "#D7CCC8";  // 类金属 (紫灰)

            // 第 1 周期
            PeriodicElements.Add(new PeriodicElement(1, "H", 0, 0, nonMetal));
            PeriodicElements.Add(new PeriodicElement(2, "He", 0, 17, nobleGas));
            // 第 2 周期
            PeriodicElements.Add(new PeriodicElement(3, "Li", 1, 0, alkali));
            PeriodicElements.Add(new PeriodicElement(4, "Be", 1, 1, alkaline));
            PeriodicElements.Add(new PeriodicElement(5, "B", 1, 12, metalloid));
            PeriodicElements.Add(new PeriodicElement(6, "C", 1, 13, nonMetal));
            PeriodicElements.Add(new PeriodicElement(7, "N", 1, 14, nonMetal));
            PeriodicElements.Add(new PeriodicElement(8, "O", 1, 15, nonMetal));
            PeriodicElements.Add(new PeriodicElement(9, "F", 1, 16, nonMetal));
            PeriodicElements.Add(new PeriodicElement(10, "Ne", 1, 17, nobleGas));
            // 第 3 周期
            PeriodicElements.Add(new PeriodicElement(11, "Na", 2, 0, alkali));
            PeriodicElements.Add(new PeriodicElement(12, "Mg", 2, 1, alkaline));
            PeriodicElements.Add(new PeriodicElement(13, "Al", 2, 12, basicMetal));
            PeriodicElements.Add(new PeriodicElement(14, "Si", 2, 13, metalloid));
            PeriodicElements.Add(new PeriodicElement(15, "P", 2, 14, nonMetal));
            PeriodicElements.Add(new PeriodicElement(16, "S", 2, 15, nonMetal));
            PeriodicElements.Add(new PeriodicElement(17, "Cl", 2, 16, nonMetal));
            PeriodicElements.Add(new PeriodicElement(18, "Ar", 2, 17, nobleGas));
            // 第 4 周期 (开始出现过渡金属)
            PeriodicElements.Add(new PeriodicElement(19, "K", 3, 0, alkali));
            PeriodicElements.Add(new PeriodicElement(20, "Ca", 3, 1, alkaline));
            PeriodicElements.Add(new PeriodicElement(21, "Sc", 3, 2, transition));
            PeriodicElements.Add(new PeriodicElement(22, "Ti", 3, 3, transition));
            PeriodicElements.Add(new PeriodicElement(23, "V", 3, 4, transition));
            PeriodicElements.Add(new PeriodicElement(24, "Cr", 3, 5, transition));
            PeriodicElements.Add(new PeriodicElement(25, "Mn", 3, 6, transition));
            PeriodicElements.Add(new PeriodicElement(26, "Fe", 3, 7, transition));
            PeriodicElements.Add(new PeriodicElement(27, "Co", 3, 8, transition));
            PeriodicElements.Add(new PeriodicElement(28, "Ni", 3, 9, transition));
            PeriodicElements.Add(new PeriodicElement(29, "Cu", 3, 10, transition));
            PeriodicElements.Add(new PeriodicElement(30, "Zn", 3, 11, transition));
            PeriodicElements.Add(new PeriodicElement(31, "Ga", 3, 12, basicMetal));
            PeriodicElements.Add(new PeriodicElement(32, "Ge", 3, 13, metalloid));
            PeriodicElements.Add(new PeriodicElement(33, "As", 3, 14, metalloid));
            PeriodicElements.Add(new PeriodicElement(34, "Se", 3, 15, nonMetal));
            PeriodicElements.Add(new PeriodicElement(35, "Br", 3, 16, nonMetal));
            PeriodicElements.Add(new PeriodicElement(36, "Kr", 3, 17, nobleGas));
            // 第 5 周期
            PeriodicElements.Add(new PeriodicElement(37, "Rb", 4, 0, alkali));
            PeriodicElements.Add(new PeriodicElement(38, "Sr", 4, 1, alkaline));
            PeriodicElements.Add(new PeriodicElement(46, "Pd", 4, 9, transition));
            PeriodicElements.Add(new PeriodicElement(47, "Ag", 4, 10, transition));
            PeriodicElements.Add(new PeriodicElement(48, "Cd", 4, 11, transition));
            PeriodicElements.Add(new PeriodicElement(50, "Sn", 4, 13, basicMetal));
            PeriodicElements.Add(new PeriodicElement(51, "Sb", 4, 14, metalloid));
            PeriodicElements.Add(new PeriodicElement(53, "I", 4, 16, nonMetal));
            // 第 6 周期 (部分代表性重金属)
            PeriodicElements.Add(new PeriodicElement(55, "Cs", 5, 0, alkali));
            PeriodicElements.Add(new PeriodicElement(56, "Ba", 5, 1, alkaline));
            PeriodicElements.Add(new PeriodicElement(78, "Pt", 5, 9, transition));
            PeriodicElements.Add(new PeriodicElement(79, "Au", 5, 10, transition));
            PeriodicElements.Add(new PeriodicElement(80, "Hg", 5, 11, transition));
            PeriodicElements.Add(new PeriodicElement(82, "Pb", 5, 13, basicMetal));
        }
    }
}