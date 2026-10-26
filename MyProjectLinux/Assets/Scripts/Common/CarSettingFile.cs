using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// CarSetting.txt 中的一条车辆配置
/// </summary>
public class CarSettingEntry
{
    public string ip;
    public string plate;
    public string videoUrl;   // 未配置为空字符串
    public int carNo = -1;    // 车辆编号，未配置为-1
}

/// <summary>
/// CarSetting.txt 读取工具（供CarVideo、ExamMonitor等独立模块共用，解析规则只写在这里）。
///
/// 每行格式：IP=车牌=车辆编号=视频流地址，#开头为注释。例如：
///   192.168.1.6=GHZN001=1=rtsp://admin:admin123@192.168.1.11/h264/ch1/main/av_stream
///
/// 视频流地址在最后，里面可能自带"="（如?token=xxx），所以最多拆成4段，第4段整体就是视频流地址：
///   1. 第3段是整数 -> 车辆编号，第4段(可省略)为视频流地址；
///   2. 第3段不是整数 -> 兼容旧格式 IP=车牌=视频流地址，第3段起整体当作视频流地址，车辆编号视为未配置。
/// </summary>
public static class CarSettingFile
{
    /// <summary>
    /// 读取全部配置。文件不存在返回null（path为查找过的路径）。
    /// </summary>
    public static List<CarSettingEntry> Load(out string path)
    {
        path = Path.Combine(Application.streamingAssetsPath, "CarSetting.txt");
        if (!File.Exists(path))
        {
            // Linux打包后兼容路径（和UDPControl保持一致）
            path = Path.Combine(Directory.GetCurrentDirectory(), "CarSetting.txt");
        }
        if (!File.Exists(path)) return null;

        List<CarSettingEntry> list = new List<CarSettingEntry>();
        foreach (string line in File.ReadAllLines(path))
        {
            CarSettingEntry entry = ParseLine(line);
            if (entry != null) list.Add(entry);
        }
        return list;
    }

    /// <summary>
    /// 解析一行。空行、注释行、格式不对的行返回null。
    /// </summary>
    public static CarSettingEntry ParseLine(string line)
    {
        if (line == null) return null;
        string s = line.Trim();
        if (s.Length == 0 || s.StartsWith("#")) return null;

        int i1 = s.IndexOf('=');
        if (i1 <= 0) return null;

        CarSettingEntry entry = new CarSettingEntry();
        entry.ip = s.Substring(0, i1).Trim();
        entry.videoUrl = "";

        int i2 = s.IndexOf('=', i1 + 1);
        if (i2 < 0)
        {
            entry.plate = s.Substring(i1 + 1).Trim();
            return entry;
        }

        entry.plate = s.Substring(i1 + 1, i2 - i1 - 1).Trim();

        // 第3段起：车辆编号[=视频流地址]
        string rest = s.Substring(i2 + 1).Trim();
        int i3 = rest.IndexOf('=');
        string third = i3 < 0 ? rest : rest.Substring(0, i3).Trim();

        int no;
        if (int.TryParse(third, out no) && no >= 0)
        {
            entry.carNo = no;
            entry.videoUrl = i3 < 0 ? "" : rest.Substring(i3 + 1).Trim();
        }
        else
        {
            // 旧格式：第3段起整体是视频流地址
            entry.videoUrl = rest;
        }
        return entry;
    }
}
