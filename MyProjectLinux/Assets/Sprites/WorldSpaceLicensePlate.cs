using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WorldSpaceLicensePlate : MonoBehaviour
{
    [Header("跟随的车辆transform")]
    public Transform carTarget;
    [Header("车顶向上偏移")]
    public Vector3 offset = new Vector3(0, 3f, 0);
    [Header("俯视相机")]
    public Camera targetCam;

    [Header("缩放参数")]
    [Tooltip("在相机距离为50时的基准大小")]
    public float sizeAtDistance50 = 0.5f;
    [Tooltip("最小缩放（最近距离）")]
    public float minScale = 0.02f;
    [Tooltip("最大缩放（最远距离）")]
    public float maxScale = 0.3f;
    [Tooltip("缩放曲线指数（1=线性，越大远处放大越快）")]
    public float falloffExponent = 1.2f;

    [Header("摄像机切换")]
    [Tooltip("车辆上的摄像机（从车辆预制体获取）")]
    public Camera vehicleCamera;
    [Tooltip("摄像机切换动画时间")]
    public float switchDuration = 1.5f;

    private Transform _camTf;
    private RectTransform _rt;
    private Vector3 _originLocalScale;
    private float _referenceDistance = 50f;

    public Button plateBtn;
    public Text plateNum;
    public string CarIp;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _originLocalScale = _rt.localScale;

        if (targetCam == null)
        {
            targetCam = Camera.main;
            if (targetCam == null)
            {
                targetCam = GameObject.Find("MainCamera")?.GetComponent<Camera>();
            }
        }

        if (targetCam != null)
        {
            _camTf = targetCam.transform;
        }

        plateBtn.onClick.AddListener(OnPointerClick);
    }

    void LateUpdate()
    {
        if (carTarget == null || _camTf == null) return;

        // ========== 1. 位置跟随 ==========
        transform.position = carTarget.position + offset;

        // ========== 2. 旋转：让UI始终面向相机 ==========
        transform.LookAt(_camTf);
        transform.rotation = transform.rotation * Quaternion.Euler(0, 180, 0);

        // ========== 3. 缩放：越远越大，越近越小 ==========
        float distance = Vector3.Distance(transform.position, _camTf.position);

        // 核心公式：距离越远，缩放越大
        // 当 distance = 50 时，缩放 = sizeAtDistance50
        // 当 distance > 50 时，缩放 > sizeAtDistance50（放大）
        // 当 distance < 50 时，缩放 < sizeAtDistance50（缩小）
        float normalizedDistance = distance / _referenceDistance;
        float scaleFactor = sizeAtDistance50 * Mathf.Pow(normalizedDistance, falloffExponent);

        // 限制缩放范围
        scaleFactor = Mathf.Clamp(scaleFactor, minScale, maxScale);
        _rt.localScale = _originLocalScale * scaleFactor;

        // 调试输出（可选）
        // Debug.Log($"距离: {distance:F1}, 缩放: {scaleFactor:F2}");
    }

    // UGUI点击
    public void OnPointerClick()
    {
        Debug.Log($"点击车牌，车辆:{carTarget?.name ?? "null"}");
        SwitchToVehicleCamera();
    }

    /// <summary>
    /// 切换到车辆摄像机
    /// </summary>
    private void SwitchToVehicleCamera()
    {
        if (vehicleCamera == null)
        {
            Debug.Log("SwitchToVehicleCamera vehicleCamera is NULL");
            return;
        }
            
        // 启用车辆摄像机
        vehicleCamera.gameObject.SetActive(true);
        MainUI.instance.vehicleCamera = vehicleCamera;
        
        if (UDPControl.instance.ipPlateObjDict.ContainsKey(CarIp))
        {
            UDPControl.instance.ipPlateObjDict[CarIp].SetActive(false);
            MainUI.instance.currentPlateObj = UDPControl.instance.ipPlateObjDict[CarIp];
        }

        MainUI.instance._mainCamera.gameObject.SetActive(false);
        // 不立即禁用主摄像机，等动画完成后再切换
    }

    public void setPlateNum(string plate)
    {
        plateNum.text = plate;
    }
}