using System;
using System.Text;
using UnityEngine;

/// <summary>
/// GBK(CP936)解码。
///
/// Linux上的Mono打包默认不带I18N.CJK程序集，Encoding.GetEncoding(936)会抛"data could not be found"。
/// 所以：优先用系统的 Encoding 936；不可用时退回项目内置的映射表 Resources/GbkTable.bytes
/// （由CP936生成：首字节0x81~0xFE、次字节0x40~0xFE，共126*191项，每项2字节小端Unicode，0表示无效），
/// 这样不依赖任何系统组件，在Windows编辑器和Linux打包后行为一致。
///
/// 只能在主线程调用（内置表通过Resources加载）。
/// </summary>
public static class GbkDecoder
{
    const int TrailCount = 0xFE - 0x40 + 1;                      // 191
    const int TableEntries = (0xFE - 0x81 + 1) * TrailCount;     // 24066

    static Encoding _encoding;
    static bool _encodingTried;
    static ushort[] _table;
    static bool _tableTried;

    /// <summary>
    /// 当前使用的解码方式，用于日志："Encoding936" / "内置映射表" / "不可用(按ASCII处理)"
    /// </summary>
    public static string Source { get; private set; }

    public static string GetString(byte[] data, int start, int length)
    {
        if (length <= 0) return "";

        if (!_encodingTried)
        {
            _encodingTried = true;
            try
            {
                _encoding = Encoding.GetEncoding(936);
                Source = "Encoding936";
            }
            catch (Exception)
            {
                _encoding = null;
            }
        }
        if (_encoding != null) return _encoding.GetString(data, start, length);

        if (!_tableTried)
        {
            _tableTried = true;
            LoadTable();
        }
        return DecodeByTable(data, start, length);
    }

    static void LoadTable()
    {
        TextAsset asset = Resources.Load<TextAsset>("GbkTable");
        if (asset == null || asset.bytes == null || asset.bytes.Length != TableEntries * 2)
        {
            Source = "不可用(按ASCII处理)";
            return;
        }

        byte[] bytes = asset.bytes;
        _table = new ushort[TableEntries];
        for (int i = 0; i < TableEntries; i++)
        {
            _table[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        }
        Source = "内置映射表";
    }

    static string DecodeByTable(byte[] data, int start, int length)
    {
        StringBuilder sb = new StringBuilder(length);
        int end = start + length;
        int i = start;
        while (i < end)
        {
            byte b = data[i];
            if (b < 0x80)
            {
                sb.Append((char)b);
                i++;
                continue;
            }

            // 双字节：首字节0x81~0xFE，次字节0x40~0xFE(不含0x7F)
            if (_table != null && b >= 0x81 && b <= 0xFE && i + 1 < end)
            {
                byte t = data[i + 1];
                if (t >= 0x40 && t <= 0xFE && t != 0x7F)
                {
                    ushort u = _table[(b - 0x81) * TrailCount + (t - 0x40)];
                    if (u != 0)
                    {
                        sb.Append((char)u);
                        i += 2;
                        continue;
                    }
                }
            }

            sb.Append('?');
            i++;
        }
        return sb.ToString();
    }
}
