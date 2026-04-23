using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.converter
{
    /// <summary>
    /// <see cref="IAddressModel"/> 的多实现 JSON 转换器。
    /// </summary>
    /// <remarks>
    /// 设计目标：
    /// 1. 运行时优先绑定到上层应用模型（WPF / Avalonia），确保重写逻辑生效；
    /// 2. 若上层模型不可用，则回退到 Core 模型，保证兼容性；
    /// 3. 通过缓存类型与减少中间字符串分配，降低高频反序列化开销。
    /// </remarks>
    public class AddressModelJsonConverter : JsonConverter<IAddressModel>
    {
        /// <summary>
        /// 缓存优先反序列化目标类型，避免每次反序列化都通过反射解析类型。
        /// </summary>
        private static readonly Type? PreferredType = GlobalHandler.ResolvePreferredType(
            "Snet.Iot.Daq.data.AddressModel, Snet.Iot.Daq",
            "Snet.Iot.Daq.Avalonia.data.AddressModel, Snet.Iot.Daq.Avalonia");

        /// <summary>
        /// 从 JSON 读取并反序列化为 <see cref="IAddressModel"/>。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="typeToConvert">目标类型（接口类型）。</param>
        /// <param name="options">序列化选项。</param>
        /// <returns>反序列化后的地址对象。</returns>
        public override IAddressModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            // 性能点：避免 GetRawText() 产生额外字符串分配，直接从 JsonElement 反序列化。
            if (PreferredType != null && typeof(IAddressModel).IsAssignableFrom(PreferredType))
            {
                return root.Deserialize(PreferredType, options) as IAddressModel;
            }

            // 兜底：当上层程序集不可用时，回退到 Core 模型。
            return root.Deserialize<AddressModelCore>(options);
        }

        /// <summary>
        /// 将 <see cref="IAddressModel"/> 写入 JSON。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">待序列化对象。</param>
        /// <param name="options">序列化选项。</param>
        public override void Write(Utf8JsonWriter writer, IAddressModel value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
