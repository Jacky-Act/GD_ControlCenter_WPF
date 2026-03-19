using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Models.Spectrometer;
using GD_ControlCenter_WPF.Services;
using GD_ControlCenter_WPF.Services.Spectrometer;
using System.Collections.ObjectModel;


namespace GD_ControlCenter_WPF.ViewModels
{
    /// <summary>
    /// 控制面板主逻辑
    /// 职责：统筹各硬件服务、管理设备卡片集合、控制光谱仪按钮
    /// </summary>
    public partial class ControlPanelViewModel : ObservableObject
    {
        // --- 核心服务与子 VM ---    
        [ObservableProperty] private SpectrometerViewModel _specVM; // 光谱仪
        [ObservableProperty] private ObservableCollection<DeviceBaseViewModel> _devices = new();    // 设备卡片集合
        [ObservableProperty] private BatteryViewModel _batteryVM; // 电池

        // --- 布局控制 ---
        [ObservableProperty]
        private double _dashboardHeight;    // 上方卡片区高度
        private double _originalHeight; // 初始计算高度备份
        [ObservableProperty] private double _paramHeaderHeight = 35;    // 参数栏高度
        [ObservableProperty] private string _statusInfo = "系统就绪";

        // --- 硬件控制服务 ---
        private readonly GeneralDeviceService _generalDeviceService;
        private readonly JsonConfigService _jsonConfigService;

        // --- 阀门与防连点锁 ---
        [ObservableProperty] private bool _isSteeringValveActive;
        private bool _isToggling = false;

        // --- 自动打火状态追踪 ---
        private readonly HighVoltageViewModel _hvVM;
        private readonly PeristalticPumpViewModel _pumpVM;
        private bool _isPlasmaStable = false;
        private int _reigniteAttemptCount = 0;
        private const int MaxReigniteAttempts = 3;  // 最大尝试次数
        private bool _isReigniting = false;         // 防止打火序列重入锁

        // --- 数据缓存 ---
        private SpectralData? _latestSpectralData;

        // --- 参考文件状态管理 ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReferenceFileButtonText))]
        private bool _isReferenceFileLoaded;

        // --- 脚本保存状态管理 ---
        [ObservableProperty]
        private bool _isScriptSaving;

        private CancellationTokenSource? _scriptSaveCts;

        // 【新增】：暴露给 View 用于切页面时的状态恢复
        public SpectralData? LatestSpectralData => _latestSpectralData;
        public SpectralData? LoadedReferenceData => _loadedReferenceData;

        // 按钮文本动态绑定
        public string ReferenceFileButtonText => IsReferenceFileLoaded ? "关闭文件" : "打开文件";

        private SpectralData? _loadedReferenceData;

        private readonly System.Collections.Generic.List<Models.Spectrometer.SpectralData> _scriptSaveCache = new();
        private bool _isEmergencyShutdown = false; // 防死锁标记

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

            // 1. 保存硬件 VM 实例
            _hvVM = highVoltageVM;
            _pumpVM = peristalticPumpVM;

            // 2. 订阅硬件状态变化，用于自动打火监控
            _hvVM.PropertyChanged += OnHardwareStateChanged;
            _pumpVM.PropertyChanged += OnHardwareStateChanged;

            // --- 读取本地配置 ---
            var appConfig = _jsonConfigService.Load();
            var specConfig = new SpectrometerConfig();

            if (appConfig.LastIntegrationTime > 0) specConfig.IntegrationTimeMs = appConfig.LastIntegrationTime;
            if (appConfig.LastAveragingCount > 0) specConfig.AveragingCount = appConfig.LastAveragingCount;

            // 初始化光谱仪包装器，并注入保存配置的逻辑
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

            // 直接将注入的 VM 添加到集合中
            Devices.Add(highVoltageVM);
            Devices.Add(peristalticPumpVM);
            Devices.Add(syringePumpVM);

            // 监听光谱仪状态变更消息
            WeakReferenceMessenger.Default.Register<SpectrometerStatusMessage>(this, (r, m) =>
            {
                var incomingConfig = m.Value;

                // 识别出这是来自设置页面的全局通断指令
                if (incomingConfig.SerialNumber == "GLOBAL_SWITCH")
                {
                    bool isEnabled = incomingConfig.IsConnected;

                    if (!isEnabled)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            SpecVM.IsCurrentlyMeasuring = false;
                            SpecVM.Model.IsConnected = false;
                            SpecVM.Model.SerialNumber = "已断开连接";
                            SpecVM.UpdateFromModel();
                            StatusInfo = "光谱仪硬件已被全局禁用";
                            _isToggling = false; // 释放锁
                        });
                    }
                }
            });

            // 监听：实时缓存最新一帧光谱数据，并校验参考图层冲突
            WeakReferenceMessenger.Default.Register<SpectralDataMessage>(this, (r, m) =>
            {
                _latestSpectralData = m.Value;

                // 【新增自动踢出逻辑】：如果硬件正在运行，且当前已打开参考文件
                if (IsReferenceFileLoaded && _loadedReferenceData != null)
                {
                    double[] liveX = _latestSpectralData.Wavelengths;
                    double[] fileX = _loadedReferenceData.Wavelengths;

                    // 校验波长是否不匹配
                    if (liveX.Length != fileX.Length ||
                        Math.Abs(liveX[0] - fileX[0]) > 0.1 ||
                        Math.Abs(liveX[^1] - fileX[^1]) > 0.1)
                    {
                        // 清除内存数据
                        _loadedReferenceData = null;

                        // 必须回到 UI 线程更新绑定的按钮状态和提示语
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsReferenceFileLoaded = false;
                            StatusInfo = "波长不匹配，已自动关闭参考光谱";
                        });

                        // 通知前端撤下静态图层
                        WeakReferenceMessenger.Default.Send(new ClearReferencePlotMessage());
                    }
                }
            });
        }

        // --- 交互命令 ---

        /// <summary>
        /// 光谱仪启停切换逻辑（支持多机联机识别）
        /// </summary>
        [RelayCommand]
        private async Task ToggleSpectrometer()
        {
            // 新增防呆：如果设置里禁用了硬件，直接不让点
            var config = _jsonConfigService.Load();
            if (!config.IsSpectrometerEnabled)
            {
                StatusInfo = "请先在设置中启用光谱仪连接";
                return;
            }

            if (_isToggling) return; // 防止快速连点死锁
            _isToggling = true;

            try
            {
                if (SpectrometerManager.Instance.Devices.Count == 0)
                {
                    StatusInfo = "正在扫描光谱仪...";
                    int count = await SpectrometerManager.Instance.DiscoverAndInitDevicesAsync();
                    if (count == 0) { StatusInfo = "未检测到光谱仪"; return; }
                }

                var devices = SpectrometerManager.Instance.Devices;
                int deviceCount = devices.Count;

                if (!SpecVM.IsCurrentlyMeasuring)
                {
                    StatusInfo = "正在初始化硬件...";

                    bool allSuccess = await Task.Run(async () =>
                    {
                        bool success = true;
                        foreach (var service in devices)
                        {
                            // 判断是否已经连接，已连接则直接返回 true，未连接才执行 InitializeAsync
                            bool isInitialized = service.Config.IsConnected || await service.InitializeAsync();

                            if (isInitialized)
                            {
                                await service.UpdateConfigurationAsync(SpecVM.IntegrationTimeMs, SpecVM.AveragingCount);
                            }
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
                    {
                        StatusInfo = "部分设备初始化失败";
                    }
                }
                else
                {
                    StatusInfo = "正在安全停止硬件...";
                    // 后台执行停止，等待底层 C++ 线程安全退出
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
        /// 转向阀门的切换
        /// </summary>
        partial void OnIsSteeringValveActiveChanged(bool value) => _generalDeviceService.ControlSteeringValve(value);

        /// <summary>
        /// 点火按钮逻辑
        /// </summary>
        [RelayCommand]
        private void Fire() => _generalDeviceService.Fire();

        /// <summary>
        /// 打开采样配置弹窗
        /// </summary>
        [RelayCommand]
        private void SamplingConfig()
        {
            // 1. 实例化窗口对象
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SamplingSettingWindow();

            // 2. 获取当前配置
            var config = _jsonConfigService.Load();

            // 3. 实例化弹窗的 ViewModel，并传入关闭窗口的回调动作
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SamplingSettingViewModel(
                _jsonConfigService,
                config,
                () => window.Close());

            // 4. 绑定 DataContext
            window.DataContext = vm;

            // 5. 指定 Owner，使窗口居中于主程序显示（对应 XAML 中的 WindowStartupLocation="CenterOwner"）
            window.Owner = System.Windows.Application.Current.MainWindow;

            // 6. 以模态对话框形式打开
            window.ShowDialog();
        }

        [RelayCommand]
        private void OpenPlatform3DWindow()
        {
            // 1. 实例化窗口对象
            var window = new GD_ControlCenter_WPF.Views.Dialogs.Platform3DWindow();

            // 2. 实例化 ViewModel (手动注入关闭回调)
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.Platform3DViewModel(() => window.Close());

            // 3. 绑定 DataContext
            window.DataContext = vm;

            // 4. 指定 Owner 居中
            window.Owner = System.Windows.Application.Current.MainWindow;

            // 5. 弹出
            window.ShowDialog();
        }

        /// <summary>
        /// 打开积分时间配置弹窗
        /// </summary>
        [RelayCommand]
        private void OpenIntegrationTimeConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                windowTitle: "积分时间设置",
                headerTitle: "参数配置 - 积分时间 (ms)",
                currentValue: SpecVM.IntegrationTimeMs,
                min: SpecVM.Model.MinIntegrationTime,
                max: 600000,
                step: 5.0,
                applyAction: (val) => SpecVM.ChangeIntegrationTimeCommand.Execute(val),
                closeAction: () => window.Close()
            );
            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 打开平均次数配置弹窗
        /// </summary>
        [RelayCommand]
        private void OpenAveragingCountConfig()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.SpecParamSettingWindow();
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.SpecParamSettingViewModel(
                windowTitle: "平均次数设置",
                headerTitle: "参数配置 - 平均次数",
                currentValue: SpecVM.AveragingCount,
                min: 1,
                max: 10000,
                step: 1.0,
                applyAction: (val) => SpecVM.ChangeAveragingCountCommand.Execute(val),
                closeAction: () => window.Close()
            );
            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 单次保存全谱图到 Excel
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

                // 1. 获取目标文件夹路径
                var config = _jsonConfigService.Load();
                string dirPath = config.SingleSaveExportPath;

                // 防呆：如果用户没设置过，或者之前设置的文件夹被删除了，回退到桌面
                if (string.IsNullOrWhiteSpace(dirPath) || !System.IO.Directory.Exists(dirPath))
                {
                    dirPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                // 2. 构造规范的文件名
                string serial = SpecVM.Model.SerialNumber;
                if (string.IsNullOrEmpty(serial) || serial == "已断开连接") serial = "Unknown";
                if (serial == "联机模式") serial = "MultiDevice";

                string fileName = $"Spectrum_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                string fullPath = System.IO.Path.Combine(dirPath, fileName);

                // 3. 执行导出
                // 这里临时实例化导出服务（如果不涉及全局长会话写入，用完即释放是最佳实践）
                using (var exportService = new ExcelExportService())
                {
                    await exportService.ExportSingleSpectrumAsync(
                        fullPath,
                        _latestSpectralData.AcquisitionTime,
                        SpecVM.IntegrationTimeMs,
                        SpecVM.AveragingCount,
                        _latestSpectralData.Wavelengths,
                        _latestSpectralData.Intensities
                    );
                }

                StatusInfo = "单幅保存成功";
            }
            catch (Exception)
            {
                StatusInfo = "单幅保存失败";
                // 如果需要更详细的错误排查，可以解开下方注释：
                // MessageBox.Show(ex.Message, "导出错误");
            }
        }




        /// <summary>
        /// 切换参考光谱文件的加载与卸载
        /// </summary>
        [RelayCommand]
        private async Task ToggleReferenceFileAsync()
        {
            if (IsReferenceFileLoaded)
            {
                // 执行卸载逻辑
                _loadedReferenceData = null;
                IsReferenceFileLoaded = false;
                WeakReferenceMessenger.Default.Send(new ClearReferencePlotMessage());
                StatusInfo = "已关闭参考光谱";
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    Title = "选择参考光谱文件"
                };

                var config = _jsonConfigService.Load();
                if (!string.IsNullOrWhiteSpace(config.SingleSaveExportPath) && System.IO.Directory.Exists(config.SingleSaveExportPath))
                {
                    dialog.InitialDirectory = config.SingleSaveExportPath;
                }

                if (dialog.ShowDialog() == true)
                {
                    StatusInfo = "正在读取文件...";

                    using var service = new ExcelExportService();
                    var parsedData = await service.ImportSingleSpectrumAsync(dialog.FileName);

                    if (parsedData == null)
                    {
                        StatusInfo = "文件读取失败或格式不符合要求";
                        return;
                    }

                    // 【逻辑修改】：如果当前光谱仪正在运行，才进行波长严格校验；没运行则直接放行加载
                    if (SpecVM.IsCurrentlyMeasuring && _latestSpectralData != null && _latestSpectralData.Wavelengths != null)
                    {
                        double[] liveX = _latestSpectralData.Wavelengths;
                        double[] fileX = parsedData.Wavelengths;

                        if (liveX.Length != fileX.Length ||
                            Math.Abs(liveX[0] - fileX[0]) > 0.1 ||
                            Math.Abs(liveX[^1] - fileX[^1]) > 0.1)
                        {
                            StatusInfo = "加载失败：该文件的波长范围与当前运行的硬件不匹配";
                            return;
                        }
                    }

                    // 载入内存并通知视图层绘图
                    _loadedReferenceData = parsedData;
                    IsReferenceFileLoaded = true;

                    WeakReferenceMessenger.Default.Send(new LoadReferencePlotMessage(_loadedReferenceData));
                    StatusInfo = $"已加载参考光谱: {parsedData.AcquisitionTime:HH:mm:ss}";
                }
            }
        }

        /// <summary>
        /// 打开脚本保存配置弹窗
        /// </summary>
        [RelayCommand]
        private void OpenScriptSaveWindow()
        {
            var window = new GD_ControlCenter_WPF.Views.Dialogs.ScriptSaveWindow();
            var config = _jsonConfigService.Load();

            // 实例化弹窗 VM，传入当前状态和启停回调
            var vm = new GD_ControlCenter_WPF.ViewModels.Dialogs.ScriptSaveViewModel(
                _jsonConfigService,
                config,
                IsScriptSaving,
                () => window.Close(),
                () => { ToggleScriptSaveAsync(); } // 【修改】：去掉 _ = 
            );

            window.DataContext = vm;
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();
        }

        /// <summary>
        /// 核心：启动或停止脚本保存的异步循环 (内存缓存模式)
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
            string dirPath = config.ScriptSaveDirectory;

            if (string.IsNullOrWhiteSpace(dirPath) || !System.IO.Directory.Exists(dirPath))
                dirPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string serial = SpecVM.Model.SerialNumber;
            if (string.IsNullOrEmpty(serial) || serial == "已断开连接") serial = "Unknown";
            if (serial == "联机模式") serial = "MultiDevice";

            string fileName = $"Spectrum_Auto_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string fullPath = System.IO.Path.Combine(dirPath, fileName);

            IsScriptSaving = true;
            _isEmergencyShutdown = false; // 每次正常启动时重置退出标记
            _scriptSaveCts = new CancellationTokenSource();
            var token = _scriptSaveCts.Token;

            // 提取当前参数，防止在保存过程中被修改
            float currentIntTime = SpecVM.IntegrationTimeMs;
            uint currentAvgCount = SpecVM.AveragingCount;

            _ = Task.Run(async () =>
            {
                // 【修改】：使用类的全局缓存，清空旧数据
                _scriptSaveCache.Clear();

                try
                {
                    for (int i = 1; i <= count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusInfo = $"脚本运行中: 正在缓存 {i}/{count} ...";
                        });

                        var dataToSave = _latestSpectralData;
                        if (dataToSave != null && dataToSave.Wavelengths != null)
                        {
                            // 【深拷贝】：防止底层实时数组被覆盖，必须开辟新数组保存快照
                            var snapshot = new Models.Spectrometer.SpectralData
                            {
                                Wavelengths = System.Linq.Enumerable.ToArray(dataToSave.Wavelengths),
                                Intensities = System.Linq.Enumerable.ToArray(dataToSave.Intensities),
                                AcquisitionTime = dataToSave.AcquisitionTime
                            };

                            // 【修改】：塞入全局缓存
                            _scriptSaveCache.Add(snapshot);
                        }

                        if (i < count)
                        {
                            await Task.Delay(interval * 1000, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 被主动取消时跳到这里，什么都不用做，直接进 finally 块落盘
                }
                catch (Exception)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        StatusInfo = "缓存数据时发生异常");
                }
                finally
                {
                    // 【修正】：C# 不允许在 finally 中 return，所以我们用 if 将常规逻辑包裹起来
                    // 如果是紧急退出，直接跳过里面的所有 UI 更新和清理操作，让 finally 自然结束
                    if (!_isEmergencyShutdown)
                    {
                        // 【常规落盘】：一次性落盘判断全局缓存
                        if (_scriptSaveCache.Count > 0)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                StatusInfo = $"正在将 {_scriptSaveCache.Count} 帧数据写入硬盘，请稍候...");

                            using var exportService = new ExcelExportService();

                            // 提取当前快照转为普通列表传给底层保存
                            var dataToSave = _scriptSaveCache.ToList();
                            bool success = exportService.ExportScriptSpectraBulkAsync(fullPath, dataToSave, currentIntTime, currentAvgCount).GetAwaiter().GetResult();

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (success)
                                    StatusInfo = $"脚本保存完成 (共 {dataToSave.Count} 帧，已存至 {fileName})";
                                else
                                    StatusInfo = "写入硬盘失败，请检查文件是否被占用";

                                IsScriptSaving = false;
                            });
                        }
                        else
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusInfo = "未采集到有效数据，已取消保存";
                                IsScriptSaving = false;
                            });
                        }

                        _scriptSaveCache.Clear(); // 释放内存
                    }
                }
            });
        }
        /// <summary>
        /// 紧急静默保存 (专供软件关闭时调用)
        /// </summary>
        public void EmergencySave()
        {
            if (!IsScriptSaving || _scriptSaveCache.Count == 0) return;

            _isEmergencyShutdown = true; // 开启紧急模式，阻止原循环去更新 UI
            IsScriptSaving = false;
            _scriptSaveCts?.Cancel();    // 打断原有的等待循环

            var config = _jsonConfigService.Load();
            string dirPath = string.IsNullOrWhiteSpace(config.ScriptSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : config.ScriptSaveDirectory;

            string serial = SpecVM.Model.SerialNumber;
            if (string.IsNullOrEmpty(serial) || serial == "已断开连接") serial = "Unknown";
            if (serial == "联机模式") serial = "MultiDevice";

            string fileName = $"Spectrum_Auto_{serial}_{DateTime.Now:yyyyMMdd_HHmmss}_Exit.xlsx";
            string fullPath = System.IO.Path.Combine(dirPath, fileName);

            using var exportService = new ExcelExportService();
            // 提取快照并同步落盘
            var dataToSave = _scriptSaveCache.ToList();
            exportService.ExportScriptSpectraBulkAsync(fullPath, dataToSave, SpecVM.IntegrationTimeMs, SpecVM.AveragingCount).GetAwaiter().GetResult();
        }
        /// <summary>
        /// 自动范围调整：根据当前光谱数据的 Y 轴最高点自动缩放
        /// </summary>
        [RelayCommand]
        private void AutoRange()
        {
            // 发送自动缩放请求消息
            WeakReferenceMessenger.Default.Send(new AutoRangeRequestMessage());
        }

        /// <summary>
        /// 最大范围调整：恢复到全局预设的最大观察视野
        /// </summary>
        [RelayCommand]
        private void MaxRange()
        {
            WeakReferenceMessenger.Default.Send(new MaxRangeRequestMessage());
        }

        /// <summary>
        /// 外部（如 MainWindow）初始化高度时调用
        /// </summary>
        public void SetInitialHeight(double height)
        {
            DashboardHeight = height;
            _originalHeight = height;
            ParamHeaderHeight = Math.Max(35, height * 0.15);    // 动态设置参数栏高度
        }

        // --- 自动点火与状态机逻辑 ---

        /// <summary>
        /// 硬件属性变更回调：监听高压电源电流及启停状态
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
        /// 判定等离子体状态及是否意外熄火
        /// </summary>
        private void CheckPlasmaState()
        {
            // 防呆：如果任何一个硬件被手动关闭，立即重置状态并终止打火逻辑
            if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
            {
                _isPlasmaStable = false;
                _reigniteAttemptCount = 0;
                return;
            }

            // 解析当前电流
            if (double.TryParse(_hvVM.MonitorCurrent, out double current))
            {
                // 阈值设定：假设电流 > 5.0mA 视为成功点燃并稳定 (可根据实际情况调整)
                if (current > 5.0)
                {
                    if (!_isPlasmaStable)
                    {
                        _isPlasmaStable = true;
                        _reigniteAttemptCount = 0; // 稳定后重置尝试次数
                        _isReigniting = false;
                        StatusInfo = "等离子体已稳定运行";
                    }
                }
                // 意外熄火判定：原本稳定 -> 突然跌落至 0 附近 (如 <= 1.0mA) -> 且不在重试打火过程中
                else if (current <= 1.0 && _isPlasmaStable && !_isReigniting)
                {
                    _isPlasmaStable = false;
                    _ = HandleUnexpectedExtinctionAsync(); // 触发异步重试序列
                }
            }
        }

        /// <summary>
        /// 异步执行自动打火序列
        /// </summary>
        private async Task HandleUnexpectedExtinctionAsync()
        {
            _isReigniting = true;

            // 检查配置是否允许自动打火
            var config = _jsonConfigService.Load();
            if (!config.IsAutoReigniteEnabled)
            {
                StatusInfo = "意外熄火 (未开启自动点火)";
                _isReigniting = false;
                return;
            }

            while (_reigniteAttemptCount < MaxReigniteAttempts)
            {
                _reigniteAttemptCount++;
                StatusInfo = $"意外熄火，准备尝试第 {_reigniteAttemptCount} 次自动点火...";

                // 缓冲延时：给蠕动泵进液/吹扫预留 2 秒时间
                await Task.Delay(2000);

                // 在延时期间，检查用户是否手动关机了，如果关机则直接中断
                if (!_hvVM.IsRunning || !_pumpVM.IsRunning)
                {
                    StatusInfo = "自动点火已取消 (硬件已手动关闭)";
                    _isReigniting = false;
                    return;
                }

                StatusInfo = $"正在执行第 {_reigniteAttemptCount} 次自动点火...";

                // 发送点火指令
                _generalDeviceService.Fire();

                // 等待点火后的电流攀升时间（此处假设给 3 秒反应时间）
                await Task.Delay(3000);

                // 再次检查电流判断是否点火成功
                if (double.TryParse(_hvVM.MonitorCurrent, out double current) && current > 5.0)
                {
                    // 点燃成功，退出循环，后续将交由 CheckPlasmaState 轮询接管变回 Stable 状态
                    _isReigniting = false;
                    return;
                }
            }

            // 如果走出了 while 循环，说明尝试达到最大次数依然失败
            StatusInfo = "多次自动点火失败，为保障安全已切断输出";

            // 安全切断硬件
            _hvVM.IsRunning = false;
            _pumpVM.IsRunning = false;
            _isReigniting = false;
        }
    }
}