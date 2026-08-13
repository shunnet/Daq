using Snet.Iot.Daq.Core.data;

namespace Snet.Iot.Daq.Web.Data;

/// <summary>
/// 地址实体（与 WPF 版 DAQ 同名子类）。sqlite-net 表名 = 类名，
/// 统一使用 "AddressModel" 表，保证 address.db 可在 WPF / Web 间直接互拷共用。
/// </summary>
public class AddressModel : AddressModelCore
{
}
