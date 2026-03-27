using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.Generic;

namespace GD_ControlCenter_WPF.Models.Messages
{
    // 样品的类型枚举
    public enum SampleType
    {
        空白,    // Blank
        标液,    // Standard
        待测液   // Unknown
    }

    /// <summary>
    /// 样品序列中的单行数据实体
    /// </summary>
    public partial class SampleItemModel : ObservableObject
    {
        [ObservableProperty] private string _sampleName = "新样品";
        [ObservableProperty] private SampleType _type = SampleType.待测液;

        // 标液专属浓度（如果是空白或待测液，UI 上会被隐藏或禁用）
        [ObservableProperty] private double? _standardConcentration;

        [ObservableProperty] private int _repeats = 1;      // 重复测量次数
        [ObservableProperty] private string _status = "等待"; // 状态：等待/测量中/完成/跳过

        // 深度复制方法（用于右键菜单的复制/粘贴）
        public SampleItemModel Clone()
        {
            return new SampleItemModel
            {
                SampleName = this.SampleName + " - 副本",
                Type = this.Type,
                StandardConcentration = this.StandardConcentration,
                Repeats = this.Repeats,
                Status = "等待"
            };
        }
    }

    /// <summary>
    /// 当样品序列发生改变（或者确认下发任务）时，发送此消息。
    /// 连续测量页和流动注射页订阅此消息，即可拿到最新的进样列表。
    /// </summary>
    public class SampleSequenceChangedMessage : ValueChangedMessage<List<SampleItemModel>>
    {
        public SampleSequenceChangedMessage(List<SampleItemModel> value) : base(value) { }
    }
}