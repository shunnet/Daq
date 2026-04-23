namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 批量插入结果统计模型
    /// <para>
    /// 用于记录一次批量插入（通常在事务中）的执行结果，
    /// 常见于：
    /// <list type="bullet">
    /// <item>Excel / CSV 导入数据库</item>
    /// <item>批量同步数据</item>
    /// <item>防重复插入操作</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <remarks>
    /// 该模型只负责结果统计，不参与任何业务逻辑判断，
    /// 便于在 UI 层、日志系统或调用方统一展示和处理。
    /// </remarks>
    public sealed class BatchInsertResult
    {
        /// <summary>
        /// 成功插入的数据条数
        /// <para>
        /// 表示实际执行 <c>INSERT</c> 并成功写入数据库的记录数量。
        /// </para>
        /// <para>
        /// 注意：
        /// <list type="bullet">
        /// <item>只统计数据库返回成功的插入</item>
        /// <item>不会包含被判定为重复而跳过的记录</item>
        /// </list>
        /// </para>
        /// </summary>
        public int Success { get; set; }

        /// <summary>
        /// 被判定为重复而未插入的数据条数
        /// <para>
        /// 通常是通过以下方式判定为重复：
        /// <list type="bullet">
        /// <item>与数据库中已有数据重复</item>
        /// <item>与同一批次导入的数据发生重复</item>
        /// </list>
        /// </para>
        /// <para>
        /// 被计入该数量的记录不会触发数据库插入操作。
        /// </para>
        /// </summary>
        public int Duplicate { get; set; }

        /// <summary>
        /// 插入失败的数据条数
        /// <para>
        /// 表示已经尝试执行插入操作，但由于异常或返回结果失败而未能写入数据库的记录数量。
        /// </para>
        /// <para>
        /// 常见原因包括：
        /// <list type="bullet">
        /// <item>数据库约束冲突（唯一索引、外键等）</item>
        /// <item>字段数据格式错误</item>
        /// <item>SQLite 异常或事务回滚</item>
        /// </list>
        /// </para>
        /// </summary>
        public int Failed { get; set; }
    }
}
