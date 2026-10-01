namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 操作日志读取：遍历 logs/{日期}/operate/{用户名}/ 目录，供用户管理页按日期/用户筛选查看。
/// </summary>
public static class OperateLogReader
{
    private const int MaxLogLines = 20_000;
    private const int MaxLogLineLength = 16_384;
    private static string LogsRoot => Path.Combine(WebPaths.DataDir, "logs");

    /// <summary>解析日志子目录并保证结果仍位于日志根目录内。</summary>
    private static bool TryResolveDirectory(out string directory, params string[] segments)
    {
        directory = string.Empty;
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment != Path.GetFileName(segment)
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return false;

        var root = Path.GetFullPath(LogsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine([LogsRoot, .. segments]));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        directory = candidate;
        return true;
    }

    /// <summary>所有日志日期目录（倒序，最新在前）</summary>
    #region 日期与用户
    public static List<string> GetDates()
    {
        try
        {
            if (!Directory.Exists(LogsRoot)) return new();
            return Directory.GetDirectories(LogsRoot)
                .Select(Path.GetFileName)
                .Where(d => !string.IsNullOrEmpty(d) && d != "operate")
                .OrderByDescending(d => d)
                .ToList()!;
        }
        catch { return new(); }
    }

    /// <summary>指定日期下有操作日志的用户（倒序按目录名）</summary>
    public static List<string> GetUsers(string date)
    {
        try
        {
            if (!TryResolveDirectory(out var operateDir, date, "operate")) return new();
            if (!Directory.Exists(operateDir)) return new();
            return Directory.GetDirectories(operateDir).Select(Path.GetFileName).OrderByDescending(u => u).ToList()!;
        }
        catch { return new(); }
    }

    #endregion

    #region 日志行读取
    /// <summary>指定日期+用户的操作日志行（合并当天全部 .log 文件，按时间序），行格式 "HH:mm:ss | 级别 | 内容"</summary>
    public static List<string> GetLines(string date, string user) => GetLinesAsync(date, user).GetAwaiter().GetResult();

    /// <summary>异步读取操作日志，最多保留 20000 行，每行 16 KiB 字符；一次读取最多扫描 32 MiB 字符。</summary>
    /// <param name="date">日志日期目录名，不允许路径片段。</param>
    /// <param name="user">用户目录名，不允许路径片段。</param>
    /// <param name="token">取消读取；取消不会转换为空结果。</param>
    /// <returns>按时间排序的有界显示行快照；不存在或不可读取的目录返回空列表。</returns>
    public static async Task<List<string>> GetLinesAsync(string date, string user, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!TryResolveDirectory(out var dir, date, "operate", user) || !Directory.Exists(dir)) return new();
            var lines = new List<(DateTime Time, string Line)>();
            var buffer = new char[4096];
            var scanned = 0;
            const int maxScanCharacters = 32 * 1024 * 1024;
            foreach (var file in Directory.EnumerateFiles(dir, "*.log").OrderBy(file => file))
            {
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream);
                var line = new System.Text.StringBuilder();
                int count;
                while (scanned < maxScanCharacters && lines.Count < MaxLogLines
                    && (count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxScanCharacters - scanned)), token).ConfigureAwait(false)) != 0)
                {
                    scanned += count;
                    for (var i = 0; i < count; i++)
                    {
                        if (buffer[i] == '\n')
                        {
                            AddLogLine(lines, line.ToString().TrimEnd('\r'));
                            line.Clear();
                            if (lines.Count >= MaxLogLines) break;
                        }
                        else if (line.Length < MaxLogLineLength) line.Append(buffer[i]);
                    }
                }
                if (lines.Count < MaxLogLines && line.Length > 0) AddLogLine(lines, line.ToString().TrimEnd('\r'));
                if (lines.Count >= MaxLogLines || scanned >= maxScanCharacters) break;
            }
            return lines.OrderBy(line => line.Time).Select(line => FormatLine(line.Line)).OfType<string>().ToList();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OperateLogReader] 日志读取失败: {ex.Message}");
            return new();
        }
    }

    /// <summary>解析有界日志行的时间戳，不受服务器当前区域设置影响；非空无时间戳行保持原有排序兼容性。</summary>
    private static void AddLogLine(List<(DateTime Time, string Line)> lines, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var time = line.Length >= 19 && DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed : DateTime.MinValue;
        lines.Add((time, line));
    }

    #endregion

    #region 清理
    /// <summary>清空指定用户的操作日志目录（先 Reset 释放 LogHelper 文件句柄，否则删除被锁文件失败）</summary>
    public static async Task ClearUserAsync(string date, string user)
    {
        await Snet.Log.LogHelper.ResetAsync();
        try
        {
            if (!TryResolveDirectory(out var dir, date, "operate", user)) return;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[OperateLogReader] 清理用户日志失败: {ex.Message}"); }
    }

    /// <summary>清空指定日期全部用户的操作日志目录</summary>
    public static async Task ClearAllAsync(string date)
    {
        await Snet.Log.LogHelper.ResetAsync();
        try
        {
            if (!TryResolveDirectory(out var dir, date, "operate")) return;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[OperateLogReader] 清理日期日志失败: {ex.Message}"); }
    }

    #endregion

    #region 行格式化
    private static string? FormatLine(string raw)
    {
        // 把完整时间戳压缩为 HH:mm:ss，跳过毫秒，保留级别与内容（按第一个 | 定位）
        if (raw.Length >= 23)
        {
            var time = raw[11..19];
            var pipe = raw.IndexOf('|');
            var rest = pipe >= 0 ? raw[(pipe + 1)..].TrimStart(' ', '|') : raw[23..].TrimStart();
            return $"{time} | {rest}";
        }
        return raw;
    }
    #endregion
}
