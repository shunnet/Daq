using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 可回收的插件程序集加载上下文<br/>
    /// 每个插件程序集使用独立的 AssemblyLoadContext（isCollectible = true）加载，<br/>
    /// 卸载时调用 Unload() 并释放所有引用，GC 将回收程序集占用的内存，实现插件热移除。<br/>
    /// 所有程序集均通过内存流加载，避免对 DLL 文件持有文件锁，支持卸载后立即删除文件。
    /// </summary>
    internal class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        /// <summary>
        /// 创建可回收的插件加载上下文
        /// </summary>
        /// <param name="pluginPath">插件 DLL 的完整路径，用于解析依赖项</param>
        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        /// <summary>
        /// 以内存流方式加载程序集文件，不锁定磁盘文件，支持卸载后立即删除。<br/>
        /// 同时尝试加载同目录下的 PDB 调试符号。
        /// </summary>
        /// <param name="assemblyPath">程序集 DLL 的完整路径</param>
        /// <returns>加载后的程序集</returns>
        public Assembly LoadFromFileStream(string assemblyPath)
        {
            using var ms = new MemoryStream(File.ReadAllBytes(assemblyPath));

            string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                using var pdbMs = new MemoryStream(File.ReadAllBytes(pdbPath));
                return LoadFromStream(ms, pdbMs);
            }

            return LoadFromStream(ms);
        }

        /// <summary>
        /// 尝试从插件目录解析依赖程序集<br/>
        /// 优先检查默认上下文中是否已加载同名程序集（如定义 IDaq/IMq 的 Snet.Model），<br/>
        /// 若已存在则返回 null 复用默认上下文版本，避免类型身份不一致导致 as 转换返回 null。<br/>
        /// 仅当默认上下文中不存在时，以内存流方式从插件目录加载。
        /// </summary>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // 已在默认上下文中加载的程序集直接复用，保证共享接口类型一致性
            string? targetName = assemblyName.Name;
            if (targetName != null)
            {
                foreach (Assembly loaded in Default.Assemblies)
                {
                    if (string.Equals(loaded.GetName().Name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }

            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromFileStream(assemblyPath);
            }
            return null;
        }

        /// <summary>
        /// 尝试从插件目录解析非托管 DLL
        /// </summary>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            return IntPtr.Zero;
        }
    }
}
