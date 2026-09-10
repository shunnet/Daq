using System.Globalization;
using Snet.Iot.Daq.Core;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 为当前 Blazor 用户会话提供本地化资源查询和语言状态通知。
/// </summary>
/// <remarks>
/// 每个作用域实例只保存当前会话的语言，查询时显式传入区域性，避免修改进程级区域性而影响其他用户。
/// </remarks>
public sealed class LocalizationService
{
    private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-Hans");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// 获取当前会话使用的语言代码，值为 <c>zh</c> 或 <c>en</c>。
    /// </summary>
    public string CurrentLanguage { get; private set; } = "zh";

    /// <summary>
    /// 在当前会话语言发生变化后触发，订阅方应在释放时取消订阅。
    /// </summary>
    public event Action? LanguageChanged;

    /// <summary>
    /// 从 <see cref="Language"/> 的统一资源文件读取当前语言的文本。
    /// </summary>
    /// <param name="key">资源键；项目约定中文显示文本可直接作为资源键。</param>
    /// <returns>当前语言的资源值；资源缺失时返回原始键，确保界面仍可读。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 <see langword="null"/>。</exception>
    public string T(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var culture = CurrentLanguage == "en" ? EnglishCulture : ChineseCulture;
        return Language.ResourceManager.GetString(key, culture) ?? key;
    }

    /// <summary>
    /// 切换当前用户会话的界面语言，并在值实际变化时通知订阅方。
    /// </summary>
    /// <param name="language">语言代码；仅 <c>en</c> 选择英文，其余值统一为中文。</param>
    public void SetLanguage(string language)
    {
        var normalized = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        if (normalized == CurrentLanguage)
            return;

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke();
    }
}
