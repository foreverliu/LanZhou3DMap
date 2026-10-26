using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

public class OrbitCameraController : MonoBehaviour
{
    public static OrbitCameraController instance;

    [Header("相机参数")]
    [Tooltip("相机围绕旋转的中心点")]
    public Vector3 targetPoint = new Vector3(0, 0, 0);

    [Tooltip("相机与目标点的默认距离")]
    public float defaultDistance = 50f;

    [Tooltip("最小缩放距离")]
    public float minDistance = 5f;

    [Tooltip("最大缩放距离")]
    public float maxDistance = 200f;

    [Header("旋转速度")]
    [Tooltip("鼠标拖拽旋转速度")]
    public float rotateSpeed = 0.3f;

    [Tooltip("自动旋转速度（可选）")]
    public float autoRotateSpeed = 0f;

    [Header("平移速度")]
    [Tooltip("鼠标拖拽平移速度")]
    public float panSpeed = 0.5f;

    [Header("缩放速度")]
    [Tooltip("滚轮缩放速度")]
    public float zoomSpeed = 10f;

    [Header("平滑参数")]
    [Tooltip("启用平滑过渡")]
    public bool enableSmooth = true;

    [Tooltip("平滑速度")]
    public float smoothSpeed = 8f;

    // 内部状态
    public float _currentYaw;
    public float _currentPitch;
    public float _currentDistance;
    public Vector3 _currentTarget;

    private Vector2 _lastMousePos;
    private bool _isDragging;

    // 平滑用的目标值
    public float _targetYaw;
    public float _targetPitch;
    public float _targetDistance;
    public Vector3 _targetTarget;

    // 是否已从配置文件加载
    private bool _isConfigLoaded;

    // 摄像头配置文件路径
    private string filePath;

    void Awake()
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

#if UNITY_EDITOR
        //编辑器：Assets上一级，项目根目录
        filePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CamerData.txt");
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
    // Windows / Linux 打包：可执行程序同级目录
    filePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CamerData.txt");
#else
    //其他平台fallback
    filePath = Path.Combine(Application.persistentDataPath, "CamerData.txt");
#endif


        // 在Awake中加载配置文件（在Start之前执行）
        LoadCameraConfig();
    }

    void Start()
    {
        // 如果配置未加载成功，使用默认值初始化
        if (!_isConfigLoaded)
        {
            InitializeFromCurrentTransform();
        }

        // 应用初始位置
        //UpdateCameraPosition(false);
    }

    /// <summary>
    /// 加载相机配置文件
    /// </summary>
    public void LoadCameraConfig()
    {
        // 如果文件不存在，使用默认值
        if (!File.Exists(filePath))
        {
            WriteLog.instance?.WriteStringToFileOld($"配置文件不存在: {filePath}，使用默认值", "CameraData.txt");

            InitializeFromCurrentTransform();
            return;
        }

        try
        {
            string jsonText = File.ReadAllText(filePath);
            CameraTransformData data = JsonUtility.FromJson<CameraTransformData>(jsonText);

            if (data == null)
            {
                WriteLog.instance?.WriteStringToFileOld("配置文件解析失败，使用默认值", "CameraData.txt");

                InitializeFromCurrentTransform();
                return;
            }

            //1.读取配置里的目标点
            Vector3 readTarget;
            // JsonUtility反序列化class永远不为null，不能判null，判断是否全0
            if (!(Mathf.Abs(data.TargetPoint.X) < 0.001f && Mathf.Abs(data.TargetPoint.Y) < 0.001f && Mathf.Abs(data.TargetPoint.Z) < 0.001f))
            {
                readTarget = new Vector3(data.TargetPoint.X, data.TargetPoint.Y, data.TargetPoint.Z);
                targetPoint = readTarget;
            }
            else
            {
                // json中TargetPoint是空的，沿用Inspector上的targetPoint
                readTarget = targetPoint;
            }

            //2.读取配置的相机世界位置
            Vector3 readCamPos = new Vector3(data.Translation.X, data.Translation.Y, data.Translation.Z);

            //3.【重点】使用配置的相机位置与配置的目标点，计算轨道参数，不要用旧targetPoint！
            Vector3 dir = readCamPos - readTarget;
            float readDistance = dir.magnitude;

            Vector3 horizontalDir = new Vector3(dir.x, 0, dir.z);
            float readYaw = Mathf.Atan2(horizontalDir.x, horizontalDir.z) * Mathf.Rad2Deg;
            float readPitch = Mathf.Atan2(dir.y, horizontalDir.magnitude) * Mathf.Rad2Deg;
            readPitch = Mathf.Clamp(readPitch, -85f, 85f);

            //4.把读取出来的值赋值给脚本内部驱动变量（真正控制相机的源头）
            _currentTarget = readTarget;
            _targetTarget = readTarget;

            _currentDistance = readDistance;
            _targetDistance = readDistance;

            _currentYaw = readYaw;
            _targetYaw = readYaw;

            _currentPitch = readPitch;
            _targetPitch = readPitch;

            defaultDistance = readDistance;

            // ✅ 不要手动写 transform.position / transform.rotation！交给UpdateCameraPosition去计算
            //transform.position = configPosition;
            //transform.rotation = configRotation;

            _isConfigLoaded = true;

            //手动执行一次相机位置更新，立刻生效，不需要等到LateUpdate
            UpdateCameraPosition(false);

            WriteLog.instance?.WriteStringToFileOld($"成功加载相机配置: {filePath},位置: {transform.position}", "CameraData.txt");

        }
        catch (System.Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld($"加载配置文件失败: {e.Message}，使用默认值", "CameraData.txt");
            InitializeFromCurrentTransform();
        }
    }

    /// <summary>
    /// 从当前Transform初始化轨道参数
    /// </summary>
    void InitializeFromCurrentTransform()
    {
        Vector3 dir = transform.position - targetPoint;
        _currentDistance = dir.magnitude;
        _targetDistance = _currentDistance;

        Vector3 horizontalDir = new Vector3(dir.x, 0, dir.z);
        _currentYaw = Mathf.Atan2(horizontalDir.x, horizontalDir.z) * Mathf.Rad2Deg;
        _targetYaw = _currentYaw;

        _currentPitch = Mathf.Atan2(dir.y, horizontalDir.magnitude) * Mathf.Rad2Deg;
        _targetPitch = _currentPitch;

        _currentPitch = Mathf.Clamp(_currentPitch, -85f, 85f);
        _targetPitch = _currentPitch;

        _currentTarget = targetPoint;
        _targetTarget = targetPoint;

        _isConfigLoaded = true;
    }

    void LateUpdate()
    {
        HandleInput();

        // 自动旋转（可选）
        if (autoRotateSpeed != 0 && !Input.GetMouseButton(1))
        {
            _targetYaw += autoRotateSpeed * Time.deltaTime;
        }

        //// 更新相机位置
        UpdateCameraPosition(enableSmooth);
    }

    void HandleInput()
    {
        // ============ 新增：如果鼠标在UI上，直接屏蔽相机所有输入 ============
        // IsPointerOverGameObject(-1) PC平台，判断鼠标是否悬停/点击在UGUI上
        bool isMouseOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(-1);
        if (isMouseOverUI)
        {
            // 如果鼠标在UI区域，直接退出，不处理相机拖拽、旋转、滚轮
            return;
        }

        // ==================== 左键：平移 ====================
        if (Input.GetMouseButtonDown(0))
        {
            _lastMousePos = Input.mousePosition;
            _isDragging = true;
        }

        if (Input.GetMouseButton(0) && _isDragging)
        {
            Vector2 delta = (Vector2)Input.mousePosition - _lastMousePos;
            _lastMousePos = Input.mousePosition;

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            forward.y = 0;
            right.y = 0;
            forward.Normalize();
            right.Normalize();

            Vector3 offset = (-delta.x * panSpeed) * right + (-delta.y * panSpeed) * forward;
            float distanceScale = _currentDistance / 50f;
            offset *= distanceScale;

            _targetTarget += offset;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        // ==================== 右键：旋转 ====================
        if (Input.GetMouseButtonDown(1))
        {
            _lastMousePos = Input.mousePosition;
        }

        if (Input.GetMouseButton(1))
        {
            Vector2 delta = (Vector2)Input.mousePosition - _lastMousePos;
            _lastMousePos = Input.mousePosition;

            _targetYaw += delta.x * rotateSpeed;
            _targetPitch -= delta.y * rotateSpeed;
            _targetPitch = Mathf.Clamp(_targetPitch, -85f, 85f);
        }

        // ==================== 滚轮：缩放 ====================
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _targetDistance -= scroll * zoomSpeed * (_currentDistance / 50f);
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }
    }

    void UpdateCameraPosition(bool smooth)
    {
        if (smooth)
        {
            _currentTarget = Vector3.Lerp(_currentTarget, _targetTarget, smoothSpeed * Time.deltaTime);
            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, smoothSpeed * Time.deltaTime);
            _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, smoothSpeed * Time.deltaTime);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, smoothSpeed * Time.deltaTime);
        }
        else
        {
            _currentTarget = _targetTarget;
            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
            _currentDistance = _targetDistance;
        }

        float yawRad = _currentYaw * Mathf.Deg2Rad;
        float pitchRad = _currentPitch * Mathf.Deg2Rad;

        float horizontalDistance = _currentDistance * Mathf.Cos(pitchRad);
        float heightOffset = _currentDistance * Mathf.Sin(pitchRad);

        float x = _currentTarget.x + horizontalDistance * Mathf.Sin(yawRad);
        float z = _currentTarget.z + horizontalDistance * Mathf.Cos(yawRad);
        float y = _currentTarget.y + heightOffset;

        Vector3 targetPosition = new Vector3(x, y, z);

        transform.position = targetPosition;
        transform.LookAt(_currentTarget);
    }

    /// <summary>
    /// 保存当前相机状态到配置文件
    /// </summary>
    public void SaveCameraToFile()
    {
        try
        {
            CameraTransformData saveData = new CameraTransformData();

            // 保存位置
            saveData.Translation = new Vec3
            {
                X = transform.position.x,
                Y = transform.position.y,
                Z = transform.position.z
            };

            // 保存旋转（欧拉角）
            Vector3 euler = transform.rotation.eulerAngles;
            saveData.Rotation = new RotData
            {
                Pitch = euler.x,
                Yaw = euler.y,
                Roll = euler.z
            };

            // 保存目标点
            saveData.TargetPoint = new Vec3
            {
                X = _currentTarget.x,
                Y = _currentTarget.y,
                Z = _currentTarget.z
            };

            // 保存距离
            saveData.Distance = _currentDistance;

            // 保存缩放
            saveData.Scale = new Vec3 { X = 1, Y = 1, Z = 1 };

            string json = JsonUtility.ToJson(saveData, prettyPrint: true);
            File.WriteAllText(filePath, json);

            WriteLog.instance?.WriteStringToFileOld($"相机配置已保存: {filePath}", "CameraData.txt");
        }
        catch (System.Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld($"保存配置文件失败: {e.Message}", "CameraData.txt");
        }
    }

    /// <summary>
    /// 手动从本地文件还原相机配置（按钮点击调用）
    /// </summary>
    public void RestoreFromFile()
    {
        if (!File.Exists(filePath))
        {
            WriteLog.instance?.WriteStringToFileOld($"RestoreFromFile：配置文件不存在 {filePath}", "CameraData.txt");
            return;
        }
        try
        {
            string jsonText = File.ReadAllText(filePath);
            CameraTransformData data = JsonUtility.FromJson<CameraTransformData>(jsonText);
            if (data == null)
            {
                WriteLog.instance?.WriteStringToFileOld($"RestoreFromFile：JSON解析失败", "CameraData.txt");
                return;
            }

            //读取真实保存的轨道中心点
            Vector3 readTarget = new Vector3(data.TargetPoint.X, data.TargetPoint.Y, data.TargetPoint.Z);
            Vector3 readCamPos = new Vector3(data.Translation.X, data.Translation.Y, data.Translation.Z);

            Vector3 dir = readCamPos - readTarget;
            float readDistance = dir.magnitude;
            Vector3 horizontalDir = new Vector3(dir.x, 0, dir.z);
            float readYaw = Mathf.Atan2(horizontalDir.x, horizontalDir.z) * Mathf.Rad2Deg;
            float readPitch = Mathf.Atan2(dir.y, horizontalDir.magnitude) * Mathf.Rad2Deg;
            readPitch = Mathf.Clamp(readPitch, -85f, 85f);

            //赋值给轨道内部变量
            _currentTarget = readTarget;
            _targetTarget = readTarget;
            _currentDistance = readDistance;
            _targetDistance = readDistance;
            _currentYaw = readYaw;
            _targetYaw = readYaw;
            _currentPitch = readPitch;
            _targetPitch = readPitch;

            //立刻刷新相机位置，不使用平滑
            UpdateCameraPosition(false);

            WriteLog.instance?.WriteStringToFileOld($"RestoreFromFile还原成功 pos:{transform.position}", "CameraData.txt");
        }
        catch (System.Exception e)
        {
            WriteLog.instance?.WriteStringToFileOld($"RestoreFromFile异常:{e.Message}", "CameraData.txt");
        }
    }

    /// <summary>
    /// 聚焦到指定目标点
    /// </summary>
    public void FocusOn(Vector3 point, float distance = -1)
    {
        _targetTarget = point;
        if (distance > 0)
        {
            _targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        }
    }
}

// ==================== 数据类定义 ====================

[System.Serializable]
public class CameraTransformData
{
    public Vec3 Translation;
    public RotData Rotation;
    public Vec3 Scale;
    public Vec3 TargetPoint;  // 新增：目标点
    public float Distance;     // 新增：相机距离
}

[System.Serializable]
public class Vec3
{
    public float X;
    public float Y;
    public float Z;
}

[System.Serializable]
public class RotData
{
    public float Pitch;
    public float Yaw;
    public float Roll;
}