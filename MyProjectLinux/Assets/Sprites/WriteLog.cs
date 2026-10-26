using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class WriteLog : MonoBehaviour
{
    public static WriteLog instance;

    //===============================================缓存写入====================
    // 1. 批量缓存相关字段（类级别定义）
    private readonly Queue<LogItem> _logCache = new Queue<LogItem>(); // 替换为强类型缓存项
    private readonly object _logCacheLock = new object(); // 缓存专属锁
    private DateTime _lastLogFlushTime = DateTime.Now; // 改用DateTime，支持跨线程
    private TimeSpan _logFlushInterval = TimeSpan.FromMilliseconds(500); // 10秒间隔
    private const int MAX_CACHE_COUNT = 1000; // 缓存上限，防止内存溢出
                                              // 目录创建缓存（原有）
    private readonly Dictionary<string, Task> _directoryCreationTasks = new Dictionary<string, Task>();
    private readonly object _directoryLock = new object();
    // 写入队列（原有）
    private readonly HashSet<string> _processingQueues = new HashSet<string>();

    // 1. 为每个文件维护“写入任务队列”和“队列锁”
    private readonly Dictionary<string, Queue<Func<Task>>> _writeQueues = new Dictionary<string, Queue<Func<Task>>>();
    private readonly object _queueLock = new object();

    // 1. 缓存键：{目录}|{基础文件名}|{扩展名} → 唯一标识一个"文件组"（如"Log/2024-09-17|UserLog|.txt"）
    // 缓存值：该文件组当前可用的文件路径（避免每次遍历）
    private readonly Dictionary<string, string> _activeFilePathCache = new Dictionary<string, string>();
    // 2. 保护缓存的锁（多线程调用时确保缓存读写安全）
    private readonly object _cacheLock = new object();
    // 3. 日志文件大小限制（可统一配置，避免重复计算）
    private long _maxFileSizeBytes = 2 * 1024 * 1024; // 默认2MB，后续会从配置更新
                                                      // 新增：最大遍历文件数（避免死循环，可根据需求调整）
    private const int MAX_TRAVERSE_COUNT = 200;

    // 新增：日志缓存项（强类型，避免字符串分割的坑）
    private class LogItem
    {
        public string Content { get; set; }
        public string Name { get; set; }
        public string Time { get; set; } // 记录日志产生时间，而非刷盘时间
    }

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

    }


    /// <summary>
    /// 嵌入批量缓存逻辑后的日志写入方法
    /// </summary>
    /// <param name="content">日志内容</param>
    /// <param name="name">日志文件名</param>
    public void WriteStringToFileOld(string content, string name)
    {
        try
        {
            //    Debug.Log(content+"    "+ name);

            // 第一步：入参校验（原有逻辑保留）
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(name))
            {
                Debug.LogError("WriteStringToFileAsync content 或 name 不能为空");
                return;
            }
            if (string.IsNullOrEmpty(UDPControl.instance.AndroPath))
            {
                Debug.LogError("WriteStringToFileAsync AndroPath 未初始化:content::" + content + "   name::" + name);
                return;
            }

            // 第二步：将日志加入内存缓存（核心修改点1）
            var logItem = new LogItem
            {
                Content = content,
                Name = name,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") // 记录日志产生时间
            };

            lock (_logCacheLock)
            {
                // 缓存上限保护：超过1000条立即刷盘，避免内存溢出
                if (_logCache.Count >= MAX_CACHE_COUNT)
                {
                    Debug.LogWarning($"日志缓存达到上限({MAX_CACHE_COUNT})，立即刷盘");
                    FlushLogCache();
                }
                _logCache.Enqueue(logItem);
            }

            // 修改刷盘间隔检查逻辑
            if (DateTime.Now - _lastLogFlushTime >= _logFlushInterval)
            {
                FlushLogCache();
                _lastLogFlushTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"日志入缓存失败: {ex.Message}\n堆栈: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 批量刷盘核心方法（嵌入原有异步逻辑）
    /// </summary>
    private void FlushLogCache()
    {
        // 拷贝局部副本，杜绝跨线程篡改
        string localAndroPath = UDPControl.instance.AndroPath;
        if (string.IsNullOrEmpty(localAndroPath))
        {
            return;
        }

        // 第一步：取出所有缓存的日志（一次性锁定，减少锁持有时间）
        List<LogItem> logsToFlush = new List<LogItem>();
        lock (_logCacheLock)
        {
            if (_logCache.Count == 0) return;

            // 一次性取出所有缓存项，释放锁
            logsToFlush.AddRange(_logCache);
            _logCache.Clear();
        }

        // 第二步：按日期分组（避免重复创建同一目录）
        var logsByDate = logsToFlush.GroupBy(l =>
        {
            // 从日志时间解析日期（而非当前时间），保证日志归属正确
            if (DateTime.TryParseExact(l.Time.Split(' ')[0], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime logDate))
            {
                return logDate.ToString("yyyy-MM-dd");
            }
            return DateTime.Now.ToString("yyyy-MM-dd");
        }).ToList();

        // 第三步：批量处理每组日志
        foreach (var dateGroup in logsByDate)
        {
            string today = dateGroup.Key;
            string logDirectory = Path.Combine(localAndroPath, "LinuxLog", today);

            // 1. 异步创建目录（复用原有优化逻辑，带重试）
            Task dirTask = EnsureDirectoryExistsAsync(logDirectory, 3);

            // 2. 目录创建完成后批量处理该组日志
            _ = dirTask.ContinueWith(async dirResult =>
            {
                if (dirResult.Exception != null)
                {
                    // 子线程不能直接Debug.Log，改用主线程队列
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        Debug.LogError($"创建日志目录[{today}]失败: {dirResult.Exception.InnerException?.Message}");
                    });
                    return;
                }

                // 按文件名分组（避免重复获取同一文件的路径）
                var logsByFileName = dateGroup.GroupBy(l => l.Name).ToList();
                foreach (var fileGroup in logsByFileName)
                {
                    string fileName = fileGroup.Key;
                    string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                    string fileExtension = Path.GetExtension(fileName);
                    if (string.IsNullOrEmpty(fileExtension))
                    {
                        fileExtension = ".txt";
                    }

                    // 获取该文件的可用路径（仅获取一次，复用）
                    string filePath = GetAvailableFilePath(logDirectory, baseFileName, fileExtension);
                    if (string.IsNullOrEmpty(filePath))
                    {
                        MainThreadDispatcher.Enqueue(() =>
                        {
                            Debug.LogError($"获取文件路径失败: {fileName}");
                        });
                        continue;
                    }

                    // 批量创建写入任务
                    lock (_queueLock)
                    {
                        if (!_writeQueues.ContainsKey(filePath))
                        {
                            _writeQueues[filePath] = new Queue<Func<Task>>();
                        }
                    }

                    foreach (var logItem in fileGroup)
                    {
                        // 封装写入任务（复用原有逻辑）
                        Func<Task> writeTask = async () =>
                        {
                            await WriteStringToFileAsync(logItem.Content, logItem.Time, filePath);
                        };

                        // 将任务加入队列
                        lock (_queueLock)
                        {
                            _writeQueues[filePath].Enqueue(writeTask);
                        }
                    }

                    // 启动队列消费（批量消费，减少启动次数）
                    await ProcessWriteQueue(filePath);
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion)
            .ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        Debug.LogError($"批量处理日志[{today}]异常: {t.Exception.InnerException?.Message}");
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>
    /// 异步创建目录（带重试+线程安全）
    /// </summary>
    private Task EnsureDirectoryExistsAsync(string directoryPath, int retryCount = 1)
    {
        if (_directoryCreationTasks.TryGetValue(directoryPath, out Task existingTask))
        {
            return existingTask;
        }

        lock (_directoryLock)
        {
            if (_directoryCreationTasks.TryGetValue(directoryPath, out existingTask))
            {
                return existingTask;
            }

            Task createTask = Task.Run(async () =>
            {
                int attempt = 0;
                while (attempt < retryCount)
                {
                    try
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                            Debug.Log($"成功创建日志目录: {directoryPath}");
                        }
                        return;
                    }
                    catch (Exception ex)
                    {
                        attempt++;
                        if (attempt >= retryCount)
                        {
                            throw new Exception($"创建目录失败（重试{attempt}次）: {directoryPath}", ex);
                        }
                        await Task.Delay(100);
                    }
                }
            });

            _ = createTask.ContinueWith(t =>
            {
                lock (_directoryLock)
                {
                    _directoryCreationTasks.Remove(directoryPath);
                }
                if (t.Exception != null)
                {
                    Debug.LogError($"目录创建任务异常: {t.Exception.InnerException?.Message}");
                }
            });

            _directoryCreationTasks[directoryPath] = createTask;
            return createTask;
        }
    }

    /// <summary>
    /// 消费写入队列（串行执行）
    /// </summary>
    private async Task ProcessWriteQueue(string filePath)
    {
        lock (_queueLock)
        {
            if (_processingQueues.Contains(filePath))
            {
                return;
            }
            _processingQueues.Add(filePath);
        }

        try
        {
            while (true)
            {
                Func<Task> writeTask = null;
                lock (_queueLock)
                {
                    if (_writeQueues.TryGetValue(filePath, out Queue<Func<Task>> queue) && queue.Count > 0)
                    {
                        writeTask = queue.Dequeue();
                    }
                    else
                    {
                        _writeQueues.Remove(filePath);
                        break;
                    }
                }

                if (writeTask != null)
                {
                    await writeTask();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"消费写入队列失败: {ex.Message}");
        }
        finally
        {
            lock (_queueLock)
            {
                _processingQueues.Remove(filePath);
            }
        }
    }

    /// <summary>
    /// 实际写入文件方法
    /// </summary>
    private async Task WriteStringToFileAsync(string content, string writeTime, string filePath)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"[{writeTime}] {content}{Environment.NewLine}");
        using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
        {
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }
    }

    private string GetAvailableFilePath(string directory, string baseFileName, string extension)
    {
        // 1. 基础参数校验（避免无效目录/文件名导致的后续错误）
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseFileName) || string.IsNullOrEmpty(extension))
        {
            Debug.LogError("GetAvailableFilePath: 目录、文件名前缀或扩展名不能为空");
            return null;
        }
        if (!Directory.Exists(directory))
        {
            Debug.LogError($"GetAvailableFilePath: 目录不存在 - {directory}");
            return null;
        }

        // 2. 初始化日志大小限制（默认2MB，避免配置无效）
        int logSizeMB = 2;
        logSizeMB = logSizeMB <= 0 ? 2 : logSizeMB;
        _maxFileSizeBytes = logSizeMB * 1024 * 1024; // 转换为字节（如2MB = 2*1024*1024 = 2097152字节）

        // ---------------------- 新增：缓存核心逻辑 ----------------------
        // 3. 生成当前文件组的唯一缓存键（确保不同文件组不冲突）
        string cacheKey = $"{directory}|{baseFileName}|{extension}";
        // 4. 优先检查缓存：99%的场景走这里，无需遍历文件
        lock (_cacheLock)
        {
            // 缓存存在 → 检查缓存路径是否有效（文件存在且未超限）
            if (_activeFilePathCache.TryGetValue(cacheKey, out string cachedPath))
            {
                // 校验1：缓存路径的文件是否存在（避免文件被手动删除）
                if (File.Exists(cachedPath))
                {
                    // 校验2：文件大小是否未超过限制
                    FileInfo cachedFileInfo = new FileInfo(cachedPath);
                    if (cachedFileInfo.Length < _maxFileSizeBytes)
                    {
                        // 缓存有效！直接返回，跳过遍历（耗时<1ms）
                        return cachedPath;
                    }
                    Debug.LogError($"缓存路径[{cachedPath}]已超限（{cachedFileInfo.Length / 1024 / 1024}MB），需重新查找");
                }
                else
                {
                    Debug.LogError($"缓存路径[{cachedPath}]不存在，需重新查找");
                }

                // 缓存失效（文件超限/不存在），移除旧缓存
                _activeFilePathCache.Remove(cacheKey);
            }
        }

        string targetFilePath = null;
        try
        {
            string filePath = Path.Combine(directory, $"{baseFileName}{extension}");

            // 如果文件不存在或文件大小小于限制，直接使用该文件
            if (!File.Exists(filePath) || new FileInfo(filePath).Length < _maxFileSizeBytes)
            {
                targetFilePath = filePath;
            }

            // 文件已存在且超过限制，查找下一个可用的文件
            int counter = 1;
            while (counter <= MAX_TRAVERSE_COUNT)
            {
                string numberedFilePath = Path.Combine(directory, $"{baseFileName}_{counter}{extension}");

                // 如果文件不存在或文件大小小于限制，使用该文件
                if (!File.Exists(numberedFilePath) || new FileInfo(numberedFilePath).Length < _maxFileSizeBytes)
                {
                    targetFilePath = numberedFilePath;
                    break;
                }

                counter++;
            }

            // 遍历超过上限：创建带时间戳的文件（兜底方案，避免死循环）
            if (counter > MAX_TRAVERSE_COUNT)
            {
                string timestamp = DateTime.Now.ToString("HHmmssfff"); // 精确到毫秒，避免重复
                targetFilePath = Path.Combine(directory, $"{baseFileName}_{timestamp}{extension}");
                Debug.LogWarning($"遍历超过{MAX_TRAVERSE_COUNT}个文件，创建时间戳文件：{Path.GetFileName(targetFilePath)}");
            }

            // 5. 新增：将找到的可用路径更新到缓存（下次调用直接用）
            if (!string.IsNullOrEmpty(targetFilePath))
            {
                lock (_cacheLock)
                {
                    _activeFilePathCache[cacheKey] = targetFilePath;
                    //Debug.LogError($"缓存更新：文件组[{cacheKey}] → 可用路径[{targetFilePath}]");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"GetAvailableFilePath 创建文件失败: {e.Message}\n堆栈: {e.StackTrace}");
        }

        // 兜底返回：确保返回有效路径（若target为空，返回基础路径并报警）
        if (string.IsNullOrEmpty(targetFilePath))
        {
            targetFilePath = Path.Combine(directory, $"{baseFileName}{extension}");
            Debug.LogError($"获取可用路径失败，兜底返回基础路径：{targetFilePath}");
        }
        return targetFilePath;
    }

    /// <summary>
    /// 强制立刻刷盘
    /// </summary>
    public void ForceFlushAllLog()
    {
        FlushLogCache();
    }

    private void OnApplicationQuit()
    {
        ForceFlushAllLog();
    }
}
