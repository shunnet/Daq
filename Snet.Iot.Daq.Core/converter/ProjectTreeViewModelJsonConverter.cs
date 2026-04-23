using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.converter
{
    /// <summary>
    /// <see cref="IProjectTreeViewModel"/> 的多实现 JSON 转换器。
    /// </summary>
    /// <remarks>
    /// 设计目标：
    /// 1. 优先反序列化到应用层节点模型（WPF / Avalonia），保证 override 行为生效；
    /// 2. 不可用时回退到 Core 模型，保持跨项目可运行；
    /// 3. 减少反射与字符串分配，提升大树结构加载性能。
    /// </remarks>
    public class ProjectTreeViewModelJsonConverter : JsonConverter<IProjectTreeViewModel>
    {
        /// <summary>
        /// 缓存优先反序列化类型，避免重复调用类型解析。
        /// </summary>
        private static readonly Type? PreferredType = GlobalHandler.ResolvePreferredType(
            "Snet.Iot.Daq.data.ProjectTreeViewModel, Snet.Iot.Daq",
            "Snet.Iot.Daq.Avalonia.data.ProjectTreeViewModel, Snet.Iot.Daq.Avalonia");

        /// <summary>
        /// 从 JSON 读取并反序列化为 <see cref="IProjectTreeViewModel"/>。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="typeToConvert">目标类型（接口类型）。</param>
        /// <param name="options">序列化选项。</param>
        /// <returns>反序列化后的项目树节点对象。</returns>
        public override IProjectTreeViewModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            // 性能点：直接从 JsonElement 反序列化，减少 GetRawText 的字符串分配。
            if (PreferredType != null && typeof(IProjectTreeViewModel).IsAssignableFrom(PreferredType))
            {
                return root.Deserialize(PreferredType, options) as IProjectTreeViewModel;
            }

            // 兜底：无上层类型时使用 Core。
            return root.Deserialize<ProjectTreeViewModelCore>(options);
        }

        /// <summary>
        /// 将 <see cref="IProjectTreeViewModel"/> 写入 JSON。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">待序列化对象。</param>
        /// <param name="options">序列化选项。</param>
        public override void Write(Utf8JsonWriter writer, IProjectTreeViewModel value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
