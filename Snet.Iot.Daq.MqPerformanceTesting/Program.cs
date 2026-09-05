using Snet.Log;
using Snet.Model.data;
using Snet.Mqtt.client;
using Snet.Utility;



long count = 0;
long lastCount = 0;

using System.Threading.Timer timer = new System.Threading.Timer(_ =>
{
    long current = Interlocked.Read(ref count);
    long diff = current - Interlocked.Exchange(ref lastCount, current);

    LogHelper.Info($"每秒接收次数: {diff}");
}, null, 1000, 1000);


await using MqttClientOperate operate = await MqttClientOperate.InstanceAsync(new MqttClientData.Basics()
{
    IpAddress = "127.0.0.1",
    Port = 1234
});
OperateResult result = await operate.OnAsync();
operate.OnDataEventAsync += Operate_OnDataEventAsync;
if (result.GetDetails(out string? msg))
{
    result = await operate.ConsumeAsync("s7real");
    result = await operate.ConsumeAsync("s7");
    result = await operate.ConsumeAsync("modbus");
    result = await operate.ConsumeAsync("ua");
    result = await operate.ConsumeAsync("da");
    result = await operate.ConsumeAsync("toyota");
    result = await operate.ConsumeAsync("ge");
}
if (result.Status)
{
    Console.ReadLine();
}
operate.OnDataEventAsync -= Operate_OnDataEventAsync;
if (!result.Status) LogHelper.Error(result.ToJson(true));





Task Operate_OnDataEventAsync(object? sender, EventDataResult e)
{
    if (e.Status) Interlocked.Increment(ref count);
    return Task.CompletedTask;
}


