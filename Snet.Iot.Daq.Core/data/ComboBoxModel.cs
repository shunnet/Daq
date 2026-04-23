using Snet.Iot.Daq.Core.mvvm;

namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 下拉框数据模型<br/>
    /// 用于 ComboBox 等选择控件的数据绑定<br/>
    /// 重写 ToString 返回 Key，确保 UI 正确显示
    /// </summary>
    public class ComboBoxModel : BindNotify
    {
        /// <summary>
        /// 构造函数：创建键值对模型
        /// </summary>
        /// <param name="key">显示文本（用于 UI 展示）</param>
        /// <param name="value">关联数据值（用于业务逻辑）</param>
        public ComboBoxModel(string key, object value)
        {
            this.Key = key;
            this.Value = value;
        }

        /// <summary>
        /// 显示键<br/>
        /// 用于在 UI 控件中显示的文本内容
        /// </summary>
        public string Key
        {
            get => GetProperty(() => Key);
            set => SetProperty(() => Key, value);
        }

        /// <summary>
        /// 关联值<br/>
        /// 存储与显示键对应的业务数据
        /// </summary>
        public object Value
        {
            get => GetProperty(() => Value);
            set => SetProperty(() => Value, value);
        }

        /// <summary>
        /// 重写 ToString 方法<br/>
        /// 返回 Key 值，确保 ComboBox 等控件正确显示文本
        /// </summary>
        /// <returns>显示键文本</returns>
        public override string ToString()
        {
            return Key;
        }
    }
}