using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Snet.Iot.Daq.Core.mvvm
{
    /// <summary>
    /// 绑定通知对象基类，基于 ObservableObject。<br/>
    /// 提供基于表达式的动态属性存储机制，无需手动定义后备字段。<br/>
    /// 内部使用字典属性包存储属性值，通过 lock 保证读写线程安全。
    /// </summary>
    public class BindNotify : ObservableObject
    {
        #region 字典属性包

        /// <summary>
        /// 属性值存储字典（延迟初始化）
        /// </summary>
        private Dictionary<string, object>? _propertyBag;

        /// <summary>
        /// 用于保护属性包读写操作的同步锁对象，避免在 lock 中使用 PropertyBag 自身（防止外部引用干扰）
        /// </summary>
        private readonly object _propertyBagLock = new();

        /// <summary>
        /// 延迟初始化的属性包，用于动态存储所有属性值。<br/>
        /// 首次访问时自动创建字典实例。
        /// </summary>
        private Dictionary<string, object> PropertyBag => _propertyBag ??= new Dictionary<string, object>();

        #endregion

        #region 属性访问核心方法

        /// <summary>
        /// 获取指定属性名称的值。<br/>
        /// 若属性尚未设置，则返回类型 T 的默认值。<br/>
        /// 通过 lock 保护字典读取操作，确保与 SetPropertyCore 的写入操作线程安全。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="propertyName">属性名称</param>
        /// <returns>属性值，若不存在则返回 default(T)</returns>
        private T GetPropertyCore<T>(string propertyName)
        {
            lock (_propertyBagLock)
            {
                if (PropertyBag.TryGetValue(propertyName, out object? val))
                {
                    return (T)val;
                }
                return default!;
            }
        }

        /// <summary>
        /// 设置属性值，并在值发生变化时触发属性变更通知。<br/>
        /// 通过 lock 保护字典读写操作，确保线程安全。<br/>
        /// 使用 EqualityComparer 比较新旧值，避免不必要的通知。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="propertyName">属性名称</param>
        /// <param name="value">新值</param>
        /// <param name="oldValue">输出参数，返回设置前的旧值</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        protected virtual bool SetPropertyCore<T>(string propertyName, T value, out T oldValue)
        {
            VerifyAccess();

            lock (_propertyBagLock)
            {
                oldValue = default!;
                if (PropertyBag.TryGetValue(propertyName, out object? val))
                {
                    oldValue = (T)val;
                }

                if (EqualityComparer<T>.Default.Equals(oldValue, value))
                    return false;

                PropertyBag[propertyName] = value!;
            }

            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 设置属性值，值变化后执行无参回调。<br/>
        /// 内部委托给 SetPropertyCore 完成值设置和通知，额外触发回调。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="propertyName">属性名称</param>
        /// <param name="value">新值</param>
        /// <param name="changedCallback">值变化后执行的回调（可为 null）</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        private bool SetPropertyCore<T>(string propertyName, T value, Action? changedCallback)
        {
            var result = SetPropertyCore(propertyName, value, out _);
            if (result)
                changedCallback?.Invoke();
            return result;
        }

        /// <summary>
        /// 设置属性值，值变化后执行带旧值参数的回调。<br/>
        /// 内部委托给 SetPropertyCore 完成值设置和通知，额外传递旧值触发回调。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="propertyName">属性名称</param>
        /// <param name="value">新值</param>
        /// <param name="changedCallback">值变化后执行的回调，参数为变更前的旧值（可为 null）</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        private bool SetPropertyCore<T>(string propertyName, T value, Action<T>? changedCallback)
        {
            var result = SetPropertyCore(propertyName, value, out T oldValue);
            if (result)
                changedCallback?.Invoke(oldValue);
            return result;
        }

        /// <summary>
        /// 可重写的访问验证逻辑（默认无操作）。<br/>
        /// 子类可在此实现线程亲和性检查（如 WPF Dispatcher 检查）。
        /// </summary>
        protected virtual void VerifyAccess() { }

        #endregion

        #region 表达式支持

        /// <summary>
        /// 获取 Lambda 表达式中引用的属性名称。<br/>
        /// 用于替代硬编码的属性名字符串，提供编译期安全性。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="expression">指向属性的 Lambda 表达式（如 () => PropertyName）</param>
        /// <returns>属性名称字符串</returns>
        private static string GetPropertyName<T>(Expression<Func<T>> expression)
        {
            return GetPropertyNameFast(expression);
        }

        /// <summary>
        /// 从 Lambda 表达式中快速提取属性名。<br/>
        /// 支持 VB.NET 编译器生成的 $VB$Local_ 前缀自动剥离。
        /// </summary>
        /// <param name="expression">Lambda 表达式</param>
        /// <returns>属性名称字符串</returns>
        /// <exception cref="ArgumentException">当表达式体不是成员访问表达式时抛出</exception>
        private static string GetPropertyNameFast(LambdaExpression expression)
        {
            if (expression.Body is not MemberExpression memberExpression)
            {
                throw new ArgumentException("表达式体应为成员访问表达式", nameof(expression));
            }

            MemberInfo member = memberExpression.Member;
            const string VbLocalPrefix = "$VB$Local_";

            // 修正 VB.NET 编译器局部变量前缀
            if (member.MemberType == MemberTypes.Field &&
                member.Name != null &&
                member.Name.StartsWith(VbLocalPrefix))
            {
                return member.Name[VbLocalPrefix.Length..];
            }

            return member.Name;
        }

        #endregion

        #region 封装的属性访问方法（推荐调用方式）

        /// <summary>
        /// 获取属性值（通过表达式指定属性）。<br/>
        /// 从内部属性包中读取指定属性的当前值。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="expression">指向属性的 Lambda 表达式</param>
        /// <returns>属性当前值</returns>
        protected T GetProperty<T>(Expression<Func<T>> expression)
        {
            return GetPropertyCore<T>(GetPropertyName(expression));
        }

        /// <summary>
        /// 设置属性值，仅触发属性更改通知（无回调）。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="expression">指向属性的 Lambda 表达式</param>
        /// <param name="value">新值</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        protected bool SetProperty<T>(Expression<Func<T>> expression, T value)
        {
            return SetProperty(expression, value, changedCallback: (Action?)null);
        }

        /// <summary>
        /// 设置属性值，值变化后执行带旧值参数的回调。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="expression">指向属性的 Lambda 表达式</param>
        /// <param name="value">新值</param>
        /// <param name="changedCallback">值变化后的回调，参数为旧值</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        protected bool SetProperty<T>(Expression<Func<T>> expression, T value, Action<T> changedCallback)
        {
            return SetPropertyCore(GetPropertyName(expression), value, changedCallback);
        }

        /// <summary>
        /// 设置属性值，值变化后执行无参回调。
        /// </summary>
        /// <typeparam name="T">属性值类型</typeparam>
        /// <param name="expression">指向属性的 Lambda 表达式</param>
        /// <param name="value">新值</param>
        /// <param name="changedCallback">值变化后的回调</param>
        /// <returns>如果值发生了变化返回 true，否则返回 false</returns>
        protected bool SetProperty<T>(Expression<Func<T>> expression, T value, Action? changedCallback)
        {
            return SetPropertyCore(GetPropertyName(expression), value, changedCallback);
        }
        #endregion
    }
}
