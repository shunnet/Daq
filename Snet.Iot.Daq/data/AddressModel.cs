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

        public override async Task UpdateAsync()
        {
            IAddressModel? model = null;
            GlobalConfigModel.param.SetBasics(this);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                IAddressModel param = GlobalConfigModel.param.GetBasics().GetSource<IAddressModel>();
                if (param != null)
                {
                    model = param.Guid.GetAddress();
                    if (model != null)
                    {
                        model.Address = param.Address;
                        model.AnotherName = param.AnotherName;
                        model.Describe = param.Describe;
                        model.Time = param.Time = DateTime.Now;
                        model.Length = param.Length;
                        model.Type = param.Type;
                        model.SimplifyValue = param.SimplifyValue;
                        model.EncodingType = param.EncodingType;
                        model.ExpandParam = param.ExpandParam;
                        model.Topic = param.Topic;

                        try
                        {
                            model.SetAddress();
                            GlobalConfigModel.sqliteOperate.Update(model);
                        }
                        catch (Exception ex)
                        {
                            Revoke(this.Index);
                            await MessageBox.Show(ex.Message, "异常".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                        }
                    }
                }
            }
            else
            {
                Revoke(this.Index);
            }
        }

        /// <summary>
        /// 撤销修改
        /// </summary>
        public override void Revoke(int index)
        {
            AddressModel model = GlobalConfigModel.sqliteOperate.Table<AddressModel>().FirstOrDefault(x => x.Index == index);
            if (model != null)
            {
                this.Address = model.Address;
                this.AnotherName = model.AnotherName;
                this.Describe = model.Describe;
                this.Time = model.Time;
                this.Length = model.Length;
                this.Type = model.Type;
                this.EncodingType = model.EncodingType;
                this.IsSelected = model.IsSelected;
                this.ExpandParam = model.ExpandParam;
            }
        }
    }
}
