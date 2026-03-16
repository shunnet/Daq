using System.Text;

namespace Snet.Iot.Daq.handler
{
    public static class GlobalHandler
    {
        /// <summary>
        /// 按 UTF-8 字节长度截断字符串，超出部分用 "..." 替换
        /// </summary>
        /// <param name="text">原字符串</param>
        /// <param name="maxBytes">最大字节长度</param>
        /// <returns></returns>
        public static string TruncateByBytes(this string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0)
                return string.Empty;

            var encoding = Encoding.UTF8;

            // 原字符串字节数
            int totalBytes = encoding.GetByteCount(text);
            if (totalBytes <= maxBytes)
                return text;

            // 省略号字节数
            const string ellipsis = "...";
            int ellipsisBytes = encoding.GetByteCount(ellipsis);

            // 连 "..." 都放不下
            if (ellipsisBytes >= maxBytes)
                return ellipsis.Substring(0, maxBytes);

            int allowedBytes = maxBytes - ellipsisBytes;

            var sb = new StringBuilder();
            int currentBytes = 0;
            Span<char> singleChar = stackalloc char[1];

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
    }
}
