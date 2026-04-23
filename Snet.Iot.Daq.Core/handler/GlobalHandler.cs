using System.Text;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 全局工具处理类<br/>
    /// 提供字符串截断等常用工具方法
    /// </summary>
    public static class GlobalHandler
    {
        /// <summary>
        /// 按 UTF-8 字节长度截断字符串，超出部分用 "..." 替换<br/>
        /// 逐字符计算字节数，确保不会在多字节字符中间截断<br/>
        /// 性能优化：使用 stackalloc 避免临时数组的堆分配，预分配 StringBuilder 容量
        /// </summary>
        /// <param name="text">原始字符串</param>
        /// <param name="maxBytes">允许的最大 UTF-8 字节长度（包含省略号）</param>
        /// <returns>截断后的字符串，未超限时返回原字符串</returns>
        public static string TruncateByBytes(this string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0)
                return string.Empty;

            var encoding = Encoding.UTF8;

            // 计算原字符串的总字节数
            int totalBytes = encoding.GetByteCount(text);
            if (totalBytes <= maxBytes)
                return text;

            // 省略号占用的字节数
            const string ellipsis = "...";
            int ellipsisBytes = encoding.GetByteCount(ellipsis);

            // 连省略号都放不下的极端情况
            if (ellipsisBytes >= maxBytes)
                return ellipsis.Substring(0, maxBytes);

            // 计算正文可用的字节预算
            int allowedBytes = maxBytes - ellipsisBytes;

            // 预分配 StringBuilder 容量，减少扩容次数
            var sb = new StringBuilder(Math.Min(text.Length, allowedBytes) + ellipsis.Length);
            int currentBytes = 0;
            Span<char> singleChar = stackalloc char[1];

            // 逐字符累计字节数，确保在字符边界处截断
            foreach (char c in text)
            {
                singleChar[0] = c;
                int charBytes = encoding.GetByteCount(singleChar);

                if (currentBytes + charBytes > allowedBytes)
                    break;

                sb.Append(c);
                currentBytes += charBytes;
            }

            sb.Append(ellipsis);
            return sb.ToString();
        }

        /// <summary>
        /// 解析首选类型
        /// </summary>
        /// <param name="typeNames">类型名称</param>
        /// <returns>指定类型</returns>
        public static Type? ResolvePreferredType(params string[] typeNames)
        {
            foreach (var name in typeNames)
            {
                var t = Type.GetType(name, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
