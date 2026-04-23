using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.converter
{
    public class ProjectTreeViewModelJsonConverter : JsonConverter<IProjectTreeViewModel>
    {
        public override IProjectTreeViewModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var json = document.RootElement.GetRawText();
            return JsonSerializer.Deserialize<ProjectTreeViewModelCore>(json, options);
        }

        public override void Write(Utf8JsonWriter writer, IProjectTreeViewModel value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
