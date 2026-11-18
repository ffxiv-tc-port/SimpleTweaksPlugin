using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Text;
using SimpleTweaksPlugin.TweakSystem;
using SimpleTweaksPlugin.Utility;

namespace SimpleTweaksPlugin.Tweaks.Chat;

// 本檔假設與契約（泡泡顯示時間）：遊戲在泡泡開始顯示時設 Flags 的 0x04、Timestamp＝當下毫秒，到 Timestamp+4000 就移除。
// 遊戲會整條清空佇列並釋放元素（隱藏泡泡的狀態每幀清、改泡泡設定時連被 hook 的函式本身都會清），
// 所以泡泡位址一律不跨幀保存，每幀從管理器重走。
// 預設值（4000 毫秒、每字 0）不讀也不寫佇列。

[TweakName("Increase max line count in chat bubbles")]
[TweakDescription("Allow upto 7 lines of text to be displayed in chat bubbles.")]
[TweakCategory(TweakCategory.Chat)]
[TweakReleaseVersion("1.10.11.1")]
[Changelog("1.10.12.1", "Added ability to adjust the duration of chat bubbles.")]
[TweakAutoConfig]
public unsafe class IncreaseChatBubbleLines : Tweak {
    public class Configs : TweakConfig {
        [TweakConfigOption("Max Lines", IntMin = 1, IntMax = 7, IntType = TweakConfigOptionAttribute.IntEditType.Slider, EditorSize = 150)]
        public int MaxLines = 5;
        public int Duration = 4000;
        public int AddDurationPerCharacter = 0;

    }
    [TweakConfig] public Configs TweakConfig { get; private set; } = new();
    
    protected void DrawConfig(ref bool hasChanged) {
        var timeSeconds = TweakConfig.Duration / 1000f;
        ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat(Loc.T("Bubble Duration (seconds)"), ref timeSeconds, 0.1f, 1f, "%.1f")) {
            TweakConfig.Duration = Math.Clamp((int)MathF.Round(timeSeconds * 10), 10, 600) * 100;
            hasChanged = true;
        }

        ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
        hasChanged |= ImGui.InputInt(Loc.T("Additional time per character of message (milliseconds)"), ref TweakConfig.AddDurationPerCharacter, 1, 10);
    }
    
    
    // MSVC std::deque（元素大於 16 位元組＝每塊一個元素）；Proxy 的 +0 指回本佇列。
    [StructLayout(LayoutKind.Explicit, Size = 0x28)]
    private struct BubbleQueue {
        [FieldOffset(0x00)] public nint Proxy;
        [FieldOffset(0x08)] public nint* Map;
        [FieldOffset(0x10)] public ulong MapSize;
        [FieldOffset(0x18)] public ulong Offset;
        [FieldOffset(0x20)] public ulong Size;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ChatBubbleStruct {
        [FieldOffset(0x18)] public BubbleQueue Queue0;
        [FieldOffset(0x40)] public BubbleQueue Queue1;
        [FieldOffset(0x8C)] public byte LineCount;
    }

    private delegate ulong ChatBubble(ChatBubbleStruct* a1);

    [TweakHook, Signature("E8 ?? ?? ?? ?? 0F B6 E8 48 8D 5F 18 40 0A 6C 24 ?? BE", DetourName = nameof(ChatBubbleDetour))]
    private HookWrapper<ChatBubble> chatBubbleHook;

    [StructLayout(LayoutKind.Explicit)]
    public struct ChatBubbleEntry {
        [FieldOffset(0x000)] public Utf8String String;
        [FieldOffset(0x1B8)] public long Timestamp;
        [FieldOffset(0x1C6)] public byte Flags;
    }

    private const int VanillaDuration = 4000;
    private const byte BubbleStartedFlag = 0x04;
    private const ulong MaxBubblesPerQueue = 256;
    private const ulong MaxMapSize = 1 << 20;

    private delegate uint GetStringSize(TextChecker* textChecker, Utf8String* str);
    [Signature("E8 ?? ?? ?? ?? 49 8D 56 40")]
    private GetStringSize getStringSize;

    // 已延長過的泡泡以「寫進去的時間戳值」辨識，不存位址；可能有不只一個管理器，所以不按走訪輪替，
    // 改用遊戲自己的開始時間淘汰早已到期的值（不用本機時鐘，免得與遊戲時鐘算法不同而提早淘汰）。
    // 只在 ChatBubbleDetour 內存取；停用或改回預設時刻意不清（再開啟時畫面上已延長的泡泡不會再被延長一次）。
    private const long StampRetention = 60_000;
    private readonly HashSet<long> extendedStamps = new();
    private Predicate<long> isStampStale;
    private long latestStart;
    private long staleBefore;
    private bool loggedLayoutMismatch;
    private bool loggedError;

    private ulong ChatBubbleDetour(ChatBubbleStruct* a1) {
        try {
            return chatBubbleHook.Original(a1);
        } finally {
            try {
                a1->LineCount = (byte)Math.Clamp(TweakConfig.MaxLines, 1, 7);
                ExtendStartedBubbles(a1);
            } catch (Exception ex) {
                if (!loggedError) {
                    loggedError = true;
                    SimpleLog.Error(ex, "Error handing chat bubbles.");
                }
            }
        }
    }

    // 契約：只在 Original 回傳後、同一次呼叫內從 manager 重走；任何一條佇列驗不過＝整幀不寫。
    private void ExtendStartedBubbles(ChatBubbleStruct* manager) {
        if (TweakConfig.Duration == VanillaDuration && TweakConfig.AddDurationPerCharacter <= 0) return;

        if (!IsUserAddress((nint)manager) || !TryReadQueue(&manager->Queue0, out var queue0) || !TryReadQueue(&manager->Queue1, out var queue1)) {
            if (!loggedLayoutMismatch) {
                loggedLayoutMismatch = true;
                SimpleLog.Information("[ChatBubble] Bubble queue layout check failed; duration extension skipped (logged once).");
            }
            return;
        }

        ExtendQueue(queue0);
        ExtendQueue(queue1);
        PruneStaleStamps();
    }

    // 值 S 的泡泡在遊戲時間 S+4000 到期；比最近一次看到的開始時間還早 StampRetention 以上＝早已被遊戲移除。
    private void PruneStaleStamps() {
        if (extendedStamps.Count == 0) return;
        staleBefore = latestStart - VanillaDuration - StampRetention;
        extendedStamps.RemoveWhere(isStampStale ??= IsStampStale);
    }

    private bool IsStampStale(long stamp) => stamp < staleBefore;

    private static bool IsUserAddress(nint address) {
        var value = (ulong)address;
        return value >= 0x10000 && value <= 0x00007FFFFFFFFFFF && (value & 7) == 0;
    }

    private struct QueueView {
        public nint* Map;
        public ulong Mask;
        public ulong Offset;
        public ulong Size;
    }

    // 先驗不需解參考的欄位，再驗 Proxy 指回本佇列（當型別檢查），最後才讀 map 裡的元素位址；寫入只走驗過的快照。
    private static bool TryReadQueue(BubbleQueue* queue, out QueueView view) {
        view = default;
        var size = queue->Size;
        if (size == 0) return true;
        var mapSize = queue->MapSize;
        if (size > MaxBubblesPerQueue || mapSize > MaxMapSize || size >= mapSize || (mapSize & (mapSize - 1)) != 0) return false;
        var map = queue->Map;
        var proxy = queue->Proxy;
        if (!IsUserAddress((nint)map) || !IsUserAddress(proxy)) return false;
        if (*(nint*)proxy != (nint)queue) return false;
        var mask = mapSize - 1;
        var offset = queue->Offset;
        for (ulong i = 0; i < size; i++) {
            if (!IsUserAddress(map[(offset + i) & mask])) return false;
        }
        view = new QueueView { Map = map, Mask = mask, Offset = offset, Size = size };
        return true;
    }

    private void ExtendQueue(in QueueView queue) {
        for (ulong i = 0; i < queue.Size; i++) {
            var bubble = (ChatBubbleEntry*)queue.Map[(queue.Offset + i) & queue.Mask];
            if ((bubble->Flags & BubbleStartedFlag) == 0) continue;
            var stamp = bubble->Timestamp;
            if (stamp <= 0) continue;
            if (extendedStamps.Contains(stamp)) continue;

            var extended = stamp + ExtraDuration(bubble);
            bubble->Timestamp = extended;
            extendedStamps.Add(extended);
            if (stamp > latestStart) latestStart = stamp;
        }
    }

    private long ExtraDuration(ChatBubbleEntry* bubble) {
        long extra = TweakConfig.Duration - VanillaDuration;
        if (TweakConfig.AddDurationPerCharacter > 0) {
            var characters = getStringSize(&RaptureTextModule.Instance()->TextChecker, &bubble->String);
            var maxCharacters = 194u * (uint)Math.Clamp(TweakConfig.MaxLines, 1, 7);
            extra += (long)TweakConfig.AddDurationPerCharacter * Math.Min(characters, maxCharacters);
        }
        return extra;
    }
}
