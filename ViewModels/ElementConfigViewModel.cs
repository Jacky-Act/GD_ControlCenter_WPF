using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels
{
    public class PeriodicElement
    {
        public int AtomicNumber { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Column { get; set; }
        public string HexColor { get; set; } = "#E0E0E0";
        public PeriodicElement(int num, string symbol, int row, int col, string color)
        {
            AtomicNumber = num; Symbol = symbol; Row = row; Column = col; HexColor = color;
        }
    }

    public partial class StagedConfigItem : ObservableObject
    {
        [ObservableProperty] private string _elementName = string.Empty;
        [ObservableProperty] private double _wavelength;
    }

    public partial class ElementConfigViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;

        // 1. 元素周期表与波长数据库
        public ObservableCollection<PeriodicElement> PeriodicElements { get; } = new();
        private readonly Dictionary<string, List<double>> _wavelengthDatabase = new();

        [ObservableProperty] private string _selectedElementSymbol = "未选择";
        [ObservableProperty] private ObservableCollection<double> _availableWavelengths = new();

        // 2. 中间下方的暂存候选区
        [ObservableProperty] private ObservableCollection<StagedConfigItem> _stagedConfigs = new();
        [ObservableProperty] private StagedConfigItem? _currentStagedConfig;

        // 3. 右侧的最终已选分析配置
        [ObservableProperty] private ObservableCollection<AnalysisConfigItem> _selectedConfigs = new(); [ObservableProperty] private AnalysisConfigItem? _currentSelectedConfig;

        // 4. 仪表台硬件参数同步
        [ObservableProperty] private string _globalCurrent = "-";
        [ObservableProperty] private string _globalPumpSpeed = "-";
        [ObservableProperty] private string _globalSampleCount = "-"; [ObservableProperty] private string _globalSampleInterval = "-";

        public ElementConfigViewModel(JsonConfigService configService)
        {
            _configService = configService;
            InitializePeriodicTable();
            InitializeWavelengthDatabase();
            RefreshGlobalSettings();
        }

        public void RefreshGlobalSettings()
        {
            var config = _configService.Load();
            GlobalCurrent = config.LastHvCurrent.ToString();
            GlobalPumpSpeed = config.LastPumpSpeed.ToString();
            GlobalSampleCount = config.LastSampleCount.ToString();
            GlobalSampleInterval = config.LastSampleInterval.ToString();
        }

        // ================= 核心交互逻辑 =================

        [RelayCommand]
        private void SelectElement(string symbol)
        {
            SelectedElementSymbol = symbol;
            AvailableWavelengths.Clear();

            // 检查数据库中是否存在该元素的波长
            if (_wavelengthDatabase.TryGetValue(symbol, out var wls) && wls.Count > 0)
            {
                foreach (var w in wls) AvailableWavelengths.Add(w);
            }
            else
            {
                // 如果没有，弹出友好的提示框
                MessageBox.Show($"光谱波长数据库中暂无【{symbol}】元素的特征波长信息！\n请选择其他常用检测元素或联系管理员更新谱库。",
                                "波长信息缺失", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void StageWavelength(double wavelength)
        {
            if (SelectedElementSymbol == "未选择") return;
            if (!StagedConfigs.Any(x => x.ElementName == SelectedElementSymbol && x.Wavelength == wavelength))
            {
                StagedConfigs.Add(new StagedConfigItem { ElementName = SelectedElementSymbol, Wavelength = wavelength });
            }
        }

        [RelayCommand]
        private void RemoveStagedConfig()
        {
            if (CurrentStagedConfig != null) StagedConfigs.Remove(CurrentStagedConfig);
        }

        [RelayCommand]
        private void CommitToActiveConfigs()
        {
            if (StagedConfigs.Count == 0) return;
            RefreshGlobalSettings();

            foreach (var staged in StagedConfigs)
            {
                if (SelectedConfigs.Any(x => x.ElementName == staged.ElementName && x.Wavelength == staged.Wavelength)) continue;

                SelectedConfigs.Add(new AnalysisConfigItem
                {
                    ElementName = staged.ElementName,
                    Wavelength = staged.Wavelength,
                    SampleCountText = GlobalSampleCount,
                    SampleIntervalText = GlobalSampleInterval
                });
            }
            StagedConfigs.Clear();
            WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
        }
        [RelayCommand]
        private void RemoveFinalConfig()
        {
            if (CurrentSelectedConfig != null)
            {
                SelectedConfigs.Remove(CurrentSelectedConfig);
                WeakReferenceMessenger.Default.Send(new ActiveConfigsChangedMessage(SelectedConfigs.ToList()));
            }
        }

        // ================= 数据库初始化 =================

        private void InitializeWavelengthDatabase()
        {
            _wavelengthDatabase["Pb"] = new List<double> { 220.35, 283.31, 405.78 };
            _wavelengthDatabase["Cd"] = new List<double> { 214.44, 226.50, 228.80 };
            _wavelengthDatabase["Hg"] = new List<double> { 194.23, 253.65 };
            _wavelengthDatabase["As"] = new List<double> { 189.04, 193.70, 228.81 };
            _wavelengthDatabase["Cr"] = new List<double> { 205.55, 267.72, 283.56 };
            _wavelengthDatabase["Cu"] = new List<double> { 219.23, 324.75, 327.40 };
            _wavelengthDatabase["Zn"] = new List<double> { 202.55, 206.20, 213.86 };
            _wavelengthDatabase["Fe"] = new List<double> { 238.20, 258.59, 259.94, 271.44 };
            _wavelengthDatabase["Mn"] = new List<double> { 257.61, 259.37, 260.57 };
            _wavelengthDatabase["Ni"] = new List<double> { 221.65, 231.60, 232.00 };
            _wavelengthDatabase["K"] = new List<double> { 766.49, 769.90 };
            _wavelengthDatabase["Na"] = new List<double> { 589.00, 589.59 };
            _wavelengthDatabase["Ca"] = new List<double> { 317.93, 393.37, 396.85 };
            _wavelengthDatabase["Mg"] = new List<double> { 279.55, 280.27, 285.21 };
            _wavelengthDatabase["Al"] = new List<double> { 309.27, 394.40, 396.15 };
            _wavelengthDatabase["P"] = new List<double> { 177.50, 213.62, 214.91 };
            _wavelengthDatabase["S"] = new List<double> { 180.73, 182.04 };
            _wavelengthDatabase["Si"] = new List<double> { 212.41, 251.61, 288.16 };
            _wavelengthDatabase["Ag"] = new List<double> { 328.07, 338.29 };
            _wavelengthDatabase["Au"] = new List<double> { 242.80, 267.60 };
            _wavelengthDatabase["Sn"] = new List<double> { 189.93, 283.99 };
            _wavelengthDatabase["Sb"] = new List<double> { 206.83, 217.58 };
            _wavelengthDatabase["Ba"] = new List<double> { 233.53, 455.40 };
        }

        private void InitializePeriodicTable()
        {
            string nm = "#B2DFDB", ng = "#B39DDB", ak = "#FFCC80", akn = "#FFE082";
            string tr = "#BBDEFB", bm = "#CFD8DC", ml = "#D7CCC8", la = "#F8BBD0", ac = "#F48FB1";

            // 1-3 周期
            PeriodicElements.Add(new PeriodicElement(1, "H", 0, 0, nm)); PeriodicElements.Add(new PeriodicElement(2, "He", 0, 17, ng));
            PeriodicElements.Add(new PeriodicElement(3, "Li", 1, 0, ak)); PeriodicElements.Add(new PeriodicElement(4, "Be", 1, 1, akn));
            PeriodicElements.Add(new PeriodicElement(5, "B", 1, 12, ml)); PeriodicElements.Add(new PeriodicElement(6, "C", 1, 13, nm));
            PeriodicElements.Add(new PeriodicElement(7, "N", 1, 14, nm)); PeriodicElements.Add(new PeriodicElement(8, "O", 1, 15, nm));
            PeriodicElements.Add(new PeriodicElement(9, "F", 1, 16, nm)); PeriodicElements.Add(new PeriodicElement(10, "Ne", 1, 17, ng));
            PeriodicElements.Add(new PeriodicElement(11, "Na", 2, 0, ak)); PeriodicElements.Add(new PeriodicElement(12, "Mg", 2, 1, akn));
            PeriodicElements.Add(new PeriodicElement(13, "Al", 2, 12, bm)); PeriodicElements.Add(new PeriodicElement(14, "Si", 2, 13, ml));
            PeriodicElements.Add(new PeriodicElement(15, "P", 2, 14, nm)); PeriodicElements.Add(new PeriodicElement(16, "S", 2, 15, nm));
            PeriodicElements.Add(new PeriodicElement(17, "Cl", 2, 16, nm)); PeriodicElements.Add(new PeriodicElement(18, "Ar", 2, 17, ng));
            // 4 周期
            PeriodicElements.Add(new PeriodicElement(19, "K", 3, 0, ak)); PeriodicElements.Add(new PeriodicElement(20, "Ca", 3, 1, akn));
            PeriodicElements.Add(new PeriodicElement(21, "Sc", 3, 2, tr)); PeriodicElements.Add(new PeriodicElement(22, "Ti", 3, 3, tr));
            PeriodicElements.Add(new PeriodicElement(23, "V", 3, 4, tr)); PeriodicElements.Add(new PeriodicElement(24, "Cr", 3, 5, tr));
            PeriodicElements.Add(new PeriodicElement(25, "Mn", 3, 6, tr)); PeriodicElements.Add(new PeriodicElement(26, "Fe", 3, 7, tr));
            PeriodicElements.Add(new PeriodicElement(27, "Co", 3, 8, tr)); PeriodicElements.Add(new PeriodicElement(28, "Ni", 3, 9, tr));
            PeriodicElements.Add(new PeriodicElement(29, "Cu", 3, 10, tr)); PeriodicElements.Add(new PeriodicElement(30, "Zn", 3, 11, tr));
            PeriodicElements.Add(new PeriodicElement(31, "Ga", 3, 12, bm)); PeriodicElements.Add(new PeriodicElement(32, "Ge", 3, 13, ml));
            PeriodicElements.Add(new PeriodicElement(33, "As", 3, 14, ml)); PeriodicElements.Add(new PeriodicElement(34, "Se", 3, 15, nm));
            PeriodicElements.Add(new PeriodicElement(35, "Br", 3, 16, nm)); PeriodicElements.Add(new PeriodicElement(36, "Kr", 3, 17, ng));
            // 5 周期
            PeriodicElements.Add(new PeriodicElement(37, "Rb", 4, 0, ak)); PeriodicElements.Add(new PeriodicElement(38, "Sr", 4, 1, akn));
            PeriodicElements.Add(new PeriodicElement(39, "Y", 4, 2, tr)); PeriodicElements.Add(new PeriodicElement(40, "Zr", 4, 3, tr));
            PeriodicElements.Add(new PeriodicElement(41, "Nb", 4, 4, tr)); PeriodicElements.Add(new PeriodicElement(42, "Mo", 4, 5, tr));
            PeriodicElements.Add(new PeriodicElement(43, "Tc", 4, 6, tr)); PeriodicElements.Add(new PeriodicElement(44, "Ru", 4, 7, tr));
            PeriodicElements.Add(new PeriodicElement(45, "Rh", 4, 8, tr)); PeriodicElements.Add(new PeriodicElement(46, "Pd", 4, 9, tr));
            PeriodicElements.Add(new PeriodicElement(47, "Ag", 4, 10, tr)); PeriodicElements.Add(new PeriodicElement(48, "Cd", 4, 11, tr));
            PeriodicElements.Add(new PeriodicElement(49, "In", 4, 12, bm)); PeriodicElements.Add(new PeriodicElement(50, "Sn", 4, 13, bm));
            PeriodicElements.Add(new PeriodicElement(51, "Sb", 4, 14, ml)); PeriodicElements.Add(new PeriodicElement(52, "Te", 4, 15, ml));
            PeriodicElements.Add(new PeriodicElement(53, "I", 4, 16, nm)); PeriodicElements.Add(new PeriodicElement(54, "Xe", 4, 17, ng));
            // 6 周期
            PeriodicElements.Add(new PeriodicElement(55, "Cs", 5, 0, ak)); PeriodicElements.Add(new PeriodicElement(56, "Ba", 5, 1, akn));
            // (La系 57-71 放于底部第 8 行)
            PeriodicElements.Add(new PeriodicElement(72, "Hf", 5, 3, tr)); PeriodicElements.Add(new PeriodicElement(73, "Ta", 5, 4, tr));
            PeriodicElements.Add(new PeriodicElement(74, "W", 5, 5, tr)); PeriodicElements.Add(new PeriodicElement(75, "Re", 5, 6, tr));
            PeriodicElements.Add(new PeriodicElement(76, "Os", 5, 7, tr)); PeriodicElements.Add(new PeriodicElement(77, "Ir", 5, 8, tr));
            PeriodicElements.Add(new PeriodicElement(78, "Pt", 5, 9, tr)); PeriodicElements.Add(new PeriodicElement(79, "Au", 5, 10, tr));
            PeriodicElements.Add(new PeriodicElement(80, "Hg", 5, 11, tr)); PeriodicElements.Add(new PeriodicElement(81, "Tl", 5, 12, bm));
            PeriodicElements.Add(new PeriodicElement(82, "Pb", 5, 13, bm)); PeriodicElements.Add(new PeriodicElement(83, "Bi", 5, 14, bm));
            PeriodicElements.Add(new PeriodicElement(84, "Po", 5, 15, ml)); PeriodicElements.Add(new PeriodicElement(85, "At", 5, 16, nm));
            PeriodicElements.Add(new PeriodicElement(86, "Rn", 5, 17, ng));
            // 7 周期
            PeriodicElements.Add(new PeriodicElement(87, "Fr", 6, 0, ak)); PeriodicElements.Add(new PeriodicElement(88, "Ra", 6, 1, akn));
            // (Ac系 89-103 放于底部第 9 行)
            PeriodicElements.Add(new PeriodicElement(104, "Rf", 6, 3, tr)); PeriodicElements.Add(new PeriodicElement(105, "Db", 6, 4, tr));
            PeriodicElements.Add(new PeriodicElement(106, "Sg", 6, 5, tr)); PeriodicElements.Add(new PeriodicElement(107, "Bh", 6, 6, tr));
            PeriodicElements.Add(new PeriodicElement(108, "Hs", 6, 7, tr)); PeriodicElements.Add(new PeriodicElement(109, "Mt", 6, 8, tr));
            PeriodicElements.Add(new PeriodicElement(110, "Ds", 6, 9, tr)); PeriodicElements.Add(new PeriodicElement(111, "Rg", 6, 10, tr));
            PeriodicElements.Add(new PeriodicElement(112, "Cn", 6, 11, tr)); PeriodicElements.Add(new PeriodicElement(113, "Nh", 6, 12, bm));
            PeriodicElements.Add(new PeriodicElement(114, "Fl", 6, 13, bm)); PeriodicElements.Add(new PeriodicElement(115, "Mc", 6, 14, bm));
            PeriodicElements.Add(new PeriodicElement(116, "Lv", 6, 15, bm)); PeriodicElements.Add(new PeriodicElement(117, "Ts", 6, 16, nm));
            PeriodicElements.Add(new PeriodicElement(118, "Og", 6, 17, ng));

            // 镧系 (第 8 行，留空 7 行作为分割)
            string[] lan = { "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu" };
            for (int i = 0; i < 15; i++) PeriodicElements.Add(new PeriodicElement(57 + i, lan[i], 8, i + 2, la));

            // 锕系 (第 9 行)
            string[] act = { "Ac", "Th", "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr" };
            for (int i = 0; i < 15; i++) PeriodicElements.Add(new PeriodicElement(89 + i, act[i], 9, i + 2, ac));
        }
    }
}