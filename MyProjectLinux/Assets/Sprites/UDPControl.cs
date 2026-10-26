using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class UDPControl : MonoBehaviour
{
    public static UDPControl instance;

    private UdpClient udpClient;
    private Thread udpThread;
    private bool isRun = false;
    public int port = 12345;

    [Header("车辆预制体，拖入Car预制体")]
    public GameObject carPrefab;

    // IP -> 车辆对象缓存
    public Dictionary<string, GameObject> ipCarDict = new Dictionary<string, GameObject>();
    // IP -> 车牌号映射（从CarSetting.txt读取）
    public Dictionary<string, string> ipPlateDict = new Dictionary<string, string>();
    //IP -> 车牌对象缓存
    public Dictionary<string, GameObject> ipPlateObjDict = new Dictionary<string, GameObject>();

    // 子线程消息回传队列（主线程Update消费）
    private Queue<UdpReceiveMsg> msgQueue = new Queue<UdpReceiveMsg>();

    public int isHorizontal;//修正航向数值
    public double lon1 = 117.157330360214;
    public double lat1 = 31.8177080750154;
    public float x1;
    public float z1;

    public double lon2 = 117.157353980214;
    public double lat2 = 31.8177122550154;
    public float x2;
    public float z2;


    [Serializable]
    public struct UdpReceiveMsg
    {
        public string remoteIP;
        public string content;
    }

    public string AndroPath;

    //全局引用，把场景上GlobalPlateCanvas拖进来
    public Canvas globalPlateCanvas;
    public GameObject plateUIPrefab;

    //===== 新增：超时下线管理 =====
    private Dictionary<string, float> ipLastRecvTime = new Dictionary<string, float>();
    public float carTimeoutSec = 15.0f; //5秒超时

    private void Awake()
    {
        // 完善单例模式，防止多实例
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            // ========== 读取配置文件 ==========
            string configPath = "";
#if UNITY_ANDROID
            configPath = Path.Combine(Application.persistentDataPath, "sceneModelConfig.txt");
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR || UNITY_STANDALONE_LINUX
            configPath = Path.Combine(Application.streamingAssetsPath, "sceneModelConfig.txt");
#endif
            Debug.Log("configPath::"+ configPath);


#if UNITY_ANDROID
            AndroPath = Application.persistentDataPath;
#elif UNITY_EDITOR
            AndroPath = "E:\\XiangMu";
#elif UNITY_STANDALONE_WIN
        AndroPath = Path.GetDirectoryName(Application.dataPath);//exe同级目录
#elif UNITY_STANDALONE_LINUX
        // Linux：程序可执行文件同级目录
        AndroPath = Path.GetDirectoryName(Application.dataPath);//exe同级目录
#else
        AndroPath = "E:\\XiangMu";
#endif

            Debug.Log("AndroPath::" + AndroPath);

            LoadSceneModelConfig(configPath);
        }
        else
        {
            Destroy(gameObject);
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        // 第一步：加载CarSetting.txt IP-车牌号配置
        LoadCarSetting();
        // 第二步：启动UDP监听
        StartUDP();

        CarListUIManager.instance.BuildAllCarButton();
    }

    #region 加载配置 CarSetting.txt
    public void LoadCarSetting()
    {
        ipPlateDict.Clear();
        string filePath = Path.Combine(Application.streamingAssetsPath, "CarSetting.txt");
        // Linux打包后兼容路径
        if (!File.Exists(filePath))
        {
            filePath = Path.Combine(Directory.GetCurrentDirectory(), "CarSetting.txt");
        }
        if (!File.Exists(filePath))
        {
            WriteLog.instance?.WriteStringToFileOld("CarSetting.txt 不存在！路径：" + filePath, "UDP.txt");
            return;
        }
        string[] lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            string trimLine = line.Trim();
            if (string.IsNullOrEmpty(trimLine) || trimLine.StartsWith("#")) continue;
            // 格式：IP=车牌号[=视频流地址]。只取前两段；第3段(视频流地址)由CarVideoPlayer自行读取
            string[] kv = trimLine.Split(new char[] { '=' }, 3);
            if (kv.Length >= 2)
            {
                string ip = kv[0].Trim();
                string plate = kv[1].Trim();
                ipPlateDict[ip] = plate;

                WriteLog.instance?.WriteStringToFileOld($"加载配置：{ip} → {plate}", "UDP.txt");
            }
        }
    }
    #endregion

    #region UDP 接收线程
    public void StartUDP()
    {
        try
        {
            // 【关键】先关闭旧udp，防止重复绑定
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient.Dispose();
                udpClient = null;
            }
            if (udpThread != null && udpThread.IsAlive)
            {
                isRun = false;
                udpThread.Join(200); // 等待线程正常退出，不要Abort
            }

            udpClient = new UdpClient(port);
        //    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            isRun = true;
            udpThread = new Thread(UdpReceiveLoop);
            udpThread.IsBackground = true;
            udpThread.Start();

            WriteLog.instance?.WriteStringToFileOld($"UDP监听启动，端口：{port}", "UDP.txt");
        }
        catch (Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld("UDP启动失败：" + e, "UDP.txt");
        }
    }

    void UdpReceiveLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        while (isRun)
        {
            try
            {
                byte[] recvBytes = udpClient.Receive(ref remoteEP);
                string msg = System.Text.Encoding.UTF8.GetString(recvBytes);
                string sendIP = remoteEP.Address.ToString();

                WriteLog.instance?.WriteStringToFileOld($"UdpReceiveLoop，发送方ip：{sendIP}", "UDP.txt");

                lock (msgQueue)
                {
                    msgQueue.Enqueue(new UdpReceiveMsg()
                    {
                        remoteIP = sendIP,
                        content = msg
                    });
                }
            }
            catch (SocketException se)
            {
                if (isRun == false) break;

                WriteLog.instance?.WriteStringToFileOld("UDP接收异常:" + se, "UDP.txt");
            }
            catch (Exception ex)
            {
                WriteLog.instance?.WriteStringToFileOld("UDP线程异常:" + ex, "UDP.txt");
            }
        }
    }
    #endregion

    void Update()
    {
        // 主线程处理UDP消息（子线程不能操作GameObject）
        lock (msgQueue)
        {
            while (msgQueue.Count > 0)
            {
                var msg = msgQueue.Dequeue();
                ProcessCarData(msg.remoteIP, msg.content);
            }
        }

        //=====新增：超时检测逻辑=====
        List<string> timeoutIpList = new List<string>();
        foreach (var kv in ipLastRecvTime)
        {
            float elapsed = Time.time - kv.Value;
            if (elapsed > carTimeoutSec)
            {
                timeoutIpList.Add(kv.Key);
            }
        }
        foreach (var ip in timeoutIpList)
        {
            DestroyCarByIp(ip);
        }
    }

    /// <summary>
    /// 根据IP销毁车辆+车牌，清理全部字典
    /// </summary>
    void DestroyCarByIp(string ip)
    {
        WriteLog.instance?.WriteStringToFileOld($"车辆超时{carTimeoutSec}秒无数据，销毁IP:{ip}", "UDP.txt");

        //销毁车辆物体
        if (ipCarDict.TryGetValue(ip, out GameObject carGo))
        {
            Destroy(carGo);
            ipCarDict.Remove(ip);
        }
        //销毁车牌UI
        if (ipPlateObjDict.TryGetValue(ip, out GameObject plateGo))
        {
            Destroy(plateGo);
            ipPlateObjDict.Remove(ip);
        }
        //清理超时记录
        ipLastRecvTime.Remove(ip);

        //========新增，车辆下线，刷新按钮状态
        CarListUIManager.instance?.RefreshAllBtnState();
    }

    #region 解析车辆数据，生成/同步车辆
    void ProcessCarData(string ip, string dataStr)
    {
        // 过滤协议尾换行、空格
        dataStr = dataStr.Trim();
        if (!dataStr.StartsWith("$KSXT"))
        {
            WriteLog.instance?.WriteStringToFileOld($"非KSXT协议包，丢弃：{dataStr}", "UDP.txt");
            return;
        }
        string[] data = dataStr.Split(',');
        // KSXT固定至少23个分段
        if (data.Length < 23)
        {
            WriteLog.instance?.WriteStringToFileOld($"KSXT数据包字段不全 长度:{data.Length} IP:{ip}", "UDP.txt");
            return;
        }

        //====新增：收到报文立刻刷新该IP存活时间戳====
        ipLastRecvTime[ip] = Time.time;

        if (!ipCarDict.ContainsKey(ip))
        {
            CreateNewCar(ip, Vector3.zero, Quaternion.identity);

            //新增：通知UI刷新按钮状态
            CarListUIManager.instance?.RefreshAllBtnState();
        }
        else
        {
            CarListUIManager.instance.RefreshInfo(ip, dataStr);

            GameObject car = ipCarDict[ip];
            // 找到车辆上的控制器，推送GPS数据
            GeoOffsetCalculator carCtrl = car.GetComponent<GeoOffsetCalculator>();
            WriteLog.instance?.WriteStringToFileOld($"ProcessCarDataProcess：{dataStr}", "CarMove.txt");
            if (carCtrl != null)
            {
                carCtrl.CarMove(dataStr);
            }
            else
            {
                WriteLog.instance?.WriteStringToFileOld($"carCtrl：==null", "CarMove.txt");
            }
        }
    }

    void CreateNewCar(string ip, Vector3 pos, Quaternion rot)
    {
        if (carPrefab == null)
        {
            WriteLog.instance?.WriteStringToFileOld("请赋值Car预制体！", "UDP.txt");
            return;
        }
        GameObject newCar = Instantiate(carPrefab, pos, rot);
        newCar.name = "Car_" + ip;
        ipCarDict.Add(ip, newCar);

        //实例车牌UI，父物体设置为全局GlobalPlateCanvas的transform
        GameObject plateUiGo = Instantiate(plateUIPrefab, globalPlateCanvas.transform);
        plateUiGo.name = "Plate_" + ip;
        ipPlateObjDict.Add(ip, plateUiGo);

        UILicensePlate plate = plateUiGo.GetComponent<UILicensePlate>();
        plate.carTarget = newCar.transform;
        plate.CarIp = ip;

        // 按名字查找车载相机
        Transform vehCamTrans = newCar.transform.Find("Camera");
        if (vehCamTrans != null)
        {
            plate.vehicleCamera = vehCamTrans.GetComponent<Camera>();
        }
        // 按名字查找小视图相机
        Transform miniCamTrans = newCar.transform.Find("小视图");
        if (miniCamTrans != null)
        {
            plate.MiniCamera = miniCamTrans.GetComponent<Camera>();
        }
        // 如果没找到，尝试包含非激活的子物体
        if (plate.vehicleCamera == null)
        {
            plate.vehicleCamera = newCar.GetComponentInChildren<Camera>(true);
        }

        // 设置车顶车牌号
        if (ipPlateDict.TryGetValue(ip, out string plateNum))
        {
            plate.setPlateNum(plateNum);
            WriteLog.instance?.WriteStringToFileOld($"新车辆生成：IP={ip} 车牌={plateNum}", "UDP.txt");
        }
        else
        {
            WriteLog.instance?.WriteStringToFileOld($"CarSetting.txt 未配置IP：{ip}，车牌未知", "UDP.txt");
        }

        //新建车辆的时候初始化存活时间
        ipLastRecvTime[ip] = Time.time;
    }
    #endregion

    private void OnDestroy()
    {
        isRun = false;
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
        if (udpThread != null && udpThread.IsAlive)
        {
            udpThread.Join(300); // 等待线程自己退出，超时300ms
        }
        udpThread = null;

        //====新增：销毁全部车辆、车牌
        foreach (var kv in ipCarDict)
        {
            Destroy(kv.Value);
        }
        foreach (var kv in ipPlateObjDict)
        {
            Destroy(kv.Value);
        }
        ipCarDict.Clear();
        ipPlateObjDict.Clear();
        ipLastRecvTime.Clear();
    }

    /// <summary>
    /// 读取 sceneModelConfig.txt 配置，赋值两组参考点参数
    /// </summary>
    public bool LoadSceneModelConfig(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            WriteLog.instance?.WriteStringToFileOld($"sceneModelConfig.txt 不存在：{configFilePath}", "UDP.txt");
            return false;
        }

        WriteLog.instance?.WriteStringToFileOld($"sceneModelConfig.txt ：{configFilePath}", "UDP.txt");
        Debug.Log($"sceneModelConfig.txt ：{configFilePath}");
        try
        {
            string[] lines = File.ReadAllLines(configFilePath);
            foreach (string line in lines)
            {
                string trimLine = line.Trim();
                if (string.IsNullOrEmpty(trimLine) || !trimLine.Contains("="))
                    continue;

                string[] kv = trimLine.Split('=');
                if (kv.Length != 2) continue;

                string key = kv[0].Trim();
                string valueStr = kv[1].Trim();

                switch (key)
                {
                    case "CanKaoPointOne_lon":
                        double.TryParse(valueStr, out lon1);
                        break;
                    case "CanKaoPointOne_lat":
                        double.TryParse(valueStr, out lat1);
                        break;
                    case "CanKaoPointOne_x":
                        float.TryParse(valueStr, out x1);
                        break;
                    case "CanKaoPointOne_y":
                        float.TryParse(valueStr, out z1);
                        break;
                    case "CanKaoPointTwo_lon":
                        double.TryParse(valueStr, out lon2);
                        break;
                    case "CanKaoPointTwo_lat":
                        double.TryParse(valueStr, out lat2);
                        break;
                    case "CanKaoPointTwo_x":
                        float.TryParse(valueStr, out x2);
                        break;
                    case "CanKaoPointTwo_y":
                        float.TryParse(valueStr, out z2);
                        break;
                }
            }
            Debug.Log($"✅ 成功加载参考点配置：\nlon1={lon1},lat1={lat1},x1={x1},z1={z1}\nlon2={lon2},lat2={lat2},x2={x2},z2={z2}");
            WriteLog.instance?.WriteStringToFileOld($"✅ 成功加载参考点配置：\nlon1={lon1},lat1={lat1},x1={x1},z1={z1}\nlon2={lon2},lat2={lat2},x2={x2},z2={z2}", "UDP.txt");
            return true;
        }
        catch (Exception ex)
        {
            WriteLog.instance?.WriteStringToFileOld($"读取sceneModelConfig.txt异常：{ex}", "UDP.txt");
            return false;
        }
    }


    /// <summary>
    /// WGS84经纬度转Unity XZ，支持旋转+缩放+平移，解决斜向参考点带来点位偏移
    /// </summary>
    /// <param name="longitude">目标经度</param>
    /// <param name="latitude">目标纬度</param>
    /// <returns>Unity XZ坐标，Y=0</returns>
    public Vector3 ConvertToXZ(double longitude, double latitude)
    {
        // WGS84地球半径
        const double EarthR = 6378137.0;

        // ----------参考点A、B GPS----------
        double lonA = lon1;
        double latA = lat1;
        double lonB = lon2;
        double latB = lat2;

        // ----------参考点A、B Unity坐标----------
        Vector2 ptA = new Vector2(x1, z1);
        Vector2 ptB = new Vector2(x2, z2);

        // 转弧度
        double latRadA = latA * Math.PI / 180.0;
        double lonRadA = lonA * Math.PI / 180.0;
        double latRadB = latB * Math.PI / 180.0;
        double lonRadB = lonB * Math.PI / 180.0;
        double tarLatRad = latitude * Math.PI / 180.0;
        double tarLonRad = longitude * Math.PI / 180.0;

        // 以A为原点，计算B相对于A的东北局部坐标(米)
        double dLonAB = lonRadB - lonRadA;
        double dLatAB = latRadB - latRadA;

        // 局部平面东(E)、北(N)
        double E_B = EarthR * Math.Cos(latRadA) * dLonAB;
        double N_B = EarthR * dLatAB;

        // Unity里面B相对于A的向量
        Vector2 unityVecB = ptB - ptA;

        // GPS局部向量
        Vector2 gpsVecB = new Vector2((float)E_B, (float)N_B);

        // 防除零：两点不能重合
        if (gpsVecB.magnitude < 0.001f || unityVecB.magnitude < 0.001f)
        {
            Debug.LogError("GeoOffsetCalculator：两个参考点距离过近，无法计算转换");
            return new Vector3(0, 0, 0);
        }

        // 1、缩放系数：GPS实际米 / Unity距离
        float scale = unityVecB.magnitude / gpsVecB.magnitude;

        // 2、求角度：GPS向量角度、Unity向量角度
        double gpsAngle = Math.Atan2(gpsVecB.y, gpsVecB.x);
        double unityAngle = Math.Atan2(unityVecB.y, unityVecB.x);
        double deltaRotate = unityAngle - gpsAngle; // 需要旋转的角度（弧度）

        // =========目标点：相对于参考点A的局部东北坐标=========
        double dLonTar = tarLonRad - lonRadA;
        double dLatTar = tarLatRad - latRadA;
        double E_tar = EarthR * Math.Cos(latRadA) * dLonTar;
        double N_tar = EarthR * dLatTar;

        // 缩放
        float eScaled = (float)(E_tar * scale);
        float nScaled = (float)(N_tar * scale);

        // 旋转矩阵：把GPS局部坐标系旋转到Unity坐标系
        double cosR = Math.Cos(deltaRotate);
        double sinR = Math.Sin(deltaRotate);
        float xRot = (float)(eScaled * cosR - nScaled * sinR);
        float zRot = (float)(eScaled * sinR + nScaled * cosR);

        // 平移：加上参考点A的Unity坐标
        float finalX = ptA.x + xRot;
        float finalZ = ptA.y + zRot;

        WriteLog.instance?.WriteStringToFileOld(
            $"scale:{scale:F4},deltaRotate(deg):{deltaRotate * 180 / Math.PI:F2}° | GPS->Unity X:{finalX:F3} Z:{finalZ:F3}",
            "CheZai.txt");

        return new Vector3(finalX, 0, finalZ);
    }

}
