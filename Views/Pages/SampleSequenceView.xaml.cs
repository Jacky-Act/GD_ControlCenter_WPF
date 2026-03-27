using GD_ControlCenter_WPF.Models.Messages;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GD_ControlCenter_WPF.Views.Pages
{
    public partial class SampleSequenceView : UserControl
    {
        private bool _isDragging = false;
        private int _draggedItemIndex = -1;

        public SampleSequenceView()
        {
            InitializeComponent();

            // 绑定拖拽接收事件
            SequenceDataGrid.PreviewMouseMove += SequenceDataGrid_PreviewMouseMove;
            SequenceDataGrid.Drop += SequenceDataGrid_Drop;
            SequenceDataGrid.AllowDrop = true;
        }

        // 1. 鼠标在“把手”上按下时，记录起始索引
        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
            if (row != null)
            {
                _draggedItemIndex = row.GetIndex();
                _isDragging = true;
            }
        }

        // 2. 鼠标移动时，如果按住左键，则启动拖放操作
        private void SequenceDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;

            var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
            if (row != null)
            {
                DragDrop.DoDragDrop(row, row.Item, DragDropEffects.Move);
                _isDragging = false; // 重置状态
            }
        }

        // 3. 放下时，计算插入位置并在 ViewModel 中调换顺序
        private void SequenceDataGrid_Drop(object sender, DragEventArgs e)
        {
            _isDragging = false;
            var targetRow = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);

            if (targetRow != null && e.Data.GetDataPresent(typeof(SampleItemModel)))
            {
                int targetIndex = targetRow.GetIndex();

                // 从 DataGrid 拿到绑定的集合
                if (SequenceDataGrid.ItemsSource is ObservableCollection<SampleItemModel> collection)
                {
                    var draggedItem = collection[_draggedItemIndex];

                    // 移动元素
                    collection.RemoveAt(_draggedItemIndex);
                    collection.Insert(targetIndex, draggedItem);

                    // 自动选中拖拽的元素
                    SequenceDataGrid.SelectedIndex = targetIndex;
                }
            }
        }

        // 辅助方法：向上查找可视树找到 DataGridRow
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }
    }
}