using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class AppSettingFile
{
    public const string FileName = "AppSetting.txt";

    /// <summary>
    /// 读取指定段的全部键值（key统一为小写）。配置文件不存在返回null（path为查找过的路径）；
    /// 文件存在但没有该段时返回空字典。
    /// </summary>
    public static Dictionary<string, string> LoadSection(string section, out string path)
    {
        path = Path.Combine(Application.streamingAssetsPath, FileName);
        if (!File.Exists(path)) return null;

        Dictionary<string, string> result = new Dictionary<string, string>();
        string target = section.Trim().ToLowerInvariant();
        string current = "";

        foreach (string line in File.ReadAllLines(path))
        {
            string s = line.Trim();
            if (s.Length == 0 || s.StartsWith("#")) continue;

            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                current = s.Substring(1, s.Length - 2).Trim().ToLowerInvariant();
                continue;
            }
            if (current != target) continue;

            int eq = s.IndexOf('=');
            if (eq <= 0) continue;

            string key = s.Substring(0, eq).Trim().ToLowerInvariant();
            string val = s.Substring(eq + 1).Trim();
            int hash = val.IndexOf('#');            // 允许行尾注释
            if (hash >= 0) val = val.Substring(0, hash).Trim();

            result[key] = val;
        }
        return result;
    }
}
