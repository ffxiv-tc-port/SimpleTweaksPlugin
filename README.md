<!-- ffxiv-tc-port 繁體中文說明開始 -->
# Simple Tweaks

[Simple Tweaks](https://github.com/Caraxi/SimpleTweaksPlugin) 的台服移植版。收錄大量各自獨立的小調整，在不大幅改變遊戲的前提下讓日常操作更順手；每一項都可以個別開關，預設全部關閉。

## 功能

小調整分成聊天、指令、介面、提示、便利性等類別。以下只舉幾個例子，名稱與插件內顯示的相同：

- **修正「/target」指令**：可以直接用遊戲內建的 `/target` 以名稱選取玩家或 NPC。
- **回覆頻道切換**：輸入 `/r` 時，把目前的聊天頻道切換為悄悄話。
- **新增物品時開啟戰利品視窗**：有新物品可擲骰時，自動開啟戰利品擲骰視窗。
- **總是選「是」**：確認視窗的預設游標停在「是」，可以逐類勾選要套用的視窗（僱員探險、僱員重複物品交付、分解、強化黃道武器等）。
- **市場佈告板增強**：市場佈告板的介面增強，例如價格納入稅金。
- **顯示食物與藥品的預期數值**：依你目前的能力值，計算消耗品實際帶來的數值。
- **HP 條顯示護盾**：在 HP 條上顯示大約的護盾量。
- **指令類**：釣餌指令（`/bait`）、離開副本指令（`/leaveduty`）、指令別名等。

完整清單見 [Tweaks.md](Tweaks.md)（上游的英文清單）。其中有少數項目因已知問題而停用，設定頁會標示出來。

## 指令

- `/tweaks`：開啟／關閉設定視窗
- `/tweaks toggle <功能 ID>`、`/tweaks enable <功能 ID>`、`/tweaks disable <功能 ID>`：切換／啟用／停用指定的小調整
- `/tweaks find <功能 ID>`：在設定視窗裡跳到指定的小調整
- `/tweaks changelog`：開啟／關閉更新日誌視窗

各項小調整自己新增的指令（例如 `/bait`）要先啟用該項才能使用。

## 台服版的修改

- 介面繁體中文化：內建台服用語的繁體中文翻譯，台服客戶端第一次載入時預設使用，也可以在設定的「語言」選單切換。
- 修正三項在台服客戶端不作用的小調整：修正「/target」指令、回覆頻道切換、新增物品時開啟戰利品視窗。
- 遊戲改版後有特徵碼對不上時，只停用用到它的功能，不會讓整個插件一項功能都載入不了。
- 移除使用統計上報（上游會把啟用中的功能清單送到原作者的統計伺服器）；停用清單改用隨插件出貨的版本，不再每次載入時向上游下載。
- 「重新整理市場價格」（市場查價被拒時自動重試）在台服版停用，設定頁顯示「因已知問題而停用。」
- 總是選「是」：新增「強化黃道武器」視窗（預設關）；「僱員重複物品交付」的勾選修正為真的有作用。
- 增加聊天泡泡最大行數：修正調整泡泡顯示時間時可能讓遊戲崩潰的問題。
- 各項小調整的設定檔讀取失敗時會先留一份備份；存檔改成不會只寫到一半的方式。
- 多處降低記憶體用量。

## 安裝

1. 在遊戲內輸入 `/xlsettings` 開啟「Dalamud 設定」，到「試驗性功能」分頁的「第三方插件倉庫」（Custom Plugin Repositories）貼上
   `https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
   勾選啟用後按「儲存變更」。
2. 輸入 `/xlplugins` 開啟「插件管理員」（Plugin Installer），搜尋「Simple Tweaks」安裝。

請不要安裝上游（國際服）版本，它不是對著台服客戶端做的。

原作者：[Caraxi](https://github.com/Caraxi)

授權：AGPL-3.0（見 [LICENSE](LICENSE)）

---

以下為上游原始 README，內容未經修改：

<!-- ffxiv-tc-port 繁體中文說明結束 -->

# Simple Tweaks
Simple Tweaks is a plugin for Dalamud that started as a way to create small additions without creating an entire plugin for a couple lines of code.
Now, Simple Tweaks has grown to the point that the whole plugin should probably be renamed, but I think it can stay as Simple Tweaks and the name can be a little joke.

Simple Tweaks aims to improve quality of life when playing the game by making small changes that improve the experience. 


## [Tweak List](Tweaks.md)