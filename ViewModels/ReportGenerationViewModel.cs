using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GD_ControlCenter_WPF.ViewModels
{
    public partial class ReportGenerationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _statusInfo = "报告生成模块暂未开放";
    }
}
