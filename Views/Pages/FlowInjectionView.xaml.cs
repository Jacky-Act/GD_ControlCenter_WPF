using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models.Messages;
using System.Windows.Controls;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class FlowInjectionView : UserControl
    {
        private ScottPlot.Plottables.DataLogger? _dataLogger;

        public FlowInjectionView()
        {
            InitializeComponent();

        }

      
    }
}