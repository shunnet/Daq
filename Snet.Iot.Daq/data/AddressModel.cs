using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.handler;
using Snet.Utility;
using Snet.Windows.Controls.message;
using Snet.Windows.Controls.property.core.DataAnnotations;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 地址的模型
    /// </summary>
    public class AddressModel : AddressModelCore
    {
        /// <summary>
        /// 扩展参数
        /// </summary>
        /// [InputFilePath(".json", "json files (*.json)|*.json")]  选择文件路径的输入框
        /// [Height(200, 80, double.NaN)]
        [Description("扩展参数")]
        [Height(200, 80, double.NaN)]
        public override string ExpandParam
        {
            get => GetProperty(() => ExpandParam);
            set => SetProperty(() => ExpandParam, value);
        }

        /// <summary>在独立副本中编辑；校验并提交成功后才更新全局地址，失败或取消保持原记录。</summary>
        /// <returns>完成属性对话框、数据库提交及必要的错误提示的任务。</returns>
        public override async Task UpdateAsync()
        {
            var editable = new AddressModel { Index = Index, Guid = Guid };
            editable.CopyValues(this);
            GlobalConfigModel.param.SetBasics(editable);
            if (!(await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool()) return;
            var candidate = GlobalConfigModel.param.GetBasics().GetSource<AddressModel>();
            if (candidate is null) return;
            candidate.Index = Index;
            candidate.Guid = Guid;
            candidate.Time = DateTime.Now;
            try
            {
                var result = Snet.Iot.Daq.Core.handler.AddressStore.Update(GlobalConfigModel.sqliteOperate, GlobalConfigModel.DbLock, candidate);
                if (result != Snet.Iot.Daq.Core.handler.AddressStore.UpdateResult.Updated)
                {
                    var message = result switch
                    {
                        Snet.Iot.Daq.Core.handler.AddressStore.UpdateResult.Duplicate => "添加失败，地址或别名重复！",
                        Snet.Iot.Daq.Core.handler.AddressStore.UpdateResult.Invalid => "地址、别名和长度必须有效",
                        _ => "修改失败，地址已不存在"
                    };
                    await MessageBox.Show(message.GetLanguageValue(App.LanguageOperate) ?? message, "异常".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    return;
                }
                CopyValues(candidate);
                this.SetAddress();
            }
            catch (Exception ex)
            {
                await MessageBox.Show(ex.Message, "异常".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        /// <summary>重新读取同一身份的数据库记录，恢复全部可编辑字段；过期序号不会加载其他地址。</summary>
        /// <param name="index">当前地址的数据库序号。</param>
        public override void Revoke(int index)
        {
            AddressModel? model;
            lock (GlobalConfigModel.DbLock)
                model = GlobalConfigModel.sqliteOperate.Find<AddressModel>(index);
            if (model is not null && model.Guid == Guid) CopyValues(model);
        }

        /// <summary>从已提交的记录恢复可编辑值，保留当前对象的事件订阅和数据库身份。</summary>
        /// <param name="model">已成功保存或从数据库读取的地址。</param>
        private void CopyValues(IAddressModel model)
        {
            Address = model.Address;
            AnotherName = model.AnotherName;
            Describe = model.Describe;
            Time = model.Time;
            Length = model.Length;
            Type = model.Type;
            EncodingType = model.EncodingType;
            ExpandParam = model.ExpandParam;
            Topic = model.Topic;
            SimplifyValue = model.SimplifyValue;
        }
    }
}
