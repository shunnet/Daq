/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using Opc.Ua.Server;

namespace Snet.Iot.Daq.Core.opc.ua.service.core.DurableSubscription
{
    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly JsonSerializerSettings s_settings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            SerializationBinder = new SafeSerializationBinder(),
            Converters = { new ExtensionObjectConverter(), new NumericRangeConverter() }
        };

        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private const string kFilename = "subscriptionsStore.txt";
        private readonly DurableMonitoredItemQueueFactory m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;

        public SubscriptionStore(IServerInternal server)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_telemetry = server.Telemetry;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
        }

        public ValueTask<bool> StoreSubscriptionsAsync(
            IEnumerable<IStoredSubscription> subscriptions,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string result = JsonConvert.SerializeObject(subscriptions, s_settings);

                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                File.WriteAllText(Path.Combine(s_storage_path, kFilename), result);

                if (m_durableMonitoredItemQueueFactory != null)
                {
                    IEnumerable<uint> ids = subscriptions.SelectMany(
                        s => s.MonitoredItems.Select(m => m.Id));
                    m_durableMonitoredItemQueueFactory.PersistQueues(ids, s_storage_path);
                }
                return new ValueTask<bool>(true);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to store subscriptions");
            }
            return new ValueTask<bool>(false);
        }

        public ValueTask<RestoreSubscriptionResult> RestoreSubscriptionsAsync(
            CancellationToken cancellationToken = default)
        {
            string filePath = Path.Combine(s_storage_path, kFilename);
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    List<IStoredSubscription> result =
                        JsonConvert.DeserializeObject<List<IStoredSubscription>>(json, s_settings);

                    File.Delete(filePath);

                    return new ValueTask<RestoreSubscriptionResult>(
                        new RestoreSubscriptionResult(true, result));
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore subscriptions");
            }

            return new ValueTask<RestoreSubscriptionResult>(
                new RestoreSubscriptionResult(false, null));
        }

        public class ExtensionObjectConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(ExtensionObject);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object? existingValue,
                JsonSerializer serializer)
            {
                var jo = JObject.Load(reader);
                object? body = jo["Body"].ToObject<object>(serializer);
                ExpandedNodeId typeId = jo["TypeId"].ToObject<ExpandedNodeId>(serializer);
                return body switch
                {
                    IEncodeable encodeable => new ExtensionObject(typeId, encodeable, false),
                    ByteString binary => new ExtensionObject(typeId, binary),
                    string json => new ExtensionObject(typeId, json),
                    XmlElement xml => new ExtensionObject(typeId, xml),
                    _ => throw new JsonSerializationException(
                        $"不支持持久化的 ExtensionObject body 类型：{body?.GetType().FullName ?? "null"}")
                };
            }

            public override void WriteJson(
                JsonWriter writer,
                object? value,
                JsonSerializer serializer)
            {
                var extensionObject = (ExtensionObject)value;
                // 用 TryGetAsXXX 类型安全读取 body，替代已过时的 Body 属性
                object? body = null;
                if (extensionObject.TryGetValue(out IEncodeable? encodeable))
                {
                    body = encodeable;
                }
                else if (extensionObject.TryGetAsJson(out string? json))
                {
                    body = json;
                }
                else if (extensionObject.TryGetAsXml(out XmlElement xml))
                {
                    body = xml;
                }
                else if (extensionObject.TryGetAsBinary(out ByteString binary))
                {
                    body = binary;
                }
                else
                {
                    throw new JsonSerializationException(
                        "无法读取 ExtensionObject 的 body（非 IEncodeable/Json/Xml/Binary 类型）");
                }
                var jo = new JObject
                {
                    ["Body"] = JToken.FromObject(body, serializer),
                    ["TypeId"] = JToken.FromObject(extensionObject.TypeId, serializer)
                };
                jo.WriteTo(writer);
            }
        }

        public class NumericRangeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(NumericRange);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object? existingValue,
                JsonSerializer serializer)
            {
                var jo = JObject.Load(reader);
                int begin = jo["Begin"].ToObject<int>(serializer);
                int end = jo["End"].ToObject<int>(serializer);
                return new NumericRange(begin, end);
            }

            public override void WriteJson(
                JsonWriter writer,
                object? value,
                JsonSerializer serializer)
            {
                var extensionObject = (NumericRange)value;
                var jo = new JObject
                {
                    ["Begin"] = JToken.FromObject(extensionObject.Begin, serializer),
                    ["End"] = JToken.FromObject(extensionObject.End, serializer)
                };
                jo.WriteTo(writer);
            }
        }

        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(
            uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreDataChangeQueue(
                monitoredItemId,
                s_storage_path);
        }

        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreEventQueue(
                monitoredItemId,
                s_storage_path);
        }

        public ValueTask<IDataChangeMonitoredItemQueue?> RestoreDataChangeMonitoredItemQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IDataChangeMonitoredItemQueue?>(
                RestoreDataChangeMonitoredItemQueue(monitoredItemId));
        }

        public ValueTask<IEventMonitoredItemQueue?> RestoreEventMonitoredItemQueueAsync(
            uint monitoredItemId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IEventMonitoredItemQueue?>(
                RestoreEventMonitoredItemQueue(monitoredItemId));
        }

        public ValueTask OnSubscriptionRestoreCompleteAsync(
            Dictionary<uint, ArrayOf<uint>> createdSubscriptions,
            CancellationToken cancellationToken = default)
        {
            string filePath = Path.Combine(s_storage_path, kFilename);

            //remove old file
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to cleanup files for stored subscsription");
                }
            }
            //remove old batches & queues
            if (m_durableMonitoredItemQueueFactory != null)
            {
                IEnumerable<uint> ids = createdSubscriptions.SelectMany(s => s.Value.Memory.ToArray());
                m_durableMonitoredItemQueueFactory.CleanStoredQueues(s_storage_path, ids);
            }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 持久化 JSON 反序列化白名单：TypeNameHandling.All 会写入并信任类型名，
    /// 不加限制会允许任意类型实例化（反序列化漏洞）。仅放行本方案与 OPC UA 框架类型。
    /// </summary>
    internal sealed class SafeSerializationBinder : Newtonsoft.Json.Serialization.DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            if (!typeName.StartsWith("Snet.Iot.Daq.Core.opc.ua.service.core.DurableSubscription.", StringComparison.Ordinal) &&
                !typeName.StartsWith("Opc.Ua.", StringComparison.Ordinal) &&
                !typeName.StartsWith("System.", StringComparison.Ordinal))
            {
                throw new JsonSerializationException($"禁止反序列化类型：{typeName}");
            }
            return base.BindToType(assemblyName, typeName);
        }
    }
}
