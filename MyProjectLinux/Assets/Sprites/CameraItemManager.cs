using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class CameraItemManager : MonoBehaviour
{
    [Header("UI引用")]
    [Tooltip("ScrollView的Content")]
    public Transform content;
    [Tooltip("按钮预制体")]
    public GameObject cameraItemBtnPrefab;

    //项目列表，来自CameraItem.txt
    private List<string> _itemNameList = new List<string>();
    //每个项目保存的机位
    private Dictionary<string, CameraItemPose> _poseDict = new Dictionary<string, CameraItemPose>();

    //文件路径
    private string _cameraItemTxtPath;
    private string _savePosePath;

    [Serializable]
    public class CameraItemPose
    {
        public Vector3 TargetPoint;
        public float Yaw;
        public float Pitch;
        public float Distance;
    }

    [Serializable]
    public class AllCameraPoseSave
    {
        public List<CameraItemPoseItem> Items = new List<CameraItemPoseItem>();
    }
    [Serializable]
    public class CameraItemPoseItem
    {
        public string ItemName;
        public CameraItemPose Pose;
    }

    private void Awake()
    {
        _cameraItemTxtPath = Path.Combine(Application.streamingAssetsPath, "CameraItem.txt");

#if UNITY_EDITOR
        string rootDir = Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
    string rootDir = Directory.GetParent(Application.dataPath).FullName;
#else
    string rootDir = Application.persistentDataPath;
#endif
        //保存文件exe同级
        _savePosePath = Path.Combine(rootDir, "CameraItemSaveData.txt");

        Debug.Log($"Txt配置:{_cameraItemTxtPath}");
        Debug.Log($"保存文件:{_savePosePath}");

        //1.读取项目名称列表
        ReadCameraItemTxt();
        //2.读取保存的机位数据
        LoadSavedPose();
        //3.动态生成全部按钮
        SpawnAllButtons();
    }

    /// <summary>
    /// 读取CameraItem.txt，每行是项目名字
    /// </summary>
    void ReadCameraItemTxt()
    {
        Debug.Log("ReadCameraItemTxt");
        _itemNameList.Clear();
        if (!File.Exists(_cameraItemTxtPath))
        {
            Debug.Log($"CameraItem.txt不存在:{_cameraItemTxtPath}");

            WriteLog.instance?.WriteStringToFileOld($"CameraItem.txt不存在:{_cameraItemTxtPath}", "CameraItem.txt");
            return;
        }
        Debug.Log($"CameraItem.txt存在:{_cameraItemTxtPath}");

        string[] lines = File.ReadAllLines(_cameraItemTxtPath);
        foreach (var line in lines)
        {
            string name = line.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                _itemNameList.Add(name);
            }
        }
    }

    /// <summary>
    /// 加载保存好的机位
    /// </summary>
    void LoadSavedPose()
    {
        _poseDict.Clear();
        if (!File.Exists(_savePosePath))
        {
            return;
        }
        try
        {
            string json = File.ReadAllText(_savePosePath);
            AllCameraPoseSave saveData = JsonUtility.FromJson<AllCameraPoseSave>(json);
            foreach (var item in saveData.Items)
            {
                if (!string.IsNullOrEmpty(item.ItemName))
                {
                    _poseDict[item.ItemName] = item.Pose;
                }
            }
        }
        catch (Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld($"读取机位保存文件异常 {e.Message}", "CameraItemSaveData.txt");
        }
    }

    /// <summary>
    /// 保存全部机位到文件
    /// </summary>
    void SaveAllPoseToFile()
    {
        AllCameraPoseSave saveData = new AllCameraPoseSave();
        foreach (var kv in _poseDict)
        {
            saveData.Items.Add(new CameraItemPoseItem
            {
                ItemName = kv.Key,
                Pose = kv.Value
            });
        }
        string json = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(_savePosePath, json);
        WriteLog.instance?.WriteStringToFileOld($"已保存机位数据到 {_savePosePath}", "CameraItemSaveData.txt");
    }

    /// <summary>
    /// 实例化所有按钮
    /// </summary>
    void SpawnAllButtons()
    {
        //清除旧子物体
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(content.GetChild(i).gameObject);
        }

        foreach (string itemName in _itemNameList)
        {
            GameObject btn = Instantiate(cameraItemBtnPrefab, content);
            btn.name = $"Btn_{itemName}";
            //设置按钮文字
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.text = itemName;

            //按钮点击回调
            string captureName = itemName;
            btn.GetComponent<Button>().onClick.AddListener(() =>
            {
                OnItemButtonClick(captureName);
            });
        }
    }

    /// <summary>
    /// 按钮点击
    /// Ctrl按住：保存当前相机为此项目机位；普通点击：加载机位
    /// </summary>
    void OnItemButtonClick(string itemName)
    {
        OrbitCameraController camCtrl = OrbitCameraController.instance;
        if (camCtrl == null) return;

        bool ctrlHold = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (ctrlHold)
        {
            // =========按住Ctrl点击：保存当前机位 =========
            CameraItemPose newPose = new CameraItemPose();
            newPose.TargetPoint = camCtrl._currentTarget;
            newPose.Yaw = camCtrl._currentYaw;
            newPose.Pitch = camCtrl._currentPitch;
            newPose.Distance = camCtrl._currentDistance;
            _poseDict[itemName] = newPose;
            SaveAllPoseToFile();
            WriteLog.instance?.WriteStringToFileOld($"Ctrl+点击，保存项目[{itemName}]机位", "CameraItemSaveData.txt");
        }
        else
        {
            // =========普通点击：加载机位 =========
            if (!_poseDict.ContainsKey(itemName))
            {
                WriteLog.instance?.WriteStringToFileOld($"项目[{itemName}]还没有保存机位，跳过", "CameraItemSaveData.txt");
                return;
            }
            CameraItemPose pose = _poseDict[itemName];
            //直接赋值轨道相机的目标变量
            camCtrl._targetTarget = pose.TargetPoint;
            camCtrl._targetYaw = pose.Yaw;
            camCtrl._targetPitch = pose.Pitch;
            camCtrl._targetDistance = pose.Distance;
            WriteLog.instance?.WriteStringToFileOld($"切换机位[{itemName}]", "CameraItemSaveData.txt");
        }
    }
}
