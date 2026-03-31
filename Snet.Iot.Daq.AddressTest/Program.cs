using Snet.Model.data;
using Snet.Utility;

var list_dbw = new List<BytesModel>();
var list_dbd_int_1 = new List<BytesModel>();
var list_dbd_int_2 = new List<BytesModel>();
var list_dbd_float = new List<BytesModel>();

// DBW 0 - 398
for (int i = 0; i <= 398; i += 2)
{
    list_dbw.Add(new BytesModel($"DB5.DBW{i}", "0-398数据", i, 2, Snet.Model.@enum.DataType.Int16, dataFormat: Snet.Model.@enum.DataFormat.ABCD));
}

int startBit = 0;
// DBD 400 - 796
for (int i = 400; i <= 796; i += 4)
{
    list_dbd_int_1.Add(new BytesModel($"DB5.DBD{i}", "400-796数据", startBit, 4, Snet.Model.@enum.DataType.Int, dataFormat: Snet.Model.@enum.DataFormat.ABCD));
    startBit += 4;
}

startBit = 0;
// DBD 800 - 1196
for (int i = 800; i <= 1196; i += 4)
{
    list_dbd_int_2.Add(new BytesModel($"DB5.DBD{i}", "800-1196数据", startBit, 4, Snet.Model.@enum.DataType.Int, dataFormat: Snet.Model.@enum.DataFormat.ABCD));
    startBit += 4;

}

startBit = 0;
// DBD 1200 - 1596
for (int i = 1200; i <= 1596; i += 4)
{
    list_dbd_float.Add(new BytesModel($"DB5.DBD{i}", "1200-1596数据", startBit, 4, Snet.Model.@enum.DataType.Float, dataFormat: Snet.Model.@enum.DataFormat.ABCD));
    startBit += 4;
}



File.WriteAllText("S7-short-0~398.json", list_dbw.ToJson(true));

File.WriteAllText("S7-int-400~796.json", list_dbd_int_1.ToJson(true));

File.WriteAllText("S7-int-800~1196.json", list_dbd_int_2.ToJson(true));

File.WriteAllText("S7-float-1200~1596.json", list_dbd_float.ToJson(true));
