using System;
using System.Text.RegularExpressions;
using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using SimpleTweaksPlugin.TweakSystem;
using SimpleTweaksPlugin.Utility;

namespace SimpleTweaksPlugin.Tweaks.Chat;

[TweakName("Reply Channel Switch")]
[TweakDescription("Allow typing /r to set active chat channel to Tell.")]
public class ReplyChannelSwitch : ChatTweaks.SubTweak {
    private string searchString = string.Empty;
    private Regex searchPattern;

    protected override void Enable() {
        searchString = Service.ClientState.ClientLanguage switch {
            ClientLanguage.Japanese => @"1番目に文字列の指定がありません。： /r",
            ClientLanguage.English => @"“/r” requires a valid string.",
            ClientLanguage.German => @"Das Textkommando „/r“ erfordert den Unterbefehl [Name/Eingabe] an 1. Stelle.",
            ClientLanguage.French => @"L'argument “nom” est manquant (/r).",
            _ => string.Empty
        };
        // 台服字面沒實機取樣過，只知道樣板（LogMessage 3802 不存在／3803 不正確），所以比對樣板。
        searchPattern = Service.ClientState.ClientLanguage == ClientLanguage.TraditionalChinese
            ? new Regex(@"^「/r(eply)?」出現問題：\d+號指定的.+?(不存在|不正確)。$")
            : null;

        Service.Chat.CheckMessageHandled += CheckMesssage;
    }

    private void CheckMesssage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled) {
        if (type != XivChatType.ErrorMessage) return;
        if (searchPattern != null) {
            if (!searchPattern.IsMatch(message.TextValue)) return;
        } else if (searchString.Length == 0 || message.TextValue != searchString) {
            return;
        }
        ChatHelper.SendMessage("/t <r>");
        isHandled = true;
    }

    protected override void Disable() => Service.Chat.CheckMessageHandled -= CheckMesssage;
}
