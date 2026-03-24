using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Platform3D;
using GD_ControlCenter_WPF.Services.Spectrometer;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;

/*
 * 文件名: ControlPanelViewModel.cs
 * 模块: 视图模型层 (UI ViewModels)
 * 描述: 主控制面板的中枢神经系统。
 * 架构职责:
 * 1. 【向下管理】：统筹光谱仪、高压电源、蠕动泵等多个子 ViewModel，进行统一的调度。
 * 2. 【状态机】：维护“等离子体自动点火”与“异常熄火恢复”的异步状态流转。
 * 3. 【数据持久化】：作为“单次保存”与“自动化脚本保存”的触发枢纽。
 */

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ControlPanelViewModel : ObservableObject
    {
        #region 1. 核心依赖与子模块 (Dependencies & Sub-Modules)

        /// <summary>
        /// 通用硬件控制服务。负责下发点火指令、控制转向阀门等底层 IO 操作。
        /// </summary>
        private readonly GeneralDeviceService _generalDeviceService;

        /// <summary>
        /// 本地 JSON 配置服务。用于读取和持久化用户设置（如积分时间、采样间隔）。
        /// </summary>
        private readonly JsonConfigService _jsonConfigService;

        /// <summary>
        /// 光谱仪视图模型（子模块）。包装了光谱仪的启停逻辑与参数双向绑定。
        /// </summary>
        [ObservableProperty]
        private SpectrometerViewModel _specVM = null!;

        /// <summary>
        /// 电池视图模型（子模块）。负责电量监控UI的更新。
        /// </summary>
        [ObservableProperty]
        private BatteryViewModel _batteryVM;

        /// <summary>
        /// 高压电源视图模型的后台引用。用于监听电流变化以判断等离子体状态。
        /// </summary>
        private readonly HighVoltageViewModel _hvVM;

        /// <summary>
        /// 蠕动泵视图模型的后台引用。用于判断进液系统是否正常工作。
        /// </summary>
        private readonly PeristalticPumpViewModel _pumpVM;

        /// <summary>
        /// 统一管理的硬件控制卡片集合。暴露给前端 UI 的 ItemsControl 进行动态渲染。
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<DeviceBaseViewModel> _devices = new();

        #endregion

        #region 2. UI 布局与视图状态 (UI & View State)

        /// <summary>
        /// 动态布局：上方仪表盘卡片区域的整体高度。由 MainWindow 调整尺寸时注入。
        /// </summary>
        [ObservableProperty]
        private double _dashboardHeight;

        /// <summary>
        /// 动态布局：光谱图上方参数信息栏的高度。通常为 DashboardHeight 的 15%。
        /// </summary>
        [ObservableProperty]
        private double _paramHeaderHeight = 35;

        /// <summary>
        /// 布局状态记忆：保存窗口初始计算时的原始高度，用于可能的布局重置。
        /// </summary>
        private double _originalHeight;

        /// <summary>
        /// 全局状态指示栏文本。实时向用户反馈当前系统的运行状态或异常警告。
        /// </summary>
        [ObservableProperty]
        private string _statusInfo = "系统就绪";

        /// <summary>
        /// 转向阀门的物理开闭状态。True: 通道1，False: 通道2。
        /// </summary>
        [ObservableProperty]
        private bool _isSteeringValveActive;

        /// <summary>
        /// 动态计算属性：根据当前是否加载了参考文件，改变按钮的显示文本。
        /// </summary>
        public string ReferenceFileButtonText => IsReferenceFileLoaded ? "关闭文件" : "打开文件";

        /// <summary>
        /// 图表视窗缩放缓存 [XMin, XMax, YMin, YMax]，用于跨页面切换时的状态记忆。
        /// </summary>
        public double[]? SavedAxisLimits { get; set; }

        #endregion

        #region 3. 内存流转与持久化缓存 (Data Flow & Cache)

        // 【高危数据区】：此处存放跨线程流转的实时数据与缓存，修改时必须注意并发安全

        /// <summary>
        /// 实时缓存主界面最新一帧的光谱数据，供“单次保存”或“脚本提取”时抓取。
        /// </summary>
        private SpectralData? _latestSpectralData;

        /// <summary>
        /// 暴露给 View 层的只读属性，专门用于页面切换时的状态恢复。
        /// </summary>
        public SpectralData? LatestSpectralData => _latestSpectralData;

        /// <summary>
        /// 载入内存的历史参考光谱数据实体（用于比对的静态红色图层）。
        /// </summary>
        private SpectralData? _loadedReferenceData;

        /// <summary>
        /// 暴露给 View 层的只读属性，专门用于页面切换时恢复参考图层。
        /// </summary>
        public SpectralData? LoadedReferenceData => _loadedReferenceData;

        /// <summary>
        /// 标识当前系统中是否已经加载了外部参考文件。
        /// 触发 Notify 级联更新 ReferenceFileButtonText 属性。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReferenceFileButtonText))]
        private bool _isReferenceFileLoaded;

        /// <summary>
        /// 标识是否正在执行后台“自动化脚本保存”任务。
        /// </summary>
        [ObservableProperty]
        private bool _isScriptSaving;

        /// <summary>
        /// 脚本保存任务的异步取消令牌源。允许用户中途强行打断保存流程。
        /// </summary>
        private CancellationTokenSource? _scriptSaveCts;

        /// <summary>
        /// 脚本保存期间的全局内存缓存池。将多帧光谱数据积攒在内存中，最后一次性落盘以榨干 IO 性能。
        /// </summary>
        private readonly System.Collections.Generic.List<SpectralData> _scriptSaveCache = new();

        /// <summary>
        /// 紧急防死锁标记。当软件被强杀触发 EmergencySave 时，置为 true，
        /// 拦截所有针对 UI (Dispatcher) 的更新操作，让后台线程自然死亡。
        /// </summary>
        private bool _isEmergencyShutdown = false;

        /// <summary>
        /// 硬件防连点锁。防止用户疯狂点击“启动光谱”按钮，导致底层 C++ USB 句柄资源竞争崩溃。
        /// </summary>
        private bool _isToggling = false;

        #endregion

        #region 4. 等离子自动点火状态机 (Plasma State Machine)

        /// <summary>
        /// 核心状态 1：标志等离子体是否已处于稳定燃烧状态（依据电流 > 5.0mA 判定）。
        /// </summary>
        private bool _isPlasmaStable = false;

        /// <summary>
        /// 核心状态 2：防重入锁。标志当前系统是否正在异步执行“意外熄火挽救点火序列”。
        /// 防止电流波动时触发多次并行打火导致硬件损坏。
        /// </summary>
        private bool _isReigniting = false;

        /// <summary>
        /// 核心状态 3：当前挽救序列已经尝试点火的次数。
        /// </summary>
        private int _reigniteAttemptCount = 0;

        /// <summary>
        /// 状态机常量：挽救序列的最大容忍尝试次数。超过此次数将强制切断高压和泵。
        /// </summary>
        private const int MaxReigniteAttempts = 5;

        #endregion

        #region 5. 构造函数与初始化管线

        /// <summary>
        /// 实例化主控面板，采用构造注入获取所有底层服务与硬件 VM。
        /// </summary>
        public ControlPanelViewModel(
            GeneralDeviceService generalDeviceService,
            JsonConfigService jsonConfigService,
            BatteryViewModel batteryVM,
            HighVoltageViewModel highVoltageVM,
            PeristalticPumpViewModel peristalticPumpVM,
            SyringePumpViewModel syringePumpVM)
        {
            _generalDeviceService = generalDeviceService;
            _jsonConfigService = jsonConfigService;
            _batteryVM = batteryVM;
            _hvVM = highVoltageVM;
            _pumpVM = peristalticPumpVM;

            InitSubModules(syringePumpVM);
            SubscribeHardwareEvents();
            RegisterGlobalMessages();
        }

        /// <summary>
        /// 初始化管线 1：装载并配置所有子设备视图模型。
        /// </summary>
        private void InitSubModules(SyringePumpViewModel syringePumpVM)
        {
            var appConfig = _jsonConfigService.Load();
            var specConfig = new SpectrometerConfig();

            if (appConfig.LastIntegrationTime > 0) specConfig.IntegrationTimeMs = appConfig.LastIntegrationTime;
            if (appConfig.LastAveragingCount > 0) specConfig.AveragingCount = appConfig.LastAveragingCount;

            // 初始化光谱仪包装器，并注入其专属的“保存参数回写逻辑”
            SpecVM = new SpectrometerViewModel(specConfig, null!)
            {
                SaveConfigAction = () =>
                {
                    var config = _jsonConfigService.Load();
                    config.LastIntegrationTime = SpecVM.IntegrationTimeMs;
                    config.LastAveragingCount = SpecVM.AveragingCount;
                    _jsonConfigService.Save(config);
                }
            };

            // 统一注册到设备卡片列表中渲染
            Devices.Add(_hvVM);
            Devices.Add(_pumpVM);
            Devices.Add(syringePumpVM);
        }

        /// <summary>
        /// 初始化管线 2：挂载等离子体相关的硬件属性变更事件。
        /// </summary>
        private void SubscribeHardwareEvents()
        {
            _hvVM.PropertyChanged += OnHardwareStateChanged;
            _pumpVM.PropertyChanged += OnHardwareStateChanged;
        }

        /// <summary>
        /// 初始化管线 3：注册全局范围的弱引用消息事件 (MVVM Messenger)。
        /// </summary>
        private void RegisterGlobalMessages()
        {
            // 消息 A：监听来自底层或设置页面的硬件状态/启停警告
            WeakReferenceMessenger.Default.Register<SpectrometerStatusMessage>(this, (r, m) =>
            {
                var incomingConfig = m.Value;
                if (incomingConfig.SerialNumber == "GLOBAL_SWITCH" && !incomingConfig.IsConnected)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SpecVM.IsCurrentlyMeasuring = false;
                        SpecVM.Model.IsConnected = false;
                        SpecVM.Model.SerialNumber = "已断开连接";
                        SpecVM.UpdateFromModel();
                        StatusInfo = "光谱仪硬件已被全局禁用";
                        _isToggling = false;
                    });
                }
            });

            // 消息 B：实时汲取最新光谱数据，并负责安全踢出冲突的参考图层
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                _latestSpectralData = m.Value;

                // 【波长防冲突策略】：如果当前加载了参考图，但实时硬件跑的波长范围和参考图不一样，立即踢出
                if (IsReferenceFileLoaded && _loadedReferenceData != null)
                {
                    double[] liveX = _latestSpectralData.Wavelengths;
                    double[] fileX = _loadedReferenceData.Wavelengths;

                    if (liveX.Length != fileX.Length || Math.Abs(liveX[0] - fileX[0]) > 0.1 || Math.Abs(liveX[^1] - fileX[^1]) > 0.1)
                    {
                        _loadedReferenceData = null;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsReferenceFileLoaded = false;
                            StatusInfo = "波长不匹配，已自动关闭参考光谱";
                        });

                        WeakReferenceMessenger.Default.Send(new ClearReferencePlotMessage());
                    }
                }
            });
        }

        #endregion

        #region 6. 核心业务流水线 (启停调度 & 持久化)

        /// <summary>
        /// 光谱仪启停核心流水线。
        /// </summary>
        [RelayCommand]
        private async Task ToggleSpectrometer()
        {
            // --- 阶段 1：防呆校验 ---
            var config = _jsonConfigService.Load();
            if (!config.IsSpectrometerEnabled)
            {
                StatusInfo = "请先在设置中启用光谱仪连接";
                return;
            }

            if (_isToggling) return; // 硬件锁
            _isToggling = true;

            try
            {
                // --- 阶段 2：硬件扫描 ---
                if (SpectrometerManager.Instance.Devices.Count == 0)
                {
                    StatusInfo = "正在扫描光谱仪...";
                    int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
                    if (count == 0) { StatusInfo = "未检测到光谱仪"; return; }
                }

                var devices = SpectrometerManager.Instance.Devices;
                int deviceCount = devices.Count;

                // --- 阶段 3：分发启动/停止指令 ---
                if (!SpecVM.IsCurrentlyMeasuring)
                {
                    StatusInfo = "正在初始化硬件...";

                    bool allSuccess = await Task.Run(async () =>
                    {
                        bool success = true;
                        foreach (var service in devices)
                        {
                            bool isInitialized = service.Config.IsConnected || await service.InitializeAsync();
                            if (isInitialized)
                                await service.UpdateConfigurationAsync(SpecVM.IntegrationTimeMs, SpecVM.AveragingCount);
                            else
                            {
                                success = false;
                                break;
                            }
                        }
                        if (success) SpectrometerManager.Instance.StartAll();
                        return success;
                    });

                    if (allSuccess)
                    {
                        SpecVM.SetService(devices.First());
                        SpecVM.Model.SerialNumber = deviceCount == 1 ? devices.First().Config.SerialNumber : "联机模式";
                        SpecVM.Model.IsConnected = true;
                        SpecVM.UpdateFromModel();
                        SpecVM.IsCurrentlyMeasuring = true;
                        StatusInfo = deviceCount == 1 ? $"采集进行中: {devices.First().Config.SerialNumber}" : $"多机联机采集运行中 (共 {deviceCount} 台)";
                    }
                    else
                        StatusInfo = "部分设备初始化失败";
                }
                else
                {
                    StatusInfo = "正在安全停止硬件...";
                    await Task.Run(() => SpectrometerManager.Instance.StopAll());
                    SpecVM.IsCurrentlyMeasuring = false;
                    StatusInfo = "采集已停止";
                }
            }
            finally
            {
                _isToggling = false; // 释放锁
            }
        }

        /// <summary>
        /// 后台自动化脚本采集引擎 (带防死锁与紧急抢救机制)。
        /// </summary>
        private void ToggleScriptSaveAsync()
        {
            if (IsScriptSaving)
            {
                _scriptSaveCts?.Cancel();
                IsScriptSaving = false;
                StatusInfo = "正在中止采集并保存已缓存数据...";
                return;
            }

            if (!SpecVM.IsCurrentlyMeasuring || _latestSpectralData == null)
            {
                StatusInfo = "请先启动光谱仪采集数据";
                return;
            }

            var config = _jsonConfigService.Load();
            int count = config.ScriptSaveCount;
            int interval = config.ScriptSaveInterval;
            string dirPath = string.IsNullOrWhiteSpace(config.ScriptSaveDirectory) || !System.IO.Directory.Exists(config.ScriptSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : config.ScriptSaveDirectory;

            string serial = SpecVM.Model.SerialNumber == "已断开连接" ? "Unknown" : (SpecVM.Model.SerialNumber == "联机模式" ? "MultiDevice" : SpecVM.Model.SerialNumber);
            string fileName = $"Spectrum_Auto_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string fullPath = System.IO.Path.Combine(dirPath, fileName);

            IsScriptSaving = true;
            _isEmergencyShutdown = false;
            _scriptSaveCts = new CancellationTokenSource();
            var token = _scriptSaveCts.Token;

            float currentIntTime = SpecVM.IntegrationTimeMs;
            uint currentAvgCount = SpecVM.AveragingCount;

            _ = Task.Run(async () =>
            {
                _scriptSaveCache.Clear();

                try
                {
                    for (int i = 1; i <= count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        Application.Current.Dispatcher.Invoke(() => StatusInfo = $"脚本运行中: 正在缓存 {i}/{count} ...");

                        var dataToSave = _latestSpectralData;
                        if (dataToSave != null && dataToSave.Wavelengths != null)
                        {
                            _scriptSaveCache.Add(new SpectralData
                            {
                                Wavelengths = dataToSave.Wavelengths.ToArray(),
                                Intensities = dataToSave.Intensities.ToArray(),
                                AcquisitionTime = dataToSave.AcquisitionTime
                            });
                        }

                        if (i < count) await Task.Delay(interval * 1000, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { Application.Current.Dispatcher.Invoke(() => StatusInfo = "缓存数据时发生异常"); }
                finally
                {
                    // 【防死锁安全阀】：如果是软件关闭引发的退出，绝不可更新 UI，直接让线程自然死亡
                    if (!_isEmergencyShutdown)
                    {
                        if (_scriptSaveCache.Count > 0)
                        {
                            Application.Current.Dispatcher.Invoke(() => StatusInfo = $"正在将 {_scriptSaveCache.Count} 帧数据写入硬盘，请稍候...");

                            using var exportService = new ExcelExportService();
                            var dataToSave = _scriptSaveCache.ToList();
                            bool success = exportService.ExportScriptSpectraBulkAsync(fullPath, dataToSave, currentIntTime, currentAvgCount).GetAwaiter().GetResult();

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusInfo = success ? $"脚本保存完成 (共 {dataToSave.Count} 帧，已存至 {fileName})" : "写入硬盘失败，请检查文件是否被占用";
                                IsScriptSaving = false;
                            });
                        }
                        else
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusInfo = "未采集到有效数据，已取消保存";
                                IsScriptSaving = false;
                            });
                        }
                        _scriptSaveCache.Clear();
                    }
                }
            });
        }

        /// <summary>
        /// 紧急静默保存 (专供 App.xaml.cs 在软件意外关闭时调用)。
        /// </summary>
        public void EmergencySave()
        {
            if (!IsScriptSaving || _scriptSaveCache.Count == 0) return;

            _isEmergencyShutdown = true;
            IsScriptSaving = false;
            _scriptSaveCts?.Cancel();

            var config = _jsonConfigService.Load();
            string dirPath = string.IsNullOrWhiteSpace(config.ScriptSaveDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : config.ScriptSaveDirectory;
            string serial = SpecVM.Model.SerialNumber == "联机模式" ? "MultiDevice" : (string.IsNullOrEmpty(SpecVM.Model.SerialNumber) ? "Unknown" : SpecVM.Model.SerialNumber);
            string fileName = $"Spectrum_Auto_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}_Exit.xlsx";

            using var exportService = new ExcelExportService();
            exportService.ExportScriptSpectraBulkAsync(System.IO.Path.Combine(dirPath, fileName), _scriptSaveCache.ToList(), SpecVM.IntegrationTimeMs, SpecVM.AveragingCount).GetAwaiter().GetResult();
        }

        #endregion

        #region 7. UI 控制与通用对话框命令 (Commands)

        /// <summary>
        /// 监听 UI 层转向阀 Toggle 按钮的变化并下发至硬件。
        /// </summary>
        partial void OnIsSteeringValveActiveChanged(bool value) => _generalDeviceService.ControlSteeringValve(value);

        /// <summary>
        /// 触发单次点火。
        /// </summary>
        [RelayCommand] private void Fire() => _generalDeviceService.Fire();

        /// <summary>
        /// 请求视图层自适应 X/Y 轴范围。
        /// </summary>
        [RelayCommand] private void AutoRange() => WeakReferenceMessenger.Default.Send(new AutoRangeRequestMessage());

        /// <summary>
        /// 请求视图层恢复至全局默认的极限最大范围。
        /// </summary>
        [RelayCommand] private void MaxRange() => WeakReferenceMessenger.Default.Send(new MaxRangeRequestMessage());

        /// <summary>
        /// 打开“自动化采样序列”配置弹窗。
        /// </summary>
        [RelayCommand]
        private void SamplingConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SamplingSettingWindow();
            window.DataContext = new GD_ControlCenter_WPF.ViewModels.Dialogs.SamplingSettingViewModel(_jsonConfigService, _jsonConfigService.Load(), () => window.Close());
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 打开三维平台控制弹窗。
        /// </summary>
        [RelayCommand]
        private void OpenPlatform3DWindow()
        {
            var platformService = App.Services.GetRequiredService<IPlatform3DService>();
            var calibrationService = App.Services.GetRequiredService<PlatformCalibrationService>();
            var jsonConfigService = App.Services.GetRequiredService<JsonConfigService>();
            // 【新增】获取寻峰服务
            var peakTrackingService = App.Services.GetRequiredService<PeakTrackingService>();

            var window = new GD_ControlCenter_WPF.Views.Dialogs.Platform3DWindow();

            // 【修改】将 peakTrackingService 传入 ViewModel
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.Platform3DViewModel(
                platformService,
                calibrationService,
                jsonConfigService,
                peakTrackingService,
                () => window.Close());

            window.DataContext = vm;
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();

            vm.SavePreferences();
        }

        /// <summary>
        /// 打开积分时间快捷调节弹窗。
        /// </summary>
        [RelayCommand]
        private void OpenIntegrationTimeConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            window.DataContext = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                "积分时间设置", "参数配置 - 积分时间 (ms)", SpecVM.IntegrationTimeMs, SpecVM.Model.MinIntegrationTime, 600000, 5.0,
                (val) => SpecVM.ChangeIntegrationTimeCommand.Execute(val), () => window.Close());
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 打开平均次数快捷调节弹窗。
        /// </summary>
        [RelayCommand]
        private void OpenAveragingCountConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            window.DataContext = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                "平均次数设置", "参数配置 - 平均次数", SpecVM.AveragingCount, 1, 10000, 1.0,
                (val) => SpecVM.ChangeAveragingCountCommand.Execute(val), () => window.Close());
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 执行单次全谱数据保存。
        /// </summary>
        [RelayCommand]
        private async Task SingleSaveAsync()
        {
            if (_latestSpectralData == null || _latestSpectralData.Wavelengths == null || _latestSpectralData.Wavelengths.Length == 0)
            {
                StatusInfo = "暂无光谱数据可保存";
                return;
            }

            try
            {
                StatusInfo = "正在导出单幅光谱...";
                var config = _jsonConfigService.Load();
                string dirPath = string.IsNullOrWhiteSpace(config.SingleSaveExportPath) || !System.IO.Directory.Exists(config.SingleSaveExportPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : config.SingleSaveExportPath;
                string serial = SpecVM.Model.SerialNumber == "联机模式" ? "MultiDevice" : (string.IsNullOrEmpty(SpecVM.Model.SerialNumber) ? "Unknown" : SpecVM.Model.SerialNumber);

                using var exportService = new ExcelExportService();
                await exportService.ExportSingleSpectrumAsync(System.IO.Path.Combine(dirPath, $"Spectrum_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"), _latestSpectralData.AcquisitionTime, SpecVM.IntegrationTimeMs, SpecVM.AveragingCount, _latestSpectralData.Wavelengths, _latestSpectralData.Intensities);

                StatusInfo = "单幅保存成功";
            }
            catch (Exception) { StatusInfo = "单幅保存失败"; }
        }

        /// <summary>
        /// 打开或关闭本地参考光谱图层。
        /// </summary>
        [RelayCommand]
        private async Task ToggleReferenceFileAsync()
        {
            if (IsReferenceFileLoaded)
            {
                _loadedReferenceData = null;
                IsReferenceFileLoaded = false;
                WeakReferenceMessenger.Default.Send(new ClearReferencePlotMessage());
                StatusInfo = "已关闭参考光谱";
            }
            else
            {
                var config = _jsonConfigService.Load();
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx", Title = "选择参考光谱文件" };
                if (!string.IsNullOrWhiteSpace(config.SingleSaveExportPath) && System.IO.Directory.Exists(config.SingleSaveExportPath))
                    dialog.InitialDirectory = config.SingleSaveExportPath;

                if (dialog.ShowDialog() == true)
                {
                    StatusInfo = "正在读取文件...";
                    using var service = new ExcelExportService();
                    var parsedData = await service.ImportSingleSpectrumAsync(dialog.FileName);

                    if (parsedData == null) { StatusInfo = "文件读取失败或格式不符合要求"; return; }

                    if (SpecVM.IsCurrentlyMeasuring && _latestSpectralData != null && _latestSpectralData.Wavelengths != null)
                    {
                        double[] liveX = _latestSpectralData.Wavelengths;
                        double[] fileX = parsedData.Wavelengths;
                        if (liveX.Length != fileX.Length || Math.Abs(liveX[0] - fileX[0]) > 0.1 || Math.Abs(liveX[^1] - fileX[^1]) > 0.1)
                        {
                            StatusInfo = "加载失败：该文件的波长范围与当前运行的硬件不匹配";
                            return;
                        }
                    }

                    _loadedReferenceData = parsedData;
                    IsReferenceFileLoaded = true;
                    WeakReferenceMessenger.Default.Send(new LoadReferencePlotMessage(_loadedReferenceData));
                    StatusInfo = $"已加载参考光谱: {parsedData.AcquisitionTime:HH:mm:ss}";
                }
            }
        }

        /// <summary>
        /// 打开脚本保存参数设置二级弹窗。
        /// </summary>
        [RelayCommand]
        private void OpenScriptSaveWindow()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.ScriptSaveWindow();
            window.DataContext = new GD_ControlCenter_WPF.ViewModels.Dialogs.ScriptSaveViewModel(_jsonConfigService, _jsonConfigService.Load(), IsScriptSaving, () => window.Close(), () => { ToggleScriptSaveAsync(); });
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 供外层 (MainWindow) 传入当前可用高度，以动态调整仪表盘区域高度。
        /// </summary>
        public void SetInitialHeight(double height)
        {
            DashboardHeight = height;
            _originalHeight = height;
            ParamHeaderHeight = Math.Max(35, height * 0.15);
        }

        /// <summary>
        /// 当此类的 IsScriptSaving 属性发生变更时，自动由 MVVM Toolkit 触发此方法。
        /// 用于向打开的二级窗口同步状态，防止其按钮状态卡死。
        /// </summary>
        partial void OnIsScriptSavingChanged(bool value)
        {
            WeakReferenceMessenger.Default.Send(new ScriptSaveStateChangedMessage(value));
        }

        #endregion

        #region 8. 自动点火核心控制逻辑 (Hardware Event Callbacks)

        /// <summary>
        /// 监听底层泵和电源状态的流转事件。
        /// </summary>
        private void OnHardwareStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HighVoltageViewModel.MonitorCurrent) ||
                e.PropertyName == nameof(HighVoltageViewModel.IsRunning) ||
                e.PropertyName == nameof(PeristalticPumpViewModel.IsRunning))
            {
                CheckPlasmaState();
            }
        }

        /// <summary>
        /// 评估当前电流状态，驱动等离子状态机。
        /// </summary>
        private void CheckPlasmaState()
        {
            // 防呆校验：若物理开关被手动切断，直接复位监控器状态
            if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
            {
                _isPlasmaStable = false;
                _reigniteAttemptCount = 0;
                return;
            }

            if (double.TryParse(_hvVM.MonitorCurrent, out double current))
            {
                // 阈值判定点 1：认定为点燃成功并趋于稳定 (> 5.0mA)
                if (current > 5.0)
                {
                    if (!_isPlasmaStable)
                    {
                        _isPlasmaStable = true;
                        _reigniteAttemptCount = 0;
                        _isReigniting = false;
                        StatusInfo = "等离子体已稳定运行";
                    }
                }
                // 阈值判定点 2：认定为意外熄火 (原本稳定，突然跌落 <= 1.0mA，且没在挽救流程中)
                else if (current <= 1.0 && _isPlasmaStable && !_isReigniting)
                {
                    _isPlasmaStable = false;
                    _ = HandleUnexpectedExtinctionAsync();
                }
            }
        }

        /// <summary>
        /// 异步挽救流程：执行异常熄火后的重新点火指令序列。
        /// </summary>
        private async Task HandleUnexpectedExtinctionAsync()
        {
            _isReigniting = true;

            if (!_jsonConfigService.Load().IsAutoReigniteEnabled)
            {
                StatusInfo = "意外熄火 (未开启自动点火)";
                _isReigniting = false;
                return;
            }

            while (_reigniteAttemptCount < MaxReigniteAttempts)
            {
                _reigniteAttemptCount++;
                StatusInfo = $"意外熄火，准备尝试第 {_reigniteAttemptCount} 次自动点火...";

                // 挽救等待缓冲区：给蠕动泵进液留足 2 秒喘息
                await Task.Delay(2000);

                // 【中断校验】：如果在等待缓冲期内，用户看形势不对直接手动关机了，立即终止挽救
                if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
                {
                    StatusInfo = "自动点火已取消 (硬件已手动关闭)";
                    _isReigniting = false;
                    return;
                }

                StatusInfo = $"正在执行第 {_reigniteAttemptCount} 次自动点火...";
                _generalDeviceService.Fire();

                // 等火烧起来的反应时间
                await Task.Delay(3000);

                if (double.TryParse(_hvVM.MonitorCurrent, out double current) && current > 5.0)
                {
                    _isReigniting = false; // 点燃成功，退出死循环，交由 CheckPlasmaState 接管后续判断
                    return;
                }
            }

            // 三次打火依然拉不起来，判定为物理硬件故障，强行切断系统保护安全
            StatusInfo = "多次自动点火失败，为保障安全已切断输出";
            _hvVM.IsRunning = false;
            _pumpVM.IsRunning = false;
            _isReigniting = false;
        }

        #endregion
    }
}