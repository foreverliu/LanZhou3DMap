using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;


public class ExamMonitorReceiver : MonoBehaviour
{
    /*
    typedef struct _EXAM_MONITOR_DATA {
    quint8 nCarID;           // 车辆编号
    quint8 nTrainMode;       // 训练模式 1,考试模式0
    char szName[16];         // 考生姓名
    char szID[16];           // 考生编号
    char szIDSerial[20];     // 身份证号
    char szKGName[16];       // 考官姓名
    quint8 nCarState;        // 车辆状态
    quint8 bCurTime;         // 项目计时
    quint8 nKscs;            // 考试次数
    short nScore;            // 考试成绩
    quint32 dwCurExamItem;   // 项目在考
    quint32 dwExamItemState; // 项目状态
    quint32 dwExamItemError; // 项目出错
    double startTime;        // 开始时间
    double curTime;          // 当前时间
    quint8 aErrors[32];      // 错误代码顺序
    quint8 aItemExamed[32];  // 考试完成项目顺序
    quint8 nExamLine;        // 考试线路
    quint32 dwCarBaseState;  // 车辆信号
    int nMeter;              // 车辆里程
    short nSpeed;            // 车辆速度
    short nSceneID;          // 场地项目编号
    int nEngineSpeed;        // 发动机转速
    int nKSKM;               // 考试项目
    char nMoveDir;           // 运动状态
    short nGear;             // 档位
    short nAngle;            // 车辆航向
    float x;                 // 位置坐标
    float y;                 // 位置坐标
    double gps_j;            // GPS经纬度坐标
    double gps_w;            // GPS经纬度坐标
    } EXAM_MONITOR_DATA;

    模拟数据：
    85 00 00 00 01 02 B0 A2 CE B0 00 00 0F 00 00 00 90 04 00 00 C0 00 8D 01 F0 FD 
    60 00 30 0C 18 77 93 1F 71 43 FE FF 33 34 30 31 31 39 39 31 00 77 00 00 00 00 
    5E 3B 19 77 68 E3 C0 EE CB C4 00 00 68 E3 87 00 35 00 00 00 40 00 04 00 CC 02 
    8D 01 77 26 19 77 38 7C 8D 01 00 00 8D 01 35 00 00 00 08 00 00 00 40 00 00 00 
    68 E3 87 00 01 00 00 00 00 00 86 00 00 00 00 00 00 FE 60 00 FA D0 13 77 00 00 
    00 00 8B 0F 30 34 38 7C 8D 01 28 CB 8D 01 68 E3 87 00 D8 FD 60 00 33 00 00 00 
    38 FE 60 00 30 0C 18 77 C3 1A 71 43 FE FF FF FF 48 FE 60 00 F7 1E 78 75 00 00 
    86 00 00 00 01 00 68 E3 87 00 02 00 00 00 38 7C 8D 01 02 01 8D 01 01 00 00 00 
    00 00 00 00 47 8F 00 EA 1D 13 5A 40 9A 21 1B 69 25 00 3F 40
     * */
    const string LogFile = "ExamMonitor.txt";

    // ---- 协议常量 ----
    const int CmdExamMonitor = 133;     // 包头命令字：考试监控数据
    const int CmdEcho = 300;            // 包头命令字：回发包(忽略)
    const int HeaderSize = 4;           // 包头：int命令字
    const int StructSize = 224;         // EXAM_MONITOR_DATA 按默认对齐的大小
    const int BlockSize = HeaderSize + StructSize;
    const int OffCarId = 0;             // nCarID  u8
    const int OffName = 2;              // szName  char[16] (GBK)
    const int NameLen = 16;

    const string NamePrefix = "学员姓名：";
    const string NameObjectName = "学员姓名";
    const int MaxQueue = 200;           // 接收队列上限，防止发送过快时无限堆积
    const int MaxPacketsPerFrame = 100;

    static ExamMonitorReceiver _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("ExamMonitorReceiver");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ExamMonitorReceiver>();
    }

    // ---- 配置（AppSetting.txt 的 [ExamMonitor] 段）----
    bool _enable = true;
    int _port = 45678;
    string _bindIp = "127.0.0.1";

    // ---- UDP ----
    UdpClient _udp;
    Thread _thread;
    volatile bool _run;
    readonly Queue<byte[]> _queue = new Queue<byte[]>();

    // ---- 数据 ----
    readonly Dictionary<int, string> _names = new Dictionary<int, string>();     // 车辆编号 -> 学员姓名
    readonly Dictionary<string, int> _ipCarNo = new Dictionary<string, int>();   // 车辆IP -> 车辆编号
    bool _gbkSourceLogged;
    float _lastBadPacketLog = -100f;

    // ---- UI ----
    GameObject _boundPanel;
    Text _nameText;
    string _shown;

    void Start()
    {
        LoadConfig();
        LoadCarNumbers();
        if (!_enable)
        {
            Log("考试监控数据接收已禁用(Enable=false)");
            return;
        }
        StartUdp();
    }

    void Update()
    {
        DrainQueue();
        RefreshNameText();
    }

    void OnDestroy()
    {
        StopUdp();
    }

    void OnApplicationQuit()
    {
        StopUdp();
    }

    // ===================== 配置 =====================

    /// <summary>
    /// 读取StreamingAssets/AppSetting.txt 的 [ExamMonitor] 段。文件、段或某项缺失/无效时使用默认值。
    /// </summary>
    void LoadConfig()
    {
        string path;
        Dictionary<string, string> cfg = AppSettingFile.LoadSection("ExamMonitor", out path);
        if (cfg == null)
        {
            Log(AppSettingFile.FileName + " 不存在，使用默认配置。路径：" + path);
        }
        else
        {
            string val;
            bool enable;
            if (cfg.TryGetValue("enable", out val) && bool.TryParse(val, out enable)) _enable = enable;

            int port;
            if (cfg.TryGetValue("port", out val) && int.TryParse(val, out port) && port > 0 && port <= 65535) _port = port;

            if (cfg.TryGetValue("bindip", out val) && val.Length > 0) _bindIp = val;
        }

        Log($"配置：Enable={_enable} Port={_port} BindIp={_bindIp}");
    }

    /// <summary>
    /// 从CarSetting.txt读取 IP -> 车辆编号
    /// </summary>
    void LoadCarNumbers()
    {
        _ipCarNo.Clear();

        string path;
        List<CarSettingEntry> entries = CarSettingFile.Load(out path);
        if (entries == null)
        {
            Log("CarSetting.txt 不存在，无法读取车辆编号。路径：" + path);
            return;
        }

        foreach (CarSettingEntry entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.ip) && entry.carNo >= 0)
            {
                _ipCarNo[entry.ip] = entry.carNo;
            }
        }
        Log($"加载车辆编号 {_ipCarNo.Count} 条");
    }

    // ===================== UDP接收 =====================

    void StartUdp()
    {
        try
        {
            IPAddress addr;
            if (!IPAddress.TryParse(_bindIp, out addr))
            {
                Log("BindIp无效：" + _bindIp + "，改用127.0.0.1");
                addr = IPAddress.Loopback;
            }

            _udp = new UdpClient(new IPEndPoint(addr, _port));
            _run = true;
            _thread = new Thread(ReceiveLoop);
            _thread.IsBackground = true;
            _thread.Start();

            Log($"UDP监听启动，{addr}:{_port}");
        }
        catch (Exception e)
        {
            Log($"UDP监听启动失败（端口{_port}可能被占用）：{e.Message}");
            StopUdp();
        }
    }

    void StopUdp()
    {
        _run = false;
        if (_udp != null)
        {
            try { _udp.Close(); } catch (Exception) { }
            _udp = null;
        }
        if (_thread != null && _thread.IsAlive)
        {
            _thread.Join(300);
        }
        _thread = null;
    }

    void ReceiveLoop()
    {
        UdpClient udp = _udp;
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (_run)
        {
            try
            {
                byte[] data = udp.Receive(ref remote);
                if (data == null || data.Length == 0) continue;

                lock (_queue)
                {
                    if (_queue.Count >= MaxQueue) _queue.Dequeue();   // 丢弃最旧的
                    _queue.Enqueue(data);
                }
            }
            catch (SocketException)
            {
                if (!_run) break;      // 主动关闭时退出
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                if (!_run) break;
            }
        }
    }

    // ===================== 解析 =====================

    void DrainQueue()
    {
        for (int i = 0; i < MaxPacketsPerFrame; i++)
        {
            byte[] data;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                data = _queue.Dequeue();
            }
            ParsePacket(data);
        }
    }

    void ParsePacket(byte[] data)
    {
        if (data.Length < HeaderSize) return;

        int offset = 0;
        while (offset + BlockSize <= data.Length)
        {
            int cmd = BitConverter.ToInt32(data, offset);
            if (cmd != CmdExamMonitor)
            {
                if (cmd != CmdEcho) LogBadPacket($"未知命令字 {cmd}，长度 {data.Length}，丢弃");
                return;
            }

            int body = offset + HeaderSize;
            int carNo = data[body + OffCarId];
            string name = DecodeString(data, body + OffName, NameLen);
            //string szKGName = DecodeString(data, body + 54, NameLen);
            //Debug.Log($"收到考试监控数据：车辆编号 {carNo} 学员姓名：{name} 考官姓名：{szKGName}");
            string old;
            if (!_names.TryGetValue(carNo, out old) || old != name)
            {
                _names[carNo] = name;
                Log($"车辆编号 {carNo} 学员姓名：{name}");
            }

            offset += BlockSize;
        }

        if (offset == 0)
        {
            LogBadPacket($"数据长度不足 {data.Length} < {BlockSize}，丢弃");
        }
        else if (offset < data.Length)
        {
            LogBadPacket($"数据长度 {data.Length} 不是 {BlockSize} 的整数倍，末尾 {data.Length - offset} 字节被忽略");
        }
    }

    /// <summary>
    /// 读取定长字符数组：到第一个0为止，按GBK解码，去掉控制字符和首尾空白
    /// </summary>
    string DecodeString(byte[] data, int start, int maxLen)
    {
        int len = 0;
        while (len < maxLen && data[start + len] != 0) len++;
        if (len == 0) return "";

        string s = GbkDecoder.GetString(data, start, len);
        if (!_gbkSourceLogged)
        {
            _gbkSourceLogged = true;
            Log("GBK解码方式：" + GbkDecoder.Source);
        }
        StringBuilder sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    // ===================== UI显示 =====================

    void RefreshNameText()
    {
        CarListUIManager ui = CarListUIManager.instance;
        if (ui == null || ui.baseInfoObj == null) return;

        if (ui.baseInfoObj != _boundPanel)
        {
            BindNameText(ui.baseInfoObj);
        }
        if (_nameText == null) return;

        string diag;
        string want = NamePrefix + GetSelectedName(ui.currentClickIp, out diag);

        // 直接和Text当前内容比较（而不是和上次写入的值比较），即使别处改动了文本，也会被纠正回来
        if (_nameText.text != want)
        {
            _nameText.text = want;
        }

        // 查找过程有变化时写一条日志（选中车辆变化、收到新数据等才会写，不会刷屏）
        if (diag != _shown)
        {
            _shown = diag;
            Log("姓名显示：" + diag + " -> \"" + want + "\"");
        }
    }

    // 选中车辆IP -> 车辆编号(CarSetting.txt) -> 学员姓名；diag返回查找过程，便于排查
    string GetSelectedName(string ip, out string diag)
    {
        if (string.IsNullOrEmpty(ip))
        {
            diag = "未选中车辆";
            return "";
        }

        int carNo;
        if (!_ipCarNo.TryGetValue(ip, out carNo))
        {
            diag = $"ip={ip} 在CarSetting.txt里没有配置车辆编号";
            return "";
        }

        string name;
        if (_names.TryGetValue(carNo, out name))
        {
            diag = $"ip={ip} 车辆编号={carNo} 姓名={name}";
            return name;
        }

        diag = $"ip={ip} 车辆编号={carNo} 尚未收到该编号的数据，已收到的编号：[{string.Join(",", ReceivedCarNos())}]";
        return "";
    }

    List<string> ReceivedCarNos()
    {
        List<string> list = new List<string>();
        foreach (int no in _names.Keys) list.Add(no.ToString());
        return list;
    }

    void BindNameText(GameObject panel)
    {
        _boundPanel = panel;
        _nameText = null;
        _shown = null;

        foreach (Text t in panel.GetComponentsInChildren<Text>(true))
        {
            if (t.gameObject.name == NameObjectName)
            {
                _nameText = t;
                break;
            }
        }

        if (_nameText == null)
        {
            Log($"基本信息面板({panel.name})下找不到名为\"{NameObjectName}\"的Text，无法显示学员姓名");
        }
        else
        {
            Log($"已绑定姓名Text：{_nameText.transform.parent.name}/{_nameText.gameObject.name}");
        }
    }

    // ===================== 日志 =====================

    // 异常包的日志限频：最多每5秒一条，避免发送端持续发错包时刷屏
    void LogBadPacket(string msg)
    {
        if (Time.realtimeSinceStartup - _lastBadPacketLog < 5f) return;
        _lastBadPacketLog = Time.realtimeSinceStartup;
        Log(msg);
    }

    static void Log(string msg)
    {
        WriteLog.instance?.WriteStringToFileOld(msg, LogFile);
    }
}
