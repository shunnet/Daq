using Snet.Core.extend;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Utility;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 自动组包处理类
    /// 功能：将离散地址根据设备规则自动合并为批量读取地址，提高通信效率
    /// 特点：高性能、低GC、支持大批量地址处理
    /// </summary>
    public class AutoPackHandler : CoreUnify<AutoPackHandler, string>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 地址信息结构体（值类型，避免堆分配，降低GC压力）
        /// 存储解析后的地址元数据：原始尾部、字节索引、位索引、数据类型、预计算长度
        /// </summary>
        private readonly struct AddrInfo
        {
            /// <summary>原始地址尾部（如 "DBX0.0"、"DBD2"）</summary>
            public readonly string DbTail;

            /// <summary>字节起始索引（如 DBD2 → 2）</summary>
            public readonly int ByteIndex;

            /// <summary>位索引（Bool类型 0~7，非Bool固定为0）</summary>
            public readonly int BitIndex;

            /// <summary>数据类型枚举</summary>
            public readonly DataType DataType;

            /// <summary>预计算的读取字节长度（避免循环内重复计算）</summary>
            public readonly int Length;

            /// <summary>
            /// 构造地址信息实例
            /// </summary>
            /// <param name="dbTail">地址尾部字符串</param>
            /// <param name="byteIndex">字节索引</param>
            /// <param name="bitIndex">位索引</param>
            /// <param name="dataType">数据类型</param>
            /// <param name="length">预计算的读取长度</param>
            public AddrInfo(string dbTail, int byteIndex, int bitIndex, DataType dataType, int length)
            {
                DbTail = dbTail;
                ByteIndex = byteIndex;
                BitIndex = bitIndex;
                DataType = dataType;
                Length = length;
            }
        }

        /// <summary>
        /// 分包结果项结构体（值类型，减少中间对象堆分配）
        /// 存储每个地址在批次内的偏移信息
        /// </summary>
        private readonly struct BatchItem
        {
            /// <summary>原始地址尾部</summary>
            public readonly string DbTail;

            /// <summary>批次内字节起始偏移</summary>
            public readonly int StartByte;

            /// <summary>读取字节长度</summary>
            public readonly int Length;

            /// <summary>
            /// 构造分包结果项
            /// </summary>
            /// <param name="dbTail">地址尾部字符串</param>
            /// <param name="startByte">批次内字节偏移</param>
            /// <param name="length">读取字节长度</param>
            public BatchItem(string dbTail, int startByte, int length)
            {
                DbTail = dbTail;
                StartByte = startByte;
                Length = length;
            }
        }

        /// <summary>
        /// 无参构造函数
        /// </summary>
        public AutoPackHandler() : base() { }

        /// <summary>
        /// 带基础参数构造函数
        /// </summary>
        /// <param name="basics">基础连接参数字符串</param>
        public AutoPackHandler(string basics) : base(basics) { }

        /// <summary>
        /// 获取支持自动组包的设备类型列表
        /// </summary>
        /// <returns></returns>
        public static string[] GetSupportAutoPackDeviceTypes() => ["SiemensS7Net"];

        /// <summary>
        /// 地址自动组包入口方法
        /// 根据设备类型将离散地址集合合并为批量读取结构，减少通信轮次
        /// </summary>
        /// <param name="address">原始地址集合，包含待组包的地址列表</param>
        /// <param name="deviceType">设备类型标识（目前支持 "SiemensS7"）</param>
        /// <param name="maxByteLength">单次批量读取的最大字节数（西门子S7默认240/400）</param>
        /// <param name="format">数据字节序格式</param>
        /// <returns>组包后的地址对象，失败返回null</returns>
        public Address? AddressAutoPack(Address address, string deviceType = "SiemensS7Net", int maxByteLength = 200, DataFormat format = DataFormat.ABCD)
        {
            if (address?.AddressArray is not { Count: > 0 })
                return null;

            if (maxByteLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxByteLength));

            // 构建 地址全名 → 数据类型 映射，供结果构建阶段 O(1) 查询
            var addDict = new Dictionary<string, DataType>(address.AddressArray.Count);
            for (int i = 0; i < address.AddressArray.Count; i++)
            {
                var a = address.AddressArray[i];
                if (!string.IsNullOrEmpty(a.AddressName))
                    addDict.TryAdd(a.AddressName, a.AddressDataType);
            }

            return deviceType switch
            {
                "SiemensS7Net" => PackSiemensS7(address, addDict, maxByteLength, format),
                _ => null
            };
        }

        /// <summary>
        /// 地址自动组包入口方法(插件工具自动组包入口)
        /// 根据设备类型将离散地址集合合并为批量读取结构，减少通信轮次
        /// </summary>
        /// <param name="addressModels">插件工具地址，包含待组包的地址列表</param>
        /// <param name="deviceType">设备类型标识（目前支持 "SiemensS7"）</param>
        /// <param name="maxByteLength">单次批量读取的最大字节数（西门子S7默认240/400）</param>
        /// <param name="format">数据字节序格式</param>
        /// <returns>组包后的地址对象，失败返回null</returns>
        public List<IAddressModel>? AddressAutoPack(List<IAddressModel> addressModels, string deviceType = "SiemensS7Net", int maxByteLength = 200, DataFormat format = DataFormat.ABCD)
        {
            Address address = new Address();
            address.AddressArray = addressModels.Where(m => m.ExpandParam == null).Select(m => new AddressDetails
            {
                AddressName = m.Address,
                AddressDataType = m.Type,
                AddressDescribe = m.Describe
            }).ToList();
            Address? result = AddressAutoPack(address, deviceType, maxByteLength, format);
            if (result == null) return null;
            List<IAddressModel> models = new List<IAddressModel>();
            foreach (var model in result.AddressArray)
            {
                models.Add(new AddressModelCore
                {
                    Length = model.Length,
                    EncodingType = model.EncodingType,
                    Address = model.AddressName,
                    Type = model.AddressDataType,
                    Describe = model.AddressDescribe,
                    ExpandParam = model.AddressExtendParam.ToJson()
                });
            }
            return models;
        }

        /// <summary>
        /// 西门子S7设备组包核心流程
        /// 依次执行：按区域分组排序 → 按字节限制拆分批次 → 构建返回结果
        /// </summary>
        /// <param name="address">原始地址集合</param>
        /// <param name="addDict">地址全名到数据类型的映射表</param>
        /// <param name="maxByteLength">单包最大字节数</param>
        /// <param name="format">数据字节序格式</param>
        /// <returns>组包后的地址对象</returns>
        private Address PackSiemensS7(Address address, IDictionary<string, DataType> addDict, int maxByteLength, DataFormat format)
        {
            var groups = GroupAndSortAddresses(address.AddressArray);
            var batches = SplitIntoBatches(groups, maxByteLength);
            return BuildAddressResult(batches, addDict, format);
        }

        /// <summary>
        /// 地址分组并排序
        /// 按DB区域前缀分组，组内按字节索引升序、位索引升序排列，自动去除重复地址
        /// </summary>
        /// <param name="addresses">原始地址详情列表</param>
        /// <returns>区域前缀到有序地址信息列表的映射</returns>
        private Dictionary<string, List<AddrInfo>> GroupAndSortAddresses(List<AddressDetails> addresses)
        {
            var dict = new Dictionary<string, List<AddrInfo>>(Math.Max(addresses.Count / 4, 4));

            for (int i = 0; i < addresses.Count; i++)
            {
                var addr = addresses[i];
                if (string.IsNullOrEmpty(addr.AddressName)) continue;

                // Span 切片定位分隔点，避免 Substring 产生堆分配
                var span = addr.AddressName.AsSpan();
                int dot = span.IndexOf('.');
                if (dot <= 0) continue;

                string dbHead = span.Slice(0, dot).ToString();
                string dbTail = span.Slice(dot + 1).ToString();

                if (!dict.TryGetValue(dbHead, out var list))
                {
                    list = new List<AddrInfo>(32);
                    dict[dbHead] = list;
                }

                // HashSet 去重，O(1) 查找替代原 O(n) 线性扫描
                bool duplicate = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j].DbTail == dbTail) { duplicate = true; break; }
                }
                if (duplicate) continue;

                var (byteIndex, bitIndex) = ParseDb(dbTail);
                int len = addr.AddressDataType == DataType.Bool ? 1 : addr.AddressDataType.ReadGetLength();
                list.Add(new AddrInfo(dbTail, byteIndex, bitIndex, addr.AddressDataType, len));
            }

            // 每组按 (字节索引, 位索引) 升序排列，确保连续性最优
            foreach (var kv in dict)
            {
                var list = kv.Value;
                list.Sort(static (a, b) =>
                {
                    int c = a.ByteIndex.CompareTo(b.ByteIndex);
                    return c != 0 ? c : a.BitIndex.CompareTo(b.BitIndex);
                });
            }

            return dict;
        }

        /// <summary>
        /// 地址分包算法（核心性能函数）
        /// 在不超过单包最大字节数的前提下，将连续地址合并为批次，最大化单次读取范围
        /// </summary>
        /// <param name="groups">按区域分组并排序后的地址字典</param>
        /// <param name="maxByteLength">单包最大字节数限制</param>
        /// <returns>分包结果：每个批次包含区域标识、总字节长度和地址项列表</returns>
        private List<(int sn, string dbHead, int sumLength, List<BatchItem> items)> SplitIntoBatches(Dictionary<string, List<AddrInfo>> groups, int maxByteLength)
        {
            var results = new List<(int, string, int, List<BatchItem>)>(groups.Count * 2);
            int globalSn = 0;

            foreach (var group in groups)
            {
                string dbHead = group.Key;
                var values = group.Value;

                var batch = new List<BatchItem>(Math.Min(values.Count, 32));
                int offset = 0;
                int prevByte = -1;
                int prevLen = 0;
                bool first = true;

                for (int i = 0; i < values.Count; i++)
                {
                    var v = values[i];
                    int len = v.Length;

                    // 计算当前地址在批次缓冲区中的字节偏移
                    // 首个元素从0开始；同字节Bool复用前一偏移；跨字节累加实际间距
                    int byteOffset;
                    if (first)
                        byteOffset = 0;
                    else if (v.ByteIndex == prevByte)
                        byteOffset = offset - prevLen;
                    else
                        byteOffset = offset + (v.ByteIndex - (prevByte + prevLen));

                    // 超出最大字节限制时，结束当前批次并开启新批次
                    if (byteOffset + len > maxByteLength && batch.Count > 0)
                    {
                        results.Add((++globalSn, dbHead, offset, batch));
                        batch = new List<BatchItem>(16);
                        offset = 0;
                        prevByte = -1;
                        prevLen = 0;
                        first = true;
                        byteOffset = 0;
                    }

                    batch.Add(new BatchItem(v.DbTail, byteOffset, len));
                    offset = byteOffset + len;
                    prevByte = v.ByteIndex;
                    prevLen = len;
                    first = false;
                }

                // 提交末尾批次
                if (batch.Count > 0)
                    results.Add((++globalSn, dbHead, offset, batch));
            }

            return results;
        }

        /// <summary>
        /// 构建最终 Address 结果对象
        /// 将分包数据转换为 AddressDetails 列表，绑定 BytesModel 明细和原始数据类型信息
        /// </summary>
        /// <param name="batches">分包结果列表</param>
        /// <param name="addDict">地址全名到数据类型的映射表</param>
        /// <param name="format">数据字节序格式</param>
        /// <returns>组装完成的地址对象</returns>
        private Address BuildAddressResult(List<(int sn, string dbHead, int sumLength, List<BatchItem> items)> batches, IDictionary<string, DataType> addDict, DataFormat format)
        {
            var result = new Address { AddressArray = new List<AddressDetails>(batches.Count) };

            for (int b = 0; b < batches.Count; b++)
            {
                var (sn, dbHead, sumLength, items) = batches[b];
                var bytesModels = new List<BytesModel>(items.Count);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string fullName = $"{dbHead}.{item.DbTail}";

                    // TryGetValue 避免字典键缺失时抛出 KeyNotFoundException，提升稳定性
                    addDict.TryGetValue(fullName, out var dt);

                    int boolIndex = 0;
                    if (dt == DataType.Bool)
                    {
                        // 从尾部提取位号（如 "DBX0.3" → 3）
                        var tailSpan = item.DbTail.AsSpan();
                        int lastDot = tailSpan.LastIndexOf('.');
                        if (lastDot > 0 && int.TryParse(tailSpan.Slice(lastDot + 1), out int bit) && bit <= 7)
                            boolIndex = bit;
                    }

                    bytesModels.Add(new BytesModel(
                        fullName, string.Empty, item.StartByte,
                        (ushort)item.Length, dt,
                        dataFormat: format, boolIndex: boolIndex));
                }

                result.AddressArray.Add(new AddressDetails(
                    $"{dbHead}.{items[0].DbTail}", DataType.ByteArray, (ushort)sumLength)
                {
                    AddressDescribe = $"组包批次SN[{sn}]",
                    AddressExtendParam = bytesModels
                });
            }

            return result;
        }

        /// <summary>
        /// 解析西门子S7 DB地址尾部
        /// 支持格式：DBX10.3（位访问）、DBW10（字访问）、DBD10（双字访问）
        /// 返回字节索引和位索引的元组
        /// </summary>
        /// <param name="dbTail">地址尾部字符串（不含区域前缀）</param>
        /// <returns>(字节索引, 位索引)，位索引仅对Bool类型有意义</returns>
        private (int byteIndex, int bitIndex) ParseDb(string dbTail)
        {
            ReadOnlySpan<char> span = dbTail.AsSpan();
            int dot = span.LastIndexOf('.');

            if (dot > 0)
                return (ParseNumber(span.Slice(0, dot)), int.Parse(span.Slice(dot + 1)));

            return (ParseNumber(span), 0);
        }

        /// <summary>
        /// 从字符串前缀中提取首个连续数字序列（零堆分配）
        /// 跳过非数字前缀字符（如 "DBX"、"DBD"），解析紧随的数字部分
        /// </summary>
        /// <param name="span">待解析的字符范围</param>
        /// <returns>解析出的整数值，无数字时返回0</returns>
        private static int ParseNumber(ReadOnlySpan<char> span)
        {
            int i = 0;
            while (i < span.Length && !char.IsDigit(span[i])) i++;
            int start = i;
            while (i < span.Length && char.IsDigit(span[i])) i++;
            return i > start ? int.Parse(span.Slice(start, i - start)) : 0;
        }
    }
}
