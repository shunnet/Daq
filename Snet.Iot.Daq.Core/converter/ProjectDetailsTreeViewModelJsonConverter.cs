using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.converter
{
    /// <summary>
    /// <see cref="IProjectDetailsTreeViewModel"/> 的多实现 JSON 转换器。
    /// </summary>
    /// <remarks>
    /// 设计目标：
    /// 1. 优先绑定到应用层详情节点模型（WPF / Avalonia），确保业务重写方法可用；
    /// 2. 不可用时安全回退到 Core 模型；
    /// 3. 在频繁读配置场景中减少分配与反射重复开销。
    /// </remarks>
    public class ProjectDetailsTreeViewModelJsonConverter : JsonConverter<IProjectDetailsTreeViewModel>
    {
        /// <summary>
        /// 缓存优先目标类型，降低每次反序列化的类型解析成本。
        /// </summary>
        private static readonly Type? PreferredType = GlobalHandler.ResolvePreferredType(
            "Snet.Iot.Daq.data.ProjectDetailsTreeViewModel, Snet.Iot.Daq",
            "Snet.Iot.Daq.Avalonia.data.ProjectDetailsTreeViewModel, Snet.Iot.Daq.Avalonia");

        /// <summary>
        /// 从 JSON 读取并反序列化为 <see cref="IProjectDetailsTreeViewModel"/>。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="typeToConvert">目标类型（接口类型）。</param>
        /// <param name="options">序列化选项。</param>
        /// <returns>反序列化后的项目详情树节点对象。</returns>
        public override IProjectDetailsTreeViewModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            // 性能点：避免中间字符串创建，直接从 JsonElement 反序列化。
            if (PreferredType != null && typeof(IProjectDetailsTreeViewModel).IsAssignableFrom(PreferredType))
            {
                return root.Deserialize(PreferredType, options) as IProjectDetailsTreeViewModel;
            }

            // 兜底：上层类型不存在时使用 Core。
            return root.Deserialize<ProjectDetailsTreeViewModelCore>(options);
        }

        /// <summary>
        /// 将 <see cref="IProjectDetailsTreeViewModel"/> 写入 JSON。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">待序列化对象。</param>
        /// <param name="options">序列化选项。</param>
        public override void Write(Utf8JsonWriter writer, IProjectDetailsTreeViewModel value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
