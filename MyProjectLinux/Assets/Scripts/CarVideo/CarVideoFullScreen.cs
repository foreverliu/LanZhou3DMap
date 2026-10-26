using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class CarVideoFullScreen : MonoBehaviour
{
    const int SortingOrder = 5000;   // 盖住其他Canvas(车牌UI等)
    const string BackIconResource = "VideoBackIcon";   // Resources下的图标名(不含扩展名)
    const float BackBtnSize = 150f;
    const float BackBtnMargin = 35f;

    RawImage _source;                // 小窗的RawImage（画面来源）
    Func<bool> _canEnter;            // 当前是否允许进入全屏（有视频在播放）

    GameObject _root;                // 覆盖层根节点（Canvas）
    RawImage _fullImage;
    AspectRatioFitter _fitter;

    public bool IsFullScreen
    {
        get { return _root != null && _root.activeSelf; }
    }

    /// <summary>
    /// 初始化。source为小窗视频的RawImage；font仅在找不到图标、退回文字按钮时使用(传项目现有UI的中文字体)；
    /// canEnter决定点击时是否进入全屏。可重复调用（视频面板重建时），会重新创建覆盖层。
    /// </summary>
    public void Init(RawImage source, Font font, Func<bool> canEnter)
    {
        Exit();
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
        }

        _source = source;
        _canEnter = canEnter;
        if (_source == null) return;

        if (EventSystem.current == null)
        {
            WriteLog.instance?.WriteStringToFileOld("场景里没有EventSystem，视频点击全屏不可用", "Video.txt");
        }

        // 小窗：接收点击
        _source.raycastTarget = true;
        ClickRelay relay = _source.GetComponent<ClickRelay>();
        if (relay == null) relay = _source.gameObject.AddComponent<ClickRelay>();
        relay.onClick = OnSourceClick;

        BuildOverlay(font);
    }

    public void Enter()
    {
        if (_root == null || _source == null) return;
        SyncImage();
        _root.SetActive(true);
    }

    public void Exit()
    {
        if (_root != null && _root.activeSelf)
        {
            _root.SetActive(false);
        }
    }

    void OnSourceClick()
    {
        if (IsFullScreen) return;
        if (_canEnter != null && !_canEnter()) return;
        Enter();
    }

    void Update()
    {
        if (!IsFullScreen) return;

        if (_source == null)
        {
            Exit();
            return;
        }
        SyncImage();
    }

    // 同步小窗的画面到全屏（贴图可能被插件重建，所以每帧检查引用；颜色同步用来处理"无画面时黑底"）
    void SyncImage()
    {
        Texture tex = _source.texture;
        if (_fullImage.texture != tex)
        {
            _fullImage.texture = tex;
        }
        _fullImage.color = _source.color;
        _fullImage.uvRect = _source.uvRect;

        if (tex != null && tex.height > 0)
        {
            float ratio = (float)tex.width / tex.height;
            if (!Mathf.Approximately(_fitter.aspectRatio, ratio))
            {
                _fitter.aspectRatio = ratio;
            }
        }
    }

    // ===================== 构建覆盖层 =====================

    void BuildOverlay(Font font)
    {
        // 根：独立Canvas
        _root = new GameObject("CarVideoFullScreenCanvas");
        _root.transform.SetParent(transform, false);

        Canvas canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _root.AddComponent<GraphicRaycaster>();

        // 黑色背景（点击背景/画面退出全屏）
        GameObject bg = new GameObject("Bg", typeof(RectTransform));
        bg.transform.SetParent(_root.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        Image bgImage = bg.AddComponent<Image>();
        bgImage.color = Color.black;
        bgImage.raycastTarget = true;
        bg.AddComponent<ClickRelay>().onClick = Exit;

        // 视频画面（保持比例，居中）
        GameObject video = new GameObject("Video", typeof(RectTransform));
        video.transform.SetParent(bg.transform, false);
        Stretch(video.GetComponent<RectTransform>());
        _fullImage = video.AddComponent<RawImage>();
        _fullImage.raycastTarget = false;   // 点击穿透到背景，由背景处理退出
        _fitter = video.AddComponent<AspectRatioFitter>();
        _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _fitter.aspectRatio = 16f / 9f;

        BuildBackButton(font);

        _root.SetActive(false);
    }

    // 右下角返回按钮：有图标时图标本身就是按钮(200x200，无文字)；没有图标时退回文字按钮
    void BuildBackButton(Font font)
    {
        GameObject btn = new GameObject("BackBtn", typeof(RectTransform));
        btn.transform.SetParent(_root.transform, false);
        RectTransform btnRt = btn.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(1, 0);
        btnRt.anchorMax = new Vector2(1, 0);
        btnRt.pivot = new Vector2(1, 0);
        btnRt.anchoredPosition = new Vector2(-BackBtnMargin, BackBtnMargin);
        btnRt.sizeDelta = new Vector2(BackBtnSize, BackBtnSize);

        Image btnImage = btn.AddComponent<Image>();
        Button button = btn.AddComponent<Button>();
        button.targetGraphic = btnImage;
        button.onClick.AddListener(Exit);

        Sprite icon = LoadBackIcon();
        if (icon != null)
        {
            btnImage.sprite = icon;
            btnImage.color = Color.white;
            btnImage.preserveAspect = true;
            return;
        }

        // 找不到图标：深色底 + "返回"文字
        WriteLog.instance?.WriteStringToFileOld("找不到返回按钮图标 Resources/" + BackIconResource + "，使用文字按钮", "Video.txt");
        btnImage.color = new Color(0f, 0f, 0f, 0.6f);

        GameObject label = new GameObject("Text", typeof(RectTransform));
        label.transform.SetParent(btn.transform, false);
        Stretch(label.GetComponent<RectTransform>());
        Text text = label.AddComponent<Text>();
        text.text = "返回";
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontSize = 40;
        text.raycastTarget = false;
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    /// <summary>
    /// 从Resources加载返回按钮图标。优先按Sprite加载；图片导入类型不是Sprite时，按Texture2D加载再转成Sprite。
    /// </summary>
    static Sprite LoadBackIcon()
    {
        Sprite sprite = Resources.Load<Sprite>(BackIconResource);
        if (sprite != null) return sprite;

        Texture2D tex = Resources.Load<Texture2D>(BackIconResource);
        if (tex != null)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        return null;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 把UI点击转成回调（挂在需要响应点击的Graphic上）
    /// </summary>
    public class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (onClick != null) onClick();
        }
    }
}
