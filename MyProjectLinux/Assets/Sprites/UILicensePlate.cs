using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UILicensePlate : MonoBehaviour
{
    [Header("跟随车辆")]
    public Transform carTarget;

    [Header("3D车顶偏移(世界空间)")]
    public Vector3 worldOffset = new Vector3(0, 3.0f, 0);

    [Header("主相机")]
    public Camera mainCam;

    [Header("缩放参数")]
    [Tooltip("距离50米时的基准UI尺寸系数")]
    public float baseScaleAt50 = 1.0f;
    public float minScale = 0.2f;
    public float maxScale = 2.5f;
    public float falloffExponent = 1.2f;

    [Header("超出屏幕自动隐藏")]
    public bool hideWhenOutOfScreen = true;
    public float screenBorderMargin = 30f;

    [Header("车内相机")]
    public Camera vehicleCamera;
    public Camera MiniCamera;

    public Button plateBtn;
    public Text plateNum;
    public string CarIp;

    private RectTransform _rt;
    private Canvas _parentCanvas;
    private Vector2 _originLocalScale;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _originLocalScale = _rt.localScale;
        _parentCanvas = GetComponentInParent<Canvas>();

        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null)
            {
                mainCam = GameObject.Find("MainCamera")?.GetComponent<Camera>();
            }
        }

        if (plateBtn != null)
        {
            plateBtn.onClick.AddListener(OnPlateClick);
        }
    }

    // Start is called before the first frame update
    void LateUpdate()
    {
        if (carTarget == null || mainCam == null || _parentCanvas == null)
        {
            Debug.Log("carTarget::" + carTarget + "   mainCam::" + mainCam+ "   _parentCanvas::"+ _parentCanvas);
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        // 1.车辆车顶世界坐标
        Vector3 worldPoint = carTarget.position + worldOffset;

        // 2.世界坐标转屏幕坐标
        Vector3 screenPos = mainCam.WorldToScreenPoint(worldPoint);

        // 物体在相机背面，直接隐藏
        if (screenPos.z < 0)
        {
            if (hideWhenOutOfScreen)
            {
                Debug.Log("screenPos.zscreenPos.z:::"+screenPos.z);
                gameObject.SetActive(false);
            }
            return;
        }


        // 4.转换为Overlay画布anchoredPosition
        RectTransform canvasRt = _parentCanvas.GetComponent<RectTransform>();
        Vector2 uiPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt,
            new Vector2(screenPos.x, screenPos.y),
            null,
            out uiPos);

        _rt.anchoredPosition = uiPos;

        // 5.距离缩放
        float distance = Vector3.Distance(mainCam.transform.position, worldPoint);
        float normalizedDist = distance / 50f;
        float scale = baseScaleAt50 * Mathf.Pow(normalizedDist, falloffExponent);
        scale = Mathf.Clamp(scale, minScale, maxScale);

        _rt.localScale = _originLocalScale * scale;
    }

    private void OnPlateClick()
    {
        Debug.Log($"点击车牌，车辆:{carTarget?.name}  IP:{CarIp}");
        SwitchToVehicleCamera();

        CarListUIManager.instance.currentClickIp = CarIp;
        CarListUIManager.instance.showCarInfo();
    }

    private void SwitchToVehicleCamera()
    {
        if (vehicleCamera == null)
        {
            Debug.LogError("vehicleCamera == null");
            return;
        }

        CarListUIManager.instance.HideAllPlateObj();

        if (MainUI.instance.vehicleCamera != null)
        {//上一个车
            MainUI.instance.vehicleCamera.gameObject.SetActive(false);
            MainUI.instance.vehicleMiniCamera.gameObject.SetActive(false);
        }

        vehicleCamera.gameObject.SetActive(true);
        MiniCamera.gameObject.SetActive(true);

        MainUI.instance.vehicleCamera = vehicleCamera;
        MainUI.instance.vehicleMiniCamera = MiniCamera;

        if (UDPControl.instance != null && UDPControl.instance.ipPlateObjDict.ContainsKey(CarIp))
        {
            UDPControl.instance.ipPlateObjDict[CarIp].SetActive(false);
            MainUI.instance.currentPlateObj = UDPControl.instance.ipPlateObjDict[CarIp];
        }

        MainUI.instance._mainCamera.gameObject.SetActive(false);
    }

    public void setPlateNum(string plate)
    {
        if (plateNum != null)
            plateNum.text = plate;
    }
}
