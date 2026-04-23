using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.Core.converter;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Utility;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.@interface
{
    /// <summary>
    /// 地址模型接口
    /// </summary>
    [JsonConverter(typeof(AddressModelJsonConverter))]
    public interface IAddressModel
    {
        /// <summary>
        /// 信息事件
        /// </summary>
        event EventHandlerAsync<EventInfoResult> OnInfoEventAsync;

        /// <summary>
        /// 异步消息源传递
        /// </summary>
        /// <param name="sender">自身对象</param>
        /// <param name="e">事件结果</param>
        Task OnInfoEventHandlerAsync(object? sender, EventInfoResult e);

        /// <summary>
        /// 地址
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        DataType Type { get; set; }

        /// <summary>
        /// 长度
        /// </summary>
        ushort Length { get; set; }

        /// <summary>
        /// 编码类型
        /// </summary>
        EncodingType EncodingType { get; set; }

        /// <summary>
        /// 序号
        /// </summary>
        int Index { get; set; }

        /// <summary>
        /// 唯一标识
        /// </summary>
        string Guid { get; set; }

        /// <summary>
        /// 别名
        /// </summary>
        string AnotherName { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        string Describe { get; set; }

        /// <summary>
        /// 数据传输主题
        /// </summary>
        string Topic { get; set; }

        /// <summary>
        /// 数据传输精简值
        /// </summary>
        bool SimplifyValue { get; set; }

        /// <summary>
        /// 扩展参数
        /// </summary>
        string ExpandParam { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        DateTime Time { get; set; }

        /// <summary>
        /// 选中
        /// </summary>
        bool IsSelected { get; set; }

        /// <summary>
        /// 修改地址
        /// </summary>
        IAsyncRelayCommand Update { get; set; }

        /// <summary>
        /// 修改地址函数
        /// </summary>
        /// <returns></returns>
        Task UpdateAsync();

        /// <summary>
        /// 撤销修改
        /// </summary>
        void Revoke(int index);

        /// <summary>
        /// 模型转换成地址类型
        /// </summary>
        /// <returns>地址详情</returns>
        AddressDetails Convert();
    }
}
