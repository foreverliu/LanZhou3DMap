using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Policy;
using UnityEngine;
using UnityEngine.UI;


public class CarVideoPlayer : MonoBehaviour
{
    const string LogFile = "Video.txt";

    static CarVideoPlayer _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("CarVideoPlayer");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CarVideoPlayer>();
    }

    // ---- 播放参数（默认值，可被AppSetting.txt的[Video]段覆盖）----
    bool _rtspOverTcp = true;
    int _networkCaching = 500;
    bool _hardwareDecoding = false;
    UMP.LogLevels _logLevel = UMP.LogLevels.Error;

    // ---- 运行状态 ----
    readonly Dictionary<string, string> _urls = new Dictionary<string, string>();
    GameObject _boundVideoObj;               // 当前绑定的视频面板，面板对象变化(如切场景)时重新初始化
    UMP.UniversalMediaPlayer _player;
    RawImage _image;
    CarVideoFullScreen _fullScreen;          // 点击视频全屏
    bool _ready;
    string _currentIp;                       // 当前（已处理的）目标车辆，null表示不播放

    void Update()
    {
        CarListUIManager ui = CarListUIManager.instance;
        if (ui == null || ui.videoObj == null) return;

        if (ui.videoObj != _boundVideoObj)
        {
            Bind(ui);
        }
        if (!_ready) return;

        // 目标车辆：已选中 + 在线 + 视频面板已显示
        string ip = ui.currentClickIp;
        bool online = !string.IsNullOrEmpty(ip)
                      && UDPControl.instance != null
                      && UDPControl.instance.ipCarDict.ContainsKey(ip);
        bool panelOn = _player != null && _player.gameObject.activeInHierarchy;
        string want = (online && panelOn) ? ip : null;

        if (want == _currentIp) return;
        _currentIp = want;

        if (want == null)
        {
            StopPlayer();
        }
        else
        {
            PlayCar(want);
        }
    }

    // ===================== 初始化 =====================

    void Bind(CarListUIManager ui)
    {
        _boundVideoObj = ui.videoObj;
        if (_fullScreen != null) _fullScreen.Exit();
        _player = null;
        _image = null;
        _ready = false;
        _currentIp = null;

        LoadSetting();
        LoadVideoUrls();

        GameObject panel = ui.videoObj;
        _player = panel.GetComponentInChildren<UMP.UniversalMediaPlayer>(true);

        if (_player != null)
        {
            _image = _player.GetComponent<RawImage>();
        }
        if (_image == null)
        {
            _image = panel.GetComponentInChildren<RawImage>(true);
        }

        if (_player == null)
        {
            if (_image == null)
            {
                Log("videoObj下找不到RawImage，车载视频不可用");
                return;
            }

            // 先置为未激活再添加组件：UMP的Awake会等配置完成、重新激活时才执行
            GameObject go = _image.gameObject;
            bool wasActive = go.activeSelf;
            go.SetActive(false);

            _player = go.AddComponent<UMP.UniversalMediaPlayer>();
            _player.RenderingObjects = new GameObject[] { go };
            _player.AutoPlay = false;
            _player.ConfigureDesktop(_rtspOverTcp, _networkCaching, _hardwareDecoding, _logLevel);

            go.SetActive(wasActive);
        }

        _player.AddImageReadyEvent(OnImageReady);
        _player.AddStoppedEvent(OnStopped);
        _player.AddPlayingEvent(OnPlaying);
        _player.AddEncounteredErrorEvent(OnError);

        SetImageVisible(false);

        // 点击全屏：返回按钮字体复用项目现有的中文UI字体
        if (_fullScreen == null) _fullScreen = gameObject.AddComponent<CarVideoFullScreen>();
        Font uiFont = ui.CarPlate != null ? ui.CarPlate.font : null;
        _fullScreen.Init(_image, uiFont, () => _currentIp != null);

        _ready = true;
    }

    /// <summary>
    /// 读取StreamingAssets/AppSetting.txt 的 [Video] 段。文件、段或某项缺失/无效时保持默认值。
    /// </summary>
    void LoadSetting()
    {
        string path;
        Dictionary<string, string> cfg = AppSettingFile.LoadSection("Video", out path);
        if (cfg == null)
        {
            Log(AppSettingFile.FileName + " 不存在，使用默认视频参数。路径：" + path);
            return;
        }

        string val;
        if (cfg.TryGetValue("networkcaching", out val) && int.TryParse(val, out int caching) && caching >= 0) _networkCaching = caching;
        if (cfg.TryGetValue("rtspovertcp", out val) && bool.TryParse(val, out bool tcp)) _rtspOverTcp = tcp;
        if (cfg.TryGetValue("hardwaredecoding", out val) && bool.TryParse(val, out bool hw)) _hardwareDecoding = hw;
        if (cfg.TryGetValue("loglevel", out val) && Enum.TryParse(val, true, out UMP.LogLevels level)) _logLevel = level;

        Log($"视频参数：NetworkCaching={_networkCaching} RtspOverTcp={_rtspOverTcp} HardwareDecoding={_hardwareDecoding} LogLevel={_logLevel}");
    }

    /// <summary>
    /// 读取CarSetting.txt里每辆车的视频流地址（解析规则见CarSettingFile，格式：IP=车牌=视频流地址=车辆编号）。
    /// </summary>
    void LoadVideoUrls()
    {
        _urls.Clear();

        string path;
        List<CarSettingEntry> entries = CarSettingFile.Load(out path);
        if (entries == null)
        {
            Log("CarSetting.txt 不存在，无法读取视频流地址。路径：" + path);
            return;
        }

        foreach (CarSettingEntry entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.ip) && !string.IsNullOrEmpty(entry.videoUrl))
            {
                _urls[entry.ip] = entry.videoUrl;
            }
        }

        Log($"加载视频流地址 {_urls.Count} 条");
    }

    // ===================== 播放控制 =====================

    void PlayCar(string ip)
    {
        if (!_urls.TryGetValue(ip, out string url) || string.IsNullOrEmpty(url))
        {
            StopPlayer();
            Log($"车辆{ip}未配置视频流地址，不播放");
            return;
        }

        _player.Stop();
        _player.Path = url;
        _player.Play();

        Log($"开始拉流 IP={ip} URL={url}");
    }

    void StopPlayer()
    {
        if (_fullScreen != null) _fullScreen.Exit();
        if (_player != null)
        {
            _player.Stop();
        }
        SetImageVisible(false);
    }

    // 有画面时RawImage用白色(原色)显示；没有画面时用黑色底（场景里RawImage的颜色是黑色，不重置会把视频染黑）
    void SetImageVisible(bool visible)
    {
        if (_image != null)
        {
            _image.color = visible ? Color.white : Color.black;
        }
    }

    // ===================== UMP事件 =====================

    void OnImageReady(Texture2D image)
    {
        SetImageVisible(true);
    }

    void OnPlaying()
    {
        Log($"视频开始播放 IP={_currentIp}");
    }

    void OnStopped()
    {
        SetImageVisible(false);
    }

    void OnError()
    {
        string url = "";
        if (!string.IsNullOrEmpty(_currentIp))
        {
            _urls.TryGetValue(_currentIp, out url);
        }
        Log($"视频播放出错 IP={_currentIp} URL={url}");
        SetImageVisible(false);
    }

    static void Log(string msg)
    {
        WriteLog.instance?.WriteStringToFileOld(msg, LogFile);
    }
}
