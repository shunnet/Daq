using Snet.Driver.Core;
using Snet.Model.@enum;
using System.ComponentModel;

namespace Snet.Iot.Daq.data;

public class BytesModel
{
    /// <summary>
    /// 无参构造函数
    /// </summary>
    public BytesModel() { }
    /// <summary>
    /// 全参构造函数
    /// </summary>
    /// <param name="address">地址名称</param>
    /// <param name="describe">描述</param>
    /// <param name="startBit">起始位</param>
    /// <param name="length">长度</param>
    /// <param name="dataType">数据类型</param>
    /// <param name="encodingType">编码类型</param>
    /// <param name="dataFormat">数据格式</param>
    public BytesModel(string address, string describe, int startBit, ushort length, DataType dataType, EncodingType encodingType, DataFormat dataFormat)
    {
        Address = address;
        Describe = describe;
        StartBit = startBit;
        Length = length;
        DataType = dataType;
        EncodingType = encodingType;
        DataFormat = dataFormat;
    }

    /// <summary>
    /// 全参构造函数
    /// </summary>
    /// <param name="address">地址名称</param>
    /// <param name="describe">描述</param>
    /// <param name="startBit">起始位</param>
    /// <param name="length">长度</param>
    /// <param name="dataType">数据类型</param>
    /// <param name="dataFormat">数据格式</param>
    public BytesModel(string address, string describe, int startBit, ushort length, DataType dataType, DataFormat dataFormat)
    {
        Address = address;
        Describe = describe;
        StartBit = startBit;
        Length = length;
        DataType = dataType;
        DataFormat = dataFormat;
    }

    /// <summary>
    /// 全参构造函数
    /// </summary>
    /// <param name="address">地址名称</param>
    /// <param name="describe">描述</param>
    /// <param name="startBit">起始位</param>
    /// <param name="length">长度</param>
    /// <param name="dataType">数据类型</param>
    public BytesModel(string address, string describe, int startBit, ushort length, DataType dataType)
    {
        Address = address;
        Describe = describe;
        StartBit = startBit;
        Length = length;
        DataType = dataType;
    }

    /// <summary>
    /// 地址名称
    /// </summary>
    [Description("地址名称")]
    public string Address { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [Description("描述")]
    public string Describe { get; set; }

    /// <summary>
    /// 起始位
    /// </summary>
    [Description("起始位")]
    public int StartBit { get; set; } = 0;

    /// <summary>
    /// 长度
    /// </summary>
    [Description("长度")]
    public ushort Length { get; set; } = 1;

    /// <summary>
    /// 数据类型
    /// </summary>
    [Description("数据类型")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
    public DataType DataType { get; set; } = DataType.Bool;

    /// <summary>
    /// 编码类型
    /// </summary>
    [Description("编码类型")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
    public EncodingType EncodingType { get; set; } = EncodingType.UTF8;

    /// <summary>
    /// 数据格式
    /// </summary>
    [Description("数据格式")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
    public DataFormat DataFormat { get; set; } = DataFormat.DCBA;
}
