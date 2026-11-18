using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Utility;

namespace SimpleTweaksPlugin.TweakSystem;

// 本檔假設與契約（艦隊範本 config-load-safety）：
// ①讀壞的 tweak 設定檔在下次寫入前先複製成 <檔名>.bad-<時間>；同內容只留一份、既有備份永不覆寫。
// ②寫入一律 FilesystemUtil.WriteAllTextSafe、在背景執行；同一路徑同時只有一個寫手，排隊中只留最新內容。
// ③讀取先看排隊／寫入中的內容，「存完立刻重讀」不會讀到舊檔。
// ④寫入失敗等一下重試一次；Shutdown 返回後不再有任何背景寫入（寫完或已放棄）。
internal static class TweakConfigFile {
    internal const int DebounceMs = 250;
    internal const int RetryDelayMs = 500;
    internal const int ShutdownWaitMs = 3000;
    private const int AbandonWaitMs = 1000;

    private sealed class Slot {
        public string Text;
        public string Label;
        public long Version;
        public long DoneVersion;
        public Task Writer;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Slot> Slots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly CancellationTokenSource Hurry = new();
    private static readonly CancellationTokenSource Abandon = new();
    private static bool stopping;

    public static bool TryGetPending(string path, out string text) {
        lock (Gate) {
            if (Slots.TryGetValue(path, out var slot)) {
                text = slot.Text;
                return true;
            }
        }

        text = null;
        return false;
    }

    // 永不擲例外。Shutdown 之後改成在呼叫端執行緒寫一次、不重試。
    public static void Save(string path, string text, string label) {
        lock (Gate) {
            if (!stopping) {
                if (!Slots.TryGetValue(path, out var slot)) Slots[path] = slot = new Slot();
                slot.Text = text;
                slot.Label = label;
                slot.Version++;
                slot.Writer ??= Task.Run(() => WriteLoop(path, slot));
                return;
            }
        }

        try {
            FilesystemUtil.WriteAllTextSafe(path, text);
        } catch (Exception ex) {
            LogFinalFailure(path, label, ex);
        }
    }

    private static async Task WriteLoop(string path, Slot slot) {
        try {
            try {
                await Task.Delay(DebounceMs, Hurry.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) { }

            while (true) {
                string text, label;
                long version;
                lock (Gate) {
                    if (slot.DoneVersion == slot.Version) {
                        slot.Writer = null;
                        Slots.Remove(path);
                        return;
                    }

                    text = slot.Text;
                    label = slot.Label;
                    version = slot.Version;
                }

                await WriteWithRetry(path, text, label).ConfigureAwait(false);
                lock (Gate) slot.DoneVersion = version;
            }
        } catch (Exception ex) {
            lock (Gate) {
                slot.Writer = null;
                Slots.Remove(path);
            }

            try { SimpleLog.Error($"[設定] {Path.GetFileName(path)} 的背景寫入意外中止（{ex.GetType().Name}: {ex.Message}），原檔維持上一次的內容。"); } catch { }
        }
    }

    private static async Task WriteWithRetry(string path, string text, string label) {
        try {
            FilesystemUtil.WriteAllTextSafe(path, text);
            return;
        } catch (Exception first) {
            try {
                await Task.Delay(RetryDelayMs, Abandon.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                LogFinalFailure(path, label, first, "插件卸載等待逾時，放棄重試");
                return;
            }

            try {
                FilesystemUtil.WriteAllTextSafe(path, text);
                SimpleLog.Information($"[設定] {Path.GetFileName(path)}：第一次寫入失敗（{first.GetType().Name}: {first.Message}），{RetryDelayMs} ms 後重試成功。");
            } catch (Exception second) {
                LogFinalFailure(path, label, second, "重試一次仍失敗");
            }
        }
    }

    private static void LogFinalFailure(string path, string label, Exception ex, string context = null) {
        try {
            SimpleLog.Error($"Failed to write config for tweak: {label}");
            SimpleLog.Error($"[設定] 寫入 {Path.GetFileName(path)} 失敗{(context == null ? "" : $"（{context}）")}：{ex.GetType().Name}: {ex.Message}。原檔維持上一次的內容，這次的變更沒有存到。");
        } catch { }
    }

    // 插件 Dispose 的最後呼叫：跳過合流等待、等背景寫入有界地結束；逾時就取消重試再等一小段。
    public static void Shutdown() {
        Task[] writers;
        lock (Gate) {
            stopping = true;
            writers = Slots.Values.Select(s => s.Writer).Where(t => t != null).ToArray();
        }

        try {
            Hurry.Cancel();
            if (writers.Length == 0 || Task.WaitAll(writers, ShutdownWaitMs)) return;
            Abandon.Cancel();
            if (!Task.WaitAll(writers, AbandonWaitMs))
                SimpleLog.Error($"[設定] 卸載時仍有 {writers.Count(t => !t.IsCompleted)} 個 tweak 設定檔寫入未結束。");
        } catch (Exception ex) {
            try { SimpleLog.Error($"[設定] 卸載時等待設定檔寫入失敗（{ex.GetType().Name}: {ex.Message}）。"); } catch { }
        }
    }

    // Newtonsoft 對空白／全 NUL 的內容回 null、不擲例外 ⇒ 呼叫端把「檔在、讀到 null」也當讀壞。
    // 回傳這份內容的備份檔名（含早已備份過的那一份）；失敗回 null。永不擲例外。
    public static string BackupUnreadable(string path, string reason, Func<DateTime> now = null) {
        var name = Path.GetFileName(path);
        try {
            var original = File.ReadAllBytes(path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            foreach (var old in new DirectoryInfo(dir).EnumerateFiles(name + ".bad-*")) {
                if (old.Length == original.Length && File.ReadAllBytes(old.FullName).AsSpan().SequenceEqual(original)) {
                    SimpleLog.Information($"[設定] {name}：{reason}；相同內容已備份過（{old.Name}），這次不再另存。之後存檔會用目前的值整份覆寫 {name}。");
                    return old.Name;
                }
            }

            var backup = Path.Combine(dir, $"{name}.bad-{(now ?? (() => DateTime.Now))():yyyyMMdd-HHmmss-fff}");
            using (var fs = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                fs.Write(original);
            SimpleLog.Information($"[設定] {name}：{reason}；原檔已備份為 {Path.GetFileName(backup)}。之後存檔會用目前的值整份覆寫 {name}。");
            return Path.GetFileName(backup);
        } catch (Exception ex) {
            try { SimpleLog.Information($"[設定] {name}：{reason}；備份原檔失敗（{ex.GetType().Name}: {ex.Message}）。之後存檔會用目前的值整份覆寫 {name}。"); } catch { }
            return null;
        }
    }
}
