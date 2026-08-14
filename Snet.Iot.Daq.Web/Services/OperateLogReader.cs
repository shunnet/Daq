namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 操作日志读取：遍历 logs/{日期}/operate/{用户名}/ 目录，供用户管理页按日期/用户筛选查看。
/// </summary>
public static class OperateLogReader
{
    private static string LogsRoot => Path.Combine(WebPaths.DataDir, "logs");

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
            var operateDir = Path.Combine(LogsRoot, date, "operate");
            if (!Directory.Exists(operateDir)) return new();
            return Directory.GetDirectories(operateDir).Select(Path.GetFileName).OrderByDescending(u => u).ToList()!;
        }
        catch { return new(); }
    }

    /// <summary>指定日期+用户的操作日志行（合并当天全部 .log 文件，按时间序），行格式 "HH:mm:ss | 级别 | 内容"</summary>
    #endregion

    #region 日志行读取
    public static List<string> GetLines(string date, string user)
    {
        try
        {
            var dir = Path.Combine(LogsRoot, date, "operate", user);
            if (!Directory.Exists(dir)) return new();
            var lines = new List<(DateTime Time, string Line)>();
            foreach (var file in Directory.GetFiles(dir, "*.log").OrderBy(f => f))
            {
                // LogHelper 写入后保持文件句柄（FileShare.None），读取必须共享读写访问，否则刚写入的文件读不了
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string? raw;
                while ((raw = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    // LogHelper 行格式：yyyy-MM-dd HH:mm:ss.fff | LVL | 内容
                    var time = raw.Length >= 23 && DateTime.TryParse(raw[..19], out var t) ? t : DateTime.MinValue;
                    lines.Add((time, raw));
                }
            }
            return lines.OrderBy(l => l.Time)
                .Select(l => FormatLine(l.Line))
                .Where(l => l is not null)
                .ToList()!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OperateLogReader] GetLines({date},{user}) 异常: {ex}");
            return new();
        }
    }

    /// <summary>清空指定用户的操作日志目录（先 Reset 释放 LogHelper 文件句柄，否则删除被锁文件失败）</summary>
    #endregion

    #region 清理
    public static async Task ClearUserAsync(string date, string user)
    {
        await Snet.Log.LogHelper.ResetAsync();
        try
        {
            var dir = Path.Combine(LogsRoot, date, "operate", user);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { /* 清理失败不阻断 */ }
    }

    /// <summary>清空指定日期全部用户的操作日志目录</summary>
    public static async Task ClearAllAsync(string date)
    {
        await Snet.Log.LogHelper.ResetAsync();
        try
        {
            var dir = Path.Combine(LogsRoot, date, "operate");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { /* 清理失败不阻断 */ }
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
