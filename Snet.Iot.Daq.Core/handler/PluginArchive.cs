using System.IO.Compression;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>两端共用的插件压缩包解压规则，先验证全部路径和声明大小，再以实际读取字节数限制解压体积。</summary>
public static class PluginArchive
{
    /// <summary>压缩文件最大 100 MiB，与 Web 上传流限制一致。</summary>
    public const long MaxArchiveBytes = 100L * 1024 * 1024;
    /// <summary>全部解压文件的最大累计体积为 200 MiB。</summary>
    public const long MaxExtractBytes = 200L * 1024 * 1024;
    /// <summary>压缩包最多包含 5000 个文件和目录条目。</summary>
    public const int MaxEntries = 5000;

    /// <summary>安全地解压到调用方独占的暂存目录，不覆盖已存在文件；失败时由调用方清理其暂存目录。</summary>
    /// <param name="archivePath">本地 ZIP 文件路径。</param>
    /// <param name="destination">全新暂存目录，不能包含指向其他目录的链接；正式插件目录不能作为此参数。</param>
    /// <param name="token">取消读取和写入的令牌。</param>
    /// <returns>全部条目解压完成的任务，不加载或执行插件程序集。</returns>
    /// <exception cref="InvalidDataException">路径越界、链接条目、压缩包或解压体积超限。</exception>
    public static async Task ExtractAsync(string archivePath, string destination, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (new FileInfo(archivePath).Length > MaxArchiveBytes)
            throw new InvalidDataException("插件压缩包超过 100 MiB 限制");
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("压缩包条目过多");
        long declaredBytes = 0;
        var entries = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var entry in archive.Entries)
        {
            declaredBytes = checked(declaredBytes + entry.Length);
            if (declaredBytes > MaxExtractBytes) throw new InvalidDataException("解压后体积超过限制");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("插件压缩包不能包含符号链接");
            var relative = entry.FullName.Replace('\\', '/');
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.Equals(root, comparison) && !path.StartsWith(prefix, comparison))
                throw new InvalidDataException("非法的压缩包路径");
            entries.Add((entry, path));
        }
        Directory.CreateDirectory(root);
        // 复用有界缓冲区，不能只相信 ZIP 声明的未压缩长度。
        var buffer = new byte[64 * 1024];
        long extractedBytes = 0;
        foreach (var (entry, path) in entries)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: buffer.Length, FileOptions.Asynchronous);
            int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                extractedBytes = checked(extractedBytes + count);
                if (extractedBytes > MaxExtractBytes) throw new InvalidDataException("解压后体积超过限制");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
        }
    }
    /// <summary>异步压缩目录，先写入独占临时文件，完整关闭 ZIP 后原子替换目标；取消或失败保留旧压缩包。</summary>
    /// <param name="sourceDirectory">发布完成的插件目录，不跟随文件系统链接。</param>
    /// <param name="archivePath">目标 ZIP 文件，不能位于被压缩的目录内部。</param>
    /// <param name="token">取消文件读取、压缩写入和最终提交的令牌。</param>
    /// <returns>压缩完成且目标文件替换成功的任务。</returns>
    public static async Task CreateAsync(string sourceDirectory, string archivePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(archivePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (target.StartsWith(source + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("压缩文件不能位于源目录内部", nameof(archivePath));
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            {
                await using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        IgnoreInaccessible = false
                    };
                    foreach (var path in Directory.EnumerateFileSystemEntries(source, "*", options))
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(source, path).Replace('\\', '/');
                        var directory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                        var entry = archive.CreateEntry(directory ? relative + "/" : relative, CompressionLevel.Optimal);
                        if (directory) continue;
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var entryStream = entry.Open();
                        await input.CopyToAsync(entryStream, 64 * 1024, token);
                    }
                }
                await output.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

}
