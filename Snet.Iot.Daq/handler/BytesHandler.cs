using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Driver.Core;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@enum;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler;

/// <summary>
/// 字节处理<br/>
/// 须引用 Snet.Core 与 Snet.Driver
/// </summary>

public class BytesHandler : CoreUnify<BytesHandler, string>, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 无参构造函数
    /// </summary>
    public BytesHandler() : base() { }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    /// <param name="basics">基础数据</param>
    public BytesHandler(string basics) : base(basics) { }

    /// <inheritdoc/>
    public override void Dispose()
    {
        outValue?.Clear();
        outValue = null;
        base.Dispose();
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        outValue?.Clear();
        outValue = null;
        await base.DisposeAsync();
    }

    /// <summary>
    /// 转换
    /// </summary>
    private RegularByteTransform transform;

    /// <summary>
    /// 出参
    /// </summary>
    private ConcurrentDictionary<string, AddressValue> outValue;

    /// <summary>
    /// 设置值
    /// </summary>
    /// <param name="value">值</param>
    /// <param name="source">源</param>
    /// <param name="param">参数</param>
    /// <param name="msg">消息</param>
    /// <param name="quality">质量</param>
    /// <returns>返回标准的 <see cref="AddressValue"/></returns>
    private AddressValue SettingsValue(object? value, byte[] source, DateTime time, BytesModel param, string? msg = null, QualityType quality = QualityType.Normal)
    {
        msg ??= "成功".GetLanguageValue();
        return new AddressValue()
        {
            AddressName = param.Address,
            AddressDataType = param.DataType,
            AddressDescribe = param.Describe,
            AddressType = AddressType.Reality,
            EncodingType = param.EncodingType,
            Time = time,
            OriginalValue = source,
            ResultValue = value,
            Message = msg,
            Quality = quality,
        };
    }

    /// <summary>
    /// 转换<br/>
    /// 只允许对值类型进行转换
    /// </summary>
    /// <param name="bytes">字节源</param>
    /// <param name="time">字节源收到的时间</param>
    /// <param name="transformParams">
    /// 转换参数集合<br/>
    /// 转换 bytes 中的数据
    /// </param>
    /// <param name="token">取消通知</param>
    /// <returns>返回对应的键值数据</returns>
    public async Task<OperateResult> TransformAsync(byte[] bytes, DateTime time, IEnumerable<BytesModel> transformParams, CancellationToken token = default)
    {
        //开始操作
        await BegOperateAsync(token);
        //实例化
        transform ??= new();
        outValue ??= new();
        //每一次执行清理
        outValue?.Clear();
        try
        {
            //开始进行转换
            foreach (var param in transformParams)
            {
                //从源中拷贝字节
                byte[] buffer = new byte[param.Length];
                Array.Copy(bytes, param.StartBit, buffer, 0, param.Length);
                //设置转换类型
                transform.DataFormat = param.DataFormat;
                try
                {
                    switch (param.DataType)
                    {
                        case DataType.Bool:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransBool(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Double:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransDouble(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Float:
                        case DataType.Single:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransSingle(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Short:
                        case DataType.Int16:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransInt16(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Ushort:
                        case DataType.UInt16:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransUInt16(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Int:
                        case DataType.Int32:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransInt32(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Uint:
                        case DataType.UInt32:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransUInt32(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Long:
                        case DataType.Int64:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransInt64(buffer, 0), buffer, time, param));
                            break;
                        case DataType.Ulong:
                        case DataType.UInt64:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransUInt64(buffer, 0), buffer, time, param));
                            break;
                        case DataType.String:
                        case DataType.Char:
                            outValue.TryAdd(param.Address, SettingsValue(transform.TransString(buffer, 0, param.Length, param.EncodingType.GetEncoding()), buffer, time, param));
                            break;
                        default:
                            return await EndOperateAsync(false, param.DataType + " " + "不支持类型转换".GetLanguageValue(App.LanguageOperate), token: token);
                    }
                }
                catch (Exception ex)
                {
                    outValue.TryAdd(param.Address, SettingsValue(null, buffer, time, param, "转换失败：".GetLanguageValue(App.LanguageOperate) + ex.Message, QualityType.Exception));
                }
            }
            return EndOperate(true, resultData: outValue);
        }
        catch (TaskCanceledException ex)
        {
            return await EndOperateAsync(false, "任务被取消", exception: ex, token: token);
        }
        catch (OperationCanceledException ex)
        {
            //取消通知
            return await EndOperateAsync(false, "取消通知", exception: ex, token: token);
        }
        catch (Exception ex)
        {
            //出现异常
            return await EndOperateAsync(false, ex.Message, exception: ex, token: token);
        }
    }
}
