using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class GeoOffsetCalculator : MonoBehaviour
{
    private int isHorizontal = 180;
    // 先在类内声明Rigidbody引用（建议在Awake中初始化）
    public Rigidbody carRigidbody;

    // ========== WheelCollider配置 ==========
    // 挂载车轮碰撞器的GameObject（需在Inspector中赋值：左前、右前、左后、右后）
    public List<GameObject> wheelGameObjects = new List<GameObject>();
    // 车轮碰撞器引用
    private List<WheelCollider> wheelColliders = new List<WheelCollider>();
    // 碰撞日志开关
    public bool isPrintWheelCollisionLog = true;
    // 日志打印间隔（避免刷屏）
    private float logInterval = 0.1f;
    private float lastLogTime;

    // 核心状态：标记是否是第一帧数据
    private bool _isFirstDataReceived = false;
    // 可选：记录上一帧的目标位置，用于更精准的速度计算
    private Vector3 _lastTargetPosition;

    [Header("坡道地面贴合")]
    public float groundRayDistance = 20f;  // 射线长度（大于车高）
    public LayerMask groundLayer;           // 地面/地形 Layer
    private float _groundY;

    public float currentPosX;
    public float currentPosY;
    public float currentPosZ;
    public float currentAngle;

    private float _smoothGroundY;
    private const float MoveDeadZone = 0.03f;    // 位移死区，小于该距离视为静止
    private const float AngleDeadZone = 1f;    // 角度死区，小于该角度不修正转向
    private const float VelocityLerpFactor = 12f; // 速度平滑系数，降低与车轮物理冲突
    private const float MaxTurnSpeed = 300f;     // 降低最大转向角速度，抑制左右摆动

    private GpsTargetFrame _latestGpsFrame;

    private void Awake()
    {
        initCarRigidbody();
    }
    void Start()
    {

    }

    public void initCarRigidbody()
    {
        if (carRigidbody == null)
        {
            carRigidbody = GetComponent<Rigidbody>();
            if (carRigidbody == null)
            {
                carRigidbody = gameObject.AddComponent<Rigidbody>();
            }
        }

        carRigidbody.useGravity = true;
        carRigidbody.isKinematic = false;
        carRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        Debug.Log($"[{gameObject.name}] 刚体初始化完成");
    }

    public void CarMove(string data)
    {
        try
        {
            if (carRigidbody == null)
            {
                Debug.LogWarning("Car或Rigidbody为空，跳过移动");
                return;
            }

            string[] values = data.Split(',');
            if (values.Length < 6)
            {
                Debug.LogWarning("GPS数据长度不足：" + data);
                return;
            }

            double x = 0;
            double y = 0;
            double h = 0;
            float rotation = 0;
            if (!double.TryParse(values[2], out x) ||
                !double.TryParse(values[3], out y) ||
                !double.TryParse(values[4], out h) ||
                !float.TryParse(values[5], out rotation))
            {
                WriteLog.instance?.WriteStringToFileOld("GPS数据解析失败：" + data, "CheZai.txt");
                return;
            }

            Vector3 convertedCoordinates = UDPControl.instance.ConvertToXZ(x, y);
            float finalYaw = rotation + isHorizontal;

            // 仅仅存入内存，不执行任何物理操作
            _latestGpsFrame = new GpsTargetFrame
            {
                valid = true,
                targetPos = convertedCoordinates,
                targetYaw = finalYaw,
                rawGpsH = h
            };

            /*
                        // ========== 新：射线取地面真实高度，贴合坡道 ==========
                        Vector3 rayOrigin = new Vector3(convertedCoordinates.x, carRigidbody.position.y + 10f, convertedCoordinates.z);

                        float rawGroundY = carRigidbody.position.y;
                        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance, groundLayer))
                        {
                            //    Debug.DrawRay(rayOrigin, Vector3.down * groundRayDistance, Color.red, Time.deltaTime);
                            rawGroundY = hit.point.y;
                        }

                        // 平滑层：只处理坡道，永远只使用原始地面高度，无偏移、无跳变
                        _groundY = rawGroundY;
                        if (!_isFirstDataReceived)
                        {
                            // 第一帧直接用原始地面高度，不插值
                            _smoothGroundY = rawGroundY;
                        }
                        else
                        {
                            // 后续帧再平滑
                            _smoothGroundY = Mathf.Lerp(_smoothGroundY, _groundY, 0.2f);
                        }

                        float baseY = _smoothGroundY;

                        // 地底兜底判断：车身低于地面，直接抬高最终目标位置
                        float wheelOffset = 0.5f;
                        float finalY;
                        if (carRigidbody.position.y < rawGroundY)
                        {
                            finalY = baseY + wheelOffset;
                            WriteLog.instance?.WriteStringToFileOld($"车辆在地底，目标高度抬高{wheelOffset}米，地面Y:{rawGroundY}", "CheZai.txt");
                        }
                        else
                        {
                            // 正常在地面，车轮贴合地面，不额外抬高
                            finalY = baseY;
                        }

                        Vector3 targetPosition = new Vector3(
                            convertedCoordinates.x,
                            finalY,
                            convertedCoordinates.z
                        );

                        // ========== 核心优化：第一帧瞬移，后续平滑 ==========
                        if (!_isFirstDataReceived)
                        {
                            // 第一帧数据：直接设置位置，无平滑
                            carRigidbody.MovePosition(targetPosition);
                            carRigidbody.velocity = Vector3.zero; // 重置速度，避免初始瞬移后有残留速度
                            carRigidbody.angularVelocity = Vector3.zero;

                            float finalYRotation = rotation + isHorizontal;
                            // =========新增坡道对齐====防止坡道生成车辆导致的翻车=====
                            Vector3 roofRay = targetPosition + Vector3.up * 1.2f;
                            if (Physics.Raycast(roofRay, Vector3.down, out RaycastHit firstHit, groundRayDistance, groundLayer))
                            {
                                // 用坡面法线构建车身姿态：保证车身贴合斜坡
                                Quaternion slopeAlignRot = Quaternion.FromToRotation(Vector3.up, firstHit.normal);
                                // 融合GPS航向 + 坡面倾斜，不水平硬放
                                Quaternion yawRot = Quaternion.Euler(0, finalYRotation, 0);
                                Quaternion initRotation = slopeAlignRot * yawRot;

                                carRigidbody.MoveRotation(initRotation);
                            }
                            else
                            {
                                // 第一帧直接设置旋转，无插值
                                Quaternion initRotation = Quaternion.Euler(
                                    carRigidbody.rotation.eulerAngles.x,
                                    finalYRotation,
                                    carRigidbody.rotation.eulerAngles.z
                                );
                                carRigidbody.MoveRotation(initRotation);
                            }

                            WriteLog.instance?.WriteStringToFileOld($"第一帧GPS数据，车辆瞬移到目标位置：{targetPosition}，旋转：{finalYRotation}°", "CheZai.txt");
                            _isFirstDataReceived = true; // 标记为已接收第一帧
                            _lastTargetPosition = targetPosition; // 记录初始位置

                            currentPosX = targetPosition.x;
                            currentPosY = targetPosition.y;
                            currentPosZ = targetPosition.z;

                            currentAngle = finalYRotation;
                            return; // 第一帧无需执行后续平滑逻辑
                        }

                        // ========== 1. 速度计算（保留原有逻辑，适配200ms数据） ==========
                        float dataInterval = 0.2f;
                        Vector3 direction = targetPosition - carRigidbody.position;
                        float posDiff = direction.magnitude;
                        Vector3 targetVelocity = direction / dataInterval;

                        // 限制最大速度（可适当提高，匹配转向速度）
                        float maxSpeed = 30f; // 恢复到30m/s（108km/h）
                        if (targetVelocity.magnitude > maxSpeed)
                        {
                            targetVelocity = targetVelocity.normalized * maxSpeed;
                        }
                        // 速度轻微平滑（保留一点点缓冲，避免瞬移）
                        // 小于死区直接锁死速度
                        if (posDiff < MoveDeadZone)
                        {
                            carRigidbody.velocity = Vector3.zero;
                        }
                        else
                        {
                            // 弱化插值系数，减少与WheelCollider物理拉扯抖动
                            carRigidbody.velocity = Vector3.Lerp(carRigidbody.velocity, targetVelocity, Time.fixedDeltaTime * VelocityLerpFactor);
                        }
                        WriteLog.instance?.WriteStringToFileOld($"CarMoveCarMove：{targetVelocity}", "GPS.txt");

                        currentPosX = carRigidbody.position.x;
                        currentPosY = carRigidbody.position.y;
                        currentPosZ = carRigidbody.position.z;

                        //// ========== 2. 旋转逻辑优化（核心：快速转向+匹配运动方向） ==========
                        float finalRotation = rotation + isHorizontal;

                        // 快速转向：仅保留极短的平滑时间（0.01秒），几乎无延迟
                        float currentYRotation = carRigidbody.rotation.eulerAngles.y;
                        // 计算角度差（自动处理360度循环，如350°→10°只转20°）
                        float angleDiff = Mathf.DeltaAngle(currentYRotation, finalRotation);
                        float angleDiffAbs = Mathf.Abs(angleDiff);

                        //    JavaCallbackExample.instance?.WriteStringToFileOld($"车辆旋转;currentYRotation::{currentYRotation}，angleDiff:{angleDiff};angleDiffAbs:{angleDiffAbs}", "CheZaiRotation.txt");
                        // 小于角度死区，不修正转向，同时衰减旋转角速度
                        if (angleDiffAbs < AngleDeadZone)
                        {
                            Vector3 angularVel = carRigidbody.angularVelocity;
                            angularVel.y *= 0.01f;
                            carRigidbody.angularVelocity = angularVel;
                            currentAngle = currentYRotation;
                            WriteLog.instance?.WriteStringToFileOld($"车辆旋转111111;currentYRotation::{currentYRotation}", "CheZaiRotation.txt");
                        }
                        else
                        {
                            // 限制最大转向角速度（可调整，越大转向越快）
                            //float maxTurnSpeed = 500f; // 最大转向速度（500度/秒，足够快速响应）
                            float targetAngleStep = Mathf.Clamp(angleDiff, -MaxTurnSpeed * Time.fixedDeltaTime, MaxTurnSpeed * Time.fixedDeltaTime);
                            float newYRotation = currentYRotation + targetAngleStep;

                            // 物理驱动旋转（无插值，直接转向目标角度）
                            Quaternion targetRotation = Quaternion.Euler(
                                carRigidbody.rotation.eulerAngles.x,
                                newYRotation,
                                carRigidbody.rotation.eulerAngles.z
                            );
                            carRigidbody.MoveRotation(targetRotation);

                            currentAngle = newYRotation;

                            WriteLog.instance?.WriteStringToFileOld($"车辆旋转22222222222;currentYRotation::{currentAngle}", "CheZaiRotation.txt");
                        }
            */
        }
        catch (FormatException ex)
        {
            WriteLog.instance?.WriteStringToFileOld("CarMove格式错误: " + ex, "CuoWu.txt");
        }
        catch (Exception ex)
        {
            WriteLog.instance?.WriteStringToFileOld("CarMove发生错误: " + ex, "CuoWu.txt");
        }
    }

    private void FixedUpdate()
    {
        if (!_latestGpsFrame.valid || carRigidbody == null)
            return;

        GpsTargetFrame frame = _latestGpsFrame;
        _latestGpsFrame.valid = false;
        Vector3 convertedCoordinates = frame.targetPos;
        float finalYRotation = frame.targetYaw;

        Vector3 rayOrigin = new Vector3(convertedCoordinates.x, carRigidbody.position.y + 10f, convertedCoordinates.z);
        float rawGroundY = carRigidbody.position.y;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance, groundLayer))
        {
            rawGroundY = hit.point.y;
        }
        _groundY = rawGroundY;
        if (!_isFirstDataReceived)
        {
            _smoothGroundY = rawGroundY;
        }
        else
        {
            _smoothGroundY = Mathf.Lerp(_smoothGroundY, _groundY, 0.2f);
        }
        float baseY = _smoothGroundY;
        float wheelOffset = 0.5f;
        float finalY;
        if (carRigidbody.position.y < rawGroundY)
        {
            finalY = baseY + wheelOffset;
            WriteLog.instance?.WriteStringToFileOld($"车辆在地底，目标高度抬高{wheelOffset}米，地面Y:{rawGroundY}", "CheZai.txt");
        }
        else
        {
            finalY = baseY;
        }

        Vector3 targetPosition = new Vector3(convertedCoordinates.x, finalY, convertedCoordinates.z);

        if (!_isFirstDataReceived)
        {
            //第一帧瞬移
            carRigidbody.MovePosition(targetPosition);
            carRigidbody.velocity = Vector3.zero;
            carRigidbody.angularVelocity = Vector3.zero;

            Vector3 roofRay = targetPosition + Vector3.up * 1.2f;
            if (Physics.Raycast(roofRay, Vector3.down, out RaycastHit firstHit, groundRayDistance, groundLayer))
            {
                Quaternion slopeAlignRot = Quaternion.FromToRotation(Vector3.up, firstHit.normal);
                Quaternion yawRot = Quaternion.Euler(0, finalYRotation, 0);
                Quaternion initRotation = slopeAlignRot * yawRot;
                carRigidbody.MoveRotation(initRotation);
            }
            else
            {
                Quaternion initRotation = Quaternion.Euler(
                    carRigidbody.rotation.eulerAngles.x,
                    finalYRotation,
                    carRigidbody.rotation.eulerAngles.z
                );
                carRigidbody.MoveRotation(initRotation);
            }

            WriteLog.instance?.WriteStringToFileOld($"第一帧GPS数据，车辆瞬移到目标位置：{targetPosition}，旋转：{finalYRotation}°", "CheZai.txt");
            _isFirstDataReceived = true;
            _lastTargetPosition = targetPosition;
            currentPosX = targetPosition.x;
            currentPosY = targetPosition.y;
            currentPosZ = targetPosition.z;
            currentAngle = finalYRotation;
            return;
        }

        //=====【改动重点】后续帧：不再使用velocity，改用MovePosition平滑插值到GPS目标点=====
        float posLerpSpeed = 8f; // 位置跟随系数，越大跟踪越快，6‑12之间调
        Vector3 smoothTargetPos = Vector3.Lerp(carRigidbody.position, targetPosition, Time.fixedDeltaTime * posLerpSpeed);

        // 处理姿态：坡道法线 + GPS航向
        Quaternion targetRot;
        Vector3 roofRay2 = smoothTargetPos + Vector3.up * 1.2f;
        if (Physics.Raycast(roofRay2, Vector3.down, out RaycastHit hit2, groundRayDistance, groundLayer))
        {
            Quaternion slopeAlignRot = Quaternion.FromToRotation(Vector3.up, hit2.normal);
            Quaternion yawRot = Quaternion.Euler(0, finalYRotation, 0);
            targetRot = slopeAlignRot * yawRot;
        }
        else
        {
            targetRot = Quaternion.Euler(carRigidbody.rotation.eulerAngles.x, finalYRotation, carRigidbody.rotation.eulerAngles.z);
        }

        // FixedUpdate内调用MovePosition / MoveRotation
        carRigidbody.MovePosition(smoothTargetPos);
        carRigidbody.MoveRotation(targetRot);

        // 清零速度，避免WheelCollider物理干扰GPS跟踪
        carRigidbody.velocity = Vector3.zero;
        carRigidbody.angularVelocity = Vector3.zero;

        currentPosX = carRigidbody.position.x;
        currentPosY = carRigidbody.position.y;
        currentPosZ = carRigidbody.position.z;
        currentAngle = finalYRotation;

        WriteLog.instance?.WriteStringToFileOld($"GPS目标:{targetPosition},实际:{carRigidbody.position}", "GPS.txt");
    }

    /// <summary>
    /// 重置状态（如重新开始、车辆复位时调用）
    /// </summary>
    public void ResetCarState()
    {
        _isFirstDataReceived = false;
        _lastTargetPosition = Vector3.zero;
        if (carRigidbody != null)
        {
            carRigidbody.velocity = Vector3.zero;
            carRigidbody.angularVelocity = Vector3.zero;
        }
        WriteLog.instance?.WriteStringToFileOld("车辆状态已重置，下次接收数据将执行首次瞬移", "CheZai.txt");
    }

    [Serializable]
    private struct GpsTargetFrame
    {
        public bool valid;
        public Vector3 targetPos;
        public float targetYaw;
        public double rawGpsH;
    }
}

