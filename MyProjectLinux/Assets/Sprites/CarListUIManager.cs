using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CarListUIManager : MonoBehaviour
{
    public static CarListUIManager instance;

    [Header("UI绑定")]
    [Tooltip("车辆列表ScrollView的Content")]
    public Transform content;
    [Tooltip("CarItemBtn按钮预制体")]
    public GameObject carItemBtnPrefab;

    [Header("按钮图片")]
    [Tooltip("车辆在线时按钮图片")]
    public Sprite onlineSprite;   // 拖入 5.png

    [Tooltip("车辆离线时按钮图片")]
    public Sprite offlineSprite;  // 拖入默认灰色图

    //ip→按钮实例
    private Dictionary<string, GameObject> _ipBtnDict = new Dictionary<string, GameObject>();

    public GameObject baseInfoObj;
    public GameObject videoObj;
    public GameObject miniCameraObj;
    public Text CarPlate;
    public Text CarAngle;
    public Text posStarNum;
    public Text posStarState;
    public Text angleStarNum;
    public Text angleStarState;

    public string currentClickIp;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    /// <summary>
    /// 根据CarSetting.txt全部配置生成按钮
    /// </summary>
    public void BuildAllCarButton()
    {
        if (UDPControl.instance == null)
        {
            Debug.LogError("UDPControl.instance为空");
            return;
        }
        //清空旧按钮
        foreach (var go in _ipBtnDict.Values)
        {
            Destroy(go);
        }
        _ipBtnDict.Clear();

        //遍历配置文件全部车辆
        foreach (var kv in UDPControl.instance.ipPlateDict)
        {
            string ip = kv.Key;
            string plate = kv.Value;

            GameObject btnGo = Instantiate(carItemBtnPrefab, content);
            btnGo.name = $"Btn_{ip}_{plate}";

            Text btnText = btnGo.GetComponentInChildren<Text>();
            if (btnText != null)
            {
                btnText.text = plate;
            }

            Button btnComp = btnGo.GetComponent<Button>();
            string curIp = ip;
            btnComp.onClick.AddListener(() =>
            {
                OnCarBtnClick(curIp);
            });

            _ipBtnDict[ip] = btnGo;
        }
        RefreshAllBtnState();
    }

    /// <summary>
    /// 刷新所有按钮：在线正常；离线置灰不可交互
    /// </summary>
    public void RefreshAllBtnState()
    {
        foreach (var kv in _ipBtnDict)
        {
            string ip = kv.Key;
            GameObject btnGo = kv.Value;
            Button btn = btnGo.GetComponent<Button>();
            Image img = btnGo.GetComponent<Image>();

            bool isOnline = UDPControl.instance.ipCarDict.ContainsKey(ip);
            if (btn != null)
            {
                btn.interactable = isOnline;
            }

            // 切换在线/离线图片
            if (img != null)
            {
                if (isOnline)
                {
                    img.sprite = onlineSprite;     // 在线：5.png
                    img.color = Color.white;       // 恢复原色
                }
                else
                {
                    img.sprite = offlineSprite;    // 离线：默认灰色图
                    img.color = Color.white;       // 不再靠灰色 tint，直接用灰色图
                }
            }
        }
    }

    /// <summary>
    /// 按钮点击逻辑
    /// </summary>
    void OnCarBtnClick(string ip)
    {
        bool isOnline = UDPControl.instance.ipCarDict.ContainsKey(ip);
        if (!isOnline)
        {
            Debug.Log($"提示：车辆 {ip} 未上线！");
            //可以替换弹窗提示
            return;
        }
        //隐藏所有车牌
        HideAllPlateObj();

        //拿到车辆GameObject
        if (UDPControl.instance.ipCarDict.TryGetValue(ip, out GameObject carObj))
        {
            //替换原来的获取车载相机代码
            Transform vehCamTrans = carObj.transform.Find("Camera");
            Camera vehCamera = null;
            if (vehCamTrans != null)
            {
                vehCamera = vehCamTrans.GetComponent<Camera>();
            }

            if (vehCamera != null)
            {
                if(MainUI.instance.vehicleCamera != null)
                {//上一个车
                    MainUI.instance.vehicleCamera.gameObject.SetActive(false);
                    MainUI.instance.vehicleMiniCamera.gameObject.SetActive(false);
                }

                // 按名字查找小视图相机
                Transform miniCamTrans = carObj.transform.Find("小视图");
                Camera vehMiniCamera = null;
                if (miniCamTrans != null)
                {
                    vehMiniCamera = miniCamTrans.GetComponent<Camera>();
                    vehMiniCamera.gameObject.SetActive(true);
                }

                vehCamera.gameObject.SetActive(true);
                MainUI.instance.vehicleCamera = vehCamera;
                MainUI.instance.vehicleMiniCamera = vehMiniCamera;

                if (UDPControl.instance != null && UDPControl.instance.ipPlateObjDict.ContainsKey(ip))
                {
                    UDPControl.instance.ipPlateObjDict[ip].SetActive(false);
                    MainUI.instance.currentPlateObj = UDPControl.instance.ipPlateObjDict[ip];
                }

                MainUI.instance._mainCamera.gameObject.SetActive(false);

                currentClickIp = ip;
                showCarInfo();
            }
        }
    }

    public void showCarInfo()
    {
        baseInfoObj.SetActive(true);
        videoObj.SetActive(true);
        miniCameraObj.SetActive(true);
    }

    public void RefreshInfo(string ip,string gpsInfo)
    {
        // 如果当前选中IP为空，或者这条报文不是选中车辆的，直接跳过
        if (string.IsNullOrEmpty(currentClickIp) || currentClickIp != ip)
        {
            return;
        }

        // 分割KSXT报文
        string[] data = gpsInfo.Split(',');
        if (data.Length < 10)
        {
            return;
        }
        
        // KSXT字段索引，按协议顺序：
        string heading = "航向："+data[5];
        string posSatCount = "定位卫星：" + data[10];
        string posState = "定位状态：" + data[12];
        string angleSatCount = "定向卫星：" + data[11];
        string angleState = "定向状态：" + data[13];
    //    Debug.Log(heading+"  "+ posSatCount+"   "+ posState+"  "+ angleSatCount+"  "+ angleState);
        //赋值给UI文本
        CarPlate.text = "考车："+ UDPControl.instance.ipPlateDict[ip];
        CarAngle.text = heading;
        posStarNum.text = posSatCount;
        posStarState.text = posState;
        angleStarNum.text = angleSatCount;
        angleStarState.text = angleState;
    }

    public void hideCarInfo() 
    {
        baseInfoObj.SetActive(false);
        videoObj.SetActive(false);
        miniCameraObj.SetActive(false);

        currentClickIp = null;
    }

    /// <summary>
    /// 全部车牌显示
    /// </summary>
    public void ShowAllPlateObj()
    {
        foreach (var plateGo in UDPControl.instance.ipPlateObjDict.Values)
        {
            plateGo.SetActive(true);
        }
    }
    /// <summary>
    /// 全部车牌隐藏
    /// </summary>
    public void HideAllPlateObj()
    {
        foreach (var plateGo in UDPControl.instance.ipPlateObjDict.Values)
        {
            plateGo.SetActive(false);
        }
    }
}
