using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// 主线程任务调度器：安全将子线程任务切换到Unity主线程执行
/// 优化点：健壮单例、批量处理任务、异常隔离、线程安全强化、自动清理
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    // 单例实例（懒加载+线程安全）
    private static MainThreadDispatcher _instance;
    // 待执行的主线程任务队列
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    // 独立锁对象（语义更清晰，避免锁队列本身）
    private static readonly object _queueLock = new object();
    // 标记是否正在处理任务（避免同一帧重复进入处理逻辑）
    private bool _isProcessing = false;
    // 每帧最大处理任务数（防止单次处理过多任务导致主线程卡顿）
    [Tooltip("每帧最大处理任务数，避免主线程卡顿")]
    public int maxTasksPerFrame = 50;

    /// <summary>
    /// 全局单例访问入口（线程安全）
    /// </summary>
    public static MainThreadDispatcher Instance
    {
        get
        {
            // 如果实例为空，自动创建GameObject挂载（避免手动挂载遗漏）
            if (_instance == null)
            {
                lock (_queueLock)
                {
                    if (_instance == null)
                    {
                        GameObject dispatcherObj = new GameObject("[MainThreadDispatcher]");
                        _instance = dispatcherObj.AddComponent<MainThreadDispatcher>();
                        DontDestroyOnLoad(dispatcherObj); // 跨场景保留
                    }
                }
            }
            return _instance;
        }
    }

    private void Awake()
    {
        // 处理重复挂载：如果已有实例，销毁当前对象
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 初始化单例并标记为 DontDestroyOnLoad
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        // 队列无任务 或 正在处理 → 直接返回
        if (_isProcessing || _executionQueue.Count == 0) return;

        try
        {
            _isProcessing = true;
            int processedCount = 0;

            // 批量处理任务：每帧处理最多 maxTasksPerFrame 个，避免卡顿
            while (_executionQueue.Count > 0 && processedCount < maxTasksPerFrame)
            {
                Action action = null;
                // 加锁取出任务（仅在取任务时加锁，减少锁持有时间）
                lock (_queueLock)
                {
                    if (_executionQueue.Count > 0)
                    {
                        action = _executionQueue.Dequeue();
                    }
                }

                // 执行任务（单独try-catch，避免单个任务异常影响其他任务）
                if (action != null)
                {
                    try
                    {
                        action.Invoke();
                    }
                    catch (Exception ex)
                    {
                        WriteLog.instance?.WriteStringToFileOld($"主线程执行任务时发生异常：{ex.Message}\n{ex.StackTrace}", "MainError.txt");
                    }
                }

                processedCount++;
            }
        }
        finally
        {
            // 无论是否抛出异常，都标记为处理完成（核心修复：避免任务卡死）
            _isProcessing = false;
        }
    }

    /// <summary>
    /// 子线程调用：将任务加入主线程执行队列
    /// </summary>
    /// <param name="action">要执行的任务（不能为null）</param>
    public static void Enqueue(Action action)
    {
        if (action == null)
        {
            WriteLog.instance?.WriteStringToFileOld("尝试将空任务加入主线程队列，已忽略", "MainError.txt");
            return;
        }

        // 加锁入队（线程安全，支持多线程同时提交任务）
        lock (_queueLock)
        {
            _executionQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// 清空所有待执行任务（场景切换/程序退出时调用）
    /// </summary>
    public static void ClearQueue()
    {
        lock (_queueLock)
        {
            _executionQueue.Clear();
        }
        Debug.Log("主线程任务队列已清空");
    }

    /// <summary>
    /// 程序退出/对象销毁时清理
    /// </summary>
    private void OnDestroy()
    {
        // 仅当当前是有效实例时，清空队列
        if (_instance == this)
        {
            ClearQueue();
            _instance = null;
        }
    }

    /// <summary>
    /// 检查是否在主线程（辅助方法）
    /// </summary>
    /// <returns>true=当前是主线程，false=子线程</returns>
    public static bool IsMainThread()
    {
        return Thread.CurrentThread.ManagedThreadId == 1; // Unity主线程ID通常为1
    }
}