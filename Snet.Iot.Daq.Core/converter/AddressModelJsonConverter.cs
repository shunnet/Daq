using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.converter
{
    public class AddressModelJsonConverter : JsonConverter<IAddressModel>
    {
        public override IAddressModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var json = document.RootElement.GetRawText();
            return JsonSerializer.Deserialize<AddressModelCore>(json, options);
        }

        public override void Write(Utf8JsonWriter writer, IAddressModel value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
