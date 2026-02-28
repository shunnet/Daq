using Snet.Core.handler;
using Snet.Model.@enum;
using Snet.Utility;
using Snet.Windows.Controls.data;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 字节绑定通知模型
    /// </summary>
    public class BytesBindNotifyModel : BindNotify
    {
        /// <summary>
        /// 设置源
        /// </summary>
        private void SetSource()
        {
            foreach (var item in typeof(Snet.Model.@enum.DataType).EnumToList())
            {
                DataTypeComboBoxItemsSource.Add(new ComboBoxModel(item.Name, (Snet.Model.@enum.DataType)item.Value));
            }
            DataTypeComboBoxSelectedItem = DataTypeComboBoxItemsSource[1];
            foreach (var item in typeof(Snet.Model.@enum.EncodingType).EnumToList())
            {
                EncodingTypeComboBoxItemsSource.Add(new ComboBoxModel(item.Name, (Snet.Model.@enum.DataType)item.Value));
            }
            EncodingTypeComboBoxSelectedItem = EncodingTypeComboBoxItemsSource[7];
            foreach (var item in typeof(DataFormat).EnumToList())
            {
                DataFormatComboBoxItemsSource.Add(new ComboBoxModel(item.Name, (Snet.Model.@enum.DataType)item.Value));
            }
            DataFormatComboBoxSelectedItem = DataFormatComboBoxItemsSource[3];
        }

        /// <summary>
        /// 无参构造函数
        /// </summary>
        public BytesBindNotifyModel()
        {
            SetSource();
        }
        /// <summary>
        /// 全参构造函数
        /// </summary>
        /// <param name="address">地址名称</param>
        /// <param name="describe">描述</param>
        /// <param name="startBit">起始位</param>
        /// <param name="length">长度</param>
        /// <param name="dataType">数据类型</param>
        /// <param name="encodingType">编码类型</param>
        /// <param name="dataFormat">数据格式</param>
        public BytesBindNotifyModel(string address, string describe, int startBit, ushort length, DataType dataType, EncodingType encodingType, DataFormat dataFormat)
        {
            Address = address;
            Describe = describe;
            StartBit = startBit;
            Length = length;
            DataType = dataType;
            EncodingType = encodingType;
            DataFormat = dataFormat;
            SetSource();
        }

        /// <summary>
        /// 全参构造函数
        /// </summary>
        /// <param name="address">地址名称</param>
        /// <param name="describe">描述</param>
        /// <param name="startBit">起始位</param>
        /// <param name="length">长度</param>
        /// <param name="dataType">数据类型</param>
        /// <param name="dataFormat">数据格式</param>
        public BytesBindNotifyModel(string address, string describe, int startBit, ushort length, DataType dataType, DataFormat dataFormat)
        {
            Address = address;
            Describe = describe;
            StartBit = startBit;
            Length = length;
            DataType = dataType;
            DataFormat = dataFormat;
            SetSource();
        }

        /// <summary>
        /// 全参构造函数
        /// </summary>
        /// <param name="address">地址名称</param>
        /// <param name="describe">描述</param>
        /// <param name="startBit">起始位</param>
        /// <param name="length">长度</param>
        /// <param name="dataType">数据类型</param>
        public BytesBindNotifyModel(string address, string describe, int startBit, ushort length, DataType dataType)
        {
            Address = address;
            Describe = describe;
            StartBit = startBit;
            Length = length;
            DataType = dataType;
            SetSource();
        }

        /// <summary>
        /// 地址名称
        /// </summary>
        [Description("地址名称")]
        public string Address
        {
            get => address;
            set => SetProperty(ref address, value);
        }
        private string address = "请输入地址名称".GetLanguageValue(App.LanguageOperate);

        /// <summary>
        /// 描述
        /// </summary>
        [Description("描述")]
        public string Describe
        {
            get => describe;
            set => SetProperty(ref describe, value);
        }
        private string describe = "请输入描述".GetLanguageValue(App.LanguageOperate);

        /// <summary>
        /// 起始位
        /// </summary>
        [Description("起始位")]
        public int StartBit
        {
            get => GetProperty(() => StartBit);
            set => SetProperty(() => StartBit, value);
        }

        /// <summary>
        /// 长度
        /// </summary>
        [Description("长度")]
        public ushort Length
        {
            get => length;
            set => SetProperty(ref length, value);
        }
        private ushort length = 1;

        /// <summary>
        /// 布尔位索引
        /// </summary>
        [Description("布尔位索引")]
        public int BoolIndex
        {
            get => boolIndex;
            set => SetProperty(ref boolIndex, value);
        }
        private int boolIndex = 0;

        /// <summary>
        /// 数据类型
        /// </summary>
        [Description("数据类型")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
        public DataType DataType
        {
            get => dataType;
            set => SetProperty(ref dataType, value);
        }
        private DataType dataType = DataType.Bool;

        /// <summary>
        /// 下拉框数据源
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<ComboBoxModel> DataTypeComboBoxItemsSource
        {
            get => _DataTypeComboBoxItemsSource;
            set => SetProperty(ref _DataTypeComboBoxItemsSource, value);
        }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private ObservableCollection<ComboBoxModel> _DataTypeComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// 下拉框数选中的数据
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ComboBoxModel DataTypeComboBoxSelectedItem
        {
            get => GetProperty(() => DataTypeComboBoxSelectedItem);
            set
            {
                SetProperty(() => DataTypeComboBoxSelectedItem, value);
                DataType = (DataType)value.Value;
            }
        }












        /// <summary>
        /// 编码类型
        /// </summary>
        [Description("编码类型")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
        public EncodingType EncodingType
        {
            get => encodingType;
            set => SetProperty(ref encodingType, value);
        }
        private EncodingType encodingType = EncodingType.UTF8;

        /// <summary>
        /// 下拉框数据源
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<ComboBoxModel> EncodingTypeComboBoxItemsSource
        {
            get => _EncodingTypeComboBoxItemsSource;
            set => SetProperty(ref _EncodingTypeComboBoxItemsSource, value);
        }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private ObservableCollection<ComboBoxModel> _EncodingTypeComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// 下拉框数选中的数据
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ComboBoxModel EncodingTypeComboBoxSelectedItem
        {
            get => GetProperty(() => EncodingTypeComboBoxSelectedItem);
            set
            {
                SetProperty(() => EncodingTypeComboBoxSelectedItem, value);
                EncodingType = (EncodingType)value.Value;
            }
        }







        /// <summary>
        /// 数据格式
        /// </summary>
        [Description("数据格式")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Serialization.JsonStringContract))]
        public DataFormat DataFormat
        {
            get => dataFormat;
            set => SetProperty(ref dataFormat, value);
        }
        private DataFormat dataFormat = DataFormat.DCBA;

        /// <summary>
        /// 下拉框数据源
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ObservableCollection<ComboBoxModel> DataFormatComboBoxItemsSource
        {
            get => _DataFormatComboBoxItemsSource;
            set => SetProperty(ref _DataFormatComboBoxItemsSource, value);
        }
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private ObservableCollection<ComboBoxModel> _DataFormatComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// 下拉框数选中的数据
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ComboBoxModel DataFormatComboBoxSelectedItem
        {
            get => GetProperty(() => DataFormatComboBoxSelectedItem);
            set
            {
                SetProperty(() => DataFormatComboBoxSelectedItem, value);
                DataFormat = (DataFormat)value.Value;
            }
        }
    }
}
