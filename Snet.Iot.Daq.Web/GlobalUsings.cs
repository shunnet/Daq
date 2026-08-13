// 含 @ 关键字转义的命名空间不能写在 razor 的 @using 指令里（Razor 会把第二个 @ 当作代码过渡符，
// 且 enum/interface 等关键字在 C# using 指令中必须转义），统一放全局 using（C# 编译单元支持 @ 转义）
global using Snet.Iot.Daq.Core.@interface;
global using Snet.Iot.Daq.Core.@enum;
global using Snet.Model.@enum;
global using Snet.Model.@interface;
