using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class MainUI : MonoBehaviour
{
    public static MainUI instance;

    public Button MainCameraBtn;
    public Button ExitBtn;

    //主摄像机
    public Camera _mainCamera;
    //当前车辆摄像机
    public Camera vehicleCamera;
    public Camera vehicleMiniCamera;
    public GameObject currentPlateObj;

    //小地图
    public Button OpenMiniCamera;
    public Button CloseMiniCamera;
    public GameObject MiniCameraMax;


    private void Awake()
    {
        // 完善单例模式，防止多实例
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

    }

    // Start is called before the first frame update
    void Start()
    {
        MainCameraBtn.onClick.AddListener(MainCameraBtnEvent);
        ExitBtn.onClick.AddListener(ExitBtnEvent);

        OpenMiniCamera.onClick.AddListener(OpenMiniCameraEvent);
        CloseMiniCamera.onClick.AddListener(CloseMiniCameraEvent);
    }

    // Update is called once per frame
    void Update()
    {
     
    }

    private void MainCameraBtnEvent()
    {
        // 按住Ctrl点击：保存当前主相机参数到本地
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            OrbitCameraController.instance.SaveCameraToFile();
            WriteLog.instance?.WriteStringToFileOld("Ctrl+点击，已保存主摄像机位置旋转到CamerData.txt", "CameraData.txt");
            return;
        }

        OrbitCameraController.instance.RestoreFromFile();

        // 启用主摄像机
        _mainCamera.gameObject.SetActive(true);

        if (vehicleCamera != null)
        {
            vehicleCamera.gameObject.SetActive(false);
        }
        //if (currentPlateObj != null)
        //{
        //    currentPlateObj.SetActive(true);
        //}
        CarListUIManager.instance.ShowAllPlateObj();

        CarListUIManager.instance.hideCarInfo();
    }


    private void ExitBtnEvent() 
    {
        try
        {
            WriteLog.instance?.WriteStringToFileOld("执行关闭软件", "KaoShi.txt");

            // Unity 编辑器停止运行
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
        // 平台正常退出
        Application.Quit();
#endif
        }
        catch (System.Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld($"关闭软件出错: {e.Message}", "KaoShi.txt");
        }
    }

    private void OpenMiniCameraEvent()
    {
        MiniCameraMax.SetActive(true);
    }

    private void CloseMiniCameraEvent()
    {
        MiniCameraMax.SetActive(false);
    }
}
