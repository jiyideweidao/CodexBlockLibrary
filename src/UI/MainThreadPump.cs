using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CodexBlockLib.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CodexBlockLib.UI
{
    /// <summary>
    /// AutoCAD 的 Database / Document API 只能在主线程（文档线程）上调用。
    /// 后台线程调用 Database.ReadDwgFile 时，AutoCAD 原生代码内部会查询调色板主题
    /// （AdUiMgdPaletteTheme.IsCurrentPaletteThemeDark -> PaletteTheme.IsDark -> Dispatcher.VerifyAccess），
    /// 抛出的托管异常会穿过没有托管处理句柄的原生栈帧，表现为
    /// “致命错误: Unhandled e0434352h Exception” 并终止整个 AutoCAD 进程。
    /// 因此扫描、缩略图等所有图纸读写都排入本队列，由主线程逐个执行。
    /// </summary>
    internal static class MainThreadPump
    {
        /// <summary>单次空闲里连续处理任务的时间上限，超过就让出消息循环，保证界面不卡死。</summary>
        private const int BudgetMs = 120;

        private static readonly Queue<Action> items = new Queue<Action>();
        private static readonly object sync = new object();
        private static System.Windows.Forms.Timer timer;
        private static bool idleHooked;
        private static bool running;
        private static bool shuttingDown;
        private static readonly HashSet<string> pauses = new HashSet<string>();

        public static int Pending
        {
            get { lock (sync) { return items.Count; } }
        }

        /// <summary>是否有暂停原因（面板隐藏、用户暂停扫描等）；存在任一原因就停下队列。</summary>
        public static bool Paused
        {
            get { lock (sync) { return pauses.Count > 0; } }
        }

        /// <summary>面板被隐藏导致队列暂停的原因标记。</summary>
        public const string PauseReasonHidden = "palette-hidden";

        /// <summary>用户点了“暂停扫描”导致队列暂停的原因标记。</summary>
        public const string PauseReasonScan = "scan-paused";

        /// <summary>
        /// 暂停 / 恢复队列。面板被隐藏、用户点“暂停扫描”时都必须调用，
        /// 否则排队的扫描与缩略图会继续在主线程逐张打开图纸，让 AutoCAD 长时间卡顿。
        /// 用带“原因”的方式记录，多个原因互不干扰：面板重新显示时不会把用户手动暂停的扫描悄悄跑起来。
        /// </summary>
        public static void SetPaused(string reason, bool paused)
        {
            if (string.IsNullOrEmpty(reason)) reason = "default";
            bool changed;
            lock (sync)
            {
                if (paused) changed = pauses.Add(reason);
                else changed = pauses.Remove(reason);
            }
            // 最后一个暂停原因解除后，继续把队列里剩下的任务跑完。
            if (changed && !Paused && Pending > 0) Arm();
        }

        public static void Enqueue(Action work)
        {
            if (work == null || shuttingDown) return;
            lock (sync) { items.Enqueue(work); }
            Arm();
        }

        /// <summary>丢弃尚未执行的任务。</summary>
        public static void Clear()
        {
            lock (sync) { items.Clear(); }
        }

        public static void Shutdown()
        {
            shuttingDown = true;
            Clear();
            try { if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; } }
            catch { }
            if (idleHooked)
            {
                try { AcApp.Idle -= OnIdle; }
                catch { }
                idleHooked = false;
            }
        }

        private static void Arm()
        {
            // 主触发：AutoCAD 空闲时一定在主线程且没有命令在跑，是最安全的时机。
            if (!idleHooked)
            {
                try { AcApp.Idle += OnIdle; idleHooked = true; }
                catch (Exception ex) { Log.Warn("注册主线程队列(Idle)失败: " + ex.Message); }
            }
            // 兜底触发：某些界面状态下 Idle 不一定触发，用只在本线程投递消息的 WinForms 计时器补上。
            try
            {
                if (timer == null)
                {
                    timer = new System.Windows.Forms.Timer();
                    timer.Interval = 200;
                    timer.Tick += OnTick;
                }
                if (!timer.Enabled) timer.Start();
            }
            catch (Exception ex) { Log.Warn("启动主线程队列计时器失败: " + ex.Message); }
        }

        private static void Disarm()
        {
            try { if (timer != null && timer.Enabled) timer.Stop(); }
            catch { }
        }

        private static void OnIdle(object sender, EventArgs e) { Pump(); }

        private static void OnTick(object sender, EventArgs e) { Pump(); }

        private static void Pump()
        {
            if (running || shuttingDown || Paused) return;
            if (Pending == 0) { Disarm(); return; }
            // 命令进行中不要去碰数据库，等命令结束后继续。
            if (IsCommandActive()) return;

            running = true;
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                while (true)
                {
                    Action work;
                    lock (sync)
                    {
                        if (items.Count == 0) break;
                        work = items.Dequeue();
                    }
                    try { work(); }
                    catch (Exception ex) { Log.Error("主线程任务失败", ex); }
                    if (watch.ElapsedMilliseconds >= BudgetMs) break;
                }
            }
            finally { running = false; }
            if (Pending == 0) Disarm();
        }

        /// <summary>命令进行中（CMDACTIVE 非 0）时推迟图纸读写。</summary>
        private static bool IsCommandActive()
        {
            try
            {
                object value = AcApp.GetSystemVariable("CMDACTIVE");
                if (value == null) return false;
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            }
            catch { return false; }
        }
    }
}
