using HarmonyLib;
using System;
using System.Collections.Generic;

namespace TrollingFishing;

internal static class FishingLocalization
{
    internal const string FishingRodBagOpenHintKey = "$tf_fishingrod_bag_open_hint";
    internal const string FishingRodMultiLineHintKey = "$tf_fishingrod_multi_line_hint";
    internal const string FishingBaitTooltipHeaderKey = "$tf_fishing_bait_tooltip_header";
    private const string FishingRodBagOpenHintWord = "tf_fishingrod_bag_open_hint";
    private const string FishingRodMultiLineHintWord = "tf_fishingrod_multi_line_hint";
    private const string FishingBaitTooltipHeaderWord = "tf_fishing_bait_tooltip_header";
    private const string EnglishLanguage = "english";
    private const string EnglishOpenHint = "Press <b>$KEY_Use</b> to open the fishing bag.";
    private const string EnglishMultiLineHint = "Hold <b>$KEY_SecondaryAttack</b> to cast multiple fishing lines.";
    private const string EnglishBaitTooltipHeader = "Accepted by:";

    private static readonly Dictionary<string, string> OpenHintByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = EnglishOpenHint,
        ["swedish"] = "Tryck på <b>$KEY_Use</b> för att öppna fiskeväskan.",
        ["french"] = "Appuyez sur <b>$KEY_Use</b> pour ouvrir le sac de pêche.",
        ["italian"] = "Premi <b>$KEY_Use</b> per aprire la borsa da pesca.",
        ["german"] = "Drücke <b>$KEY_Use</b>, um die Angeltasche zu öffnen.",
        ["spanish"] = "Pulsa <b>$KEY_Use</b> para abrir la bolsa de pesca.",
        ["russian"] = "Нажмите <b>$KEY_Use</b>, чтобы открыть рыболовную сумку.",
        ["finnish"] = "Paina <b>$KEY_Use</b> avataksesi kalastuslaukun.",
        ["danish"] = "Tryk på <b>$KEY_Use</b> for at åbne fisketasken.",
        ["norwegian"] = "Trykk på <b>$KEY_Use</b> for å åpne fiskebagen.",
        ["turkish"] = "Balık çantasını açmak için <b>$KEY_Use</b> tuşuna bas.",
        ["lithuanian"] = "Paspauskite <b>$KEY_Use</b>, kad atidarytumėte žvejybos krepšį.",
        ["czech"] = "Stiskni <b>$KEY_Use</b> pro otevření rybářské brašny.",
        ["hungarian"] = "Nyomd meg a(z) <b>$KEY_Use</b> gombot a horgásztáska megnyitásához.",
        ["slovak"] = "Stlač <b>$KEY_Use</b> na otvorenie rybárskej tašky.",
        ["polish"] = "Naciśnij <b>$KEY_Use</b>, aby otworzyć torbę wędkarską.",
        ["dutch"] = "Druk op <b>$KEY_Use</b> om de vistas te openen.",
        ["portuguese_european"] = "Prime <b>$KEY_Use</b> para abrir a bolsa de pesca.",
        ["portuguese_brazilian"] = "Pressione <b>$KEY_Use</b> para abrir a bolsa de pesca.",
        ["chinese"] = "按 <b>$KEY_Use</b> 打开钓鱼包。",
        ["chinese_trad"] = "按下 <b>$KEY_Use</b> 開啟釣魚包。",
        ["japanese"] = "釣りバッグを開くには <b>$KEY_Use</b> を押します。",
        ["korean"] = "낚시 가방을 열려면 <b>$KEY_Use</b>를 누르세요.",
        ["thai"] = "กด <b>$KEY_Use</b> เพื่อเปิดกระเป๋าตกปลา",
        ["greek"] = "Πάτησε <b>$KEY_Use</b> για να ανοίξεις την τσάντα ψαρέματος.",
        ["ukrainian"] = "Натисніть <b>$KEY_Use</b>, щоб відкрити рибальську сумку.",
        ["latvian"] = "Nospiediet <b>$KEY_Use</b>, lai atvērtu makšķerēšanas somu.",
    };

    private static readonly Dictionary<string, string> MultiLineHintByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = EnglishMultiLineHint,
        ["swedish"] = "Håll ned <b>$KEY_SecondaryAttack</b> för att kasta flera fiskelinor.",
        ["french"] = "Maintenez <b>$KEY_SecondaryAttack</b> pour lancer plusieurs lignes de pêche.",
        ["italian"] = "Tieni premuto <b>$KEY_SecondaryAttack</b> per lanciare più lenze da pesca.",
        ["german"] = "Halte <b>$KEY_SecondaryAttack</b> gedrückt, um mehrere Angelschnüre auszuwerfen.",
        ["spanish"] = "Mantén <b>$KEY_SecondaryAttack</b> para lanzar varias líneas de pesca.",
        ["russian"] = "Удерживайте <b>$KEY_SecondaryAttack</b>, чтобы закинуть несколько лесок.",
        ["finnish"] = "Pidä <b>$KEY_SecondaryAttack</b> painettuna heittääksesi useita siimoja.",
        ["danish"] = "Hold <b>$KEY_SecondaryAttack</b> nede for at kaste flere fiskeliner.",
        ["norwegian"] = "Hold inne <b>$KEY_SecondaryAttack</b> for å kaste flere fiskesnører.",
        ["turkish"] = "Birden fazla olta misinası atmak için <b>$KEY_SecondaryAttack</b> tuşunu basılı tut.",
        ["lithuanian"] = "Laikykite <b>$KEY_SecondaryAttack</b>, kad užmestumėte kelis žvejybos valus.",
        ["czech"] = "Podrž <b>$KEY_SecondaryAttack</b> pro nahození více rybářských vlasců.",
        ["hungarian"] = "Tartsd lenyomva a(z) <b>$KEY_SecondaryAttack</b> gombot több horgászzsinór bedobásához.",
        ["slovak"] = "Podrž <b>$KEY_SecondaryAttack</b> na nahodenie viacerých rybárskych vlascov.",
        ["polish"] = "Przytrzymaj <b>$KEY_SecondaryAttack</b>, aby zarzucić kilka żyłek.",
        ["dutch"] = "Houd <b>$KEY_SecondaryAttack</b> ingedrukt om meerdere vislijnen uit te werpen.",
        ["portuguese_european"] = "Mantém <b>$KEY_SecondaryAttack</b> premido para lançar várias linhas de pesca.",
        ["portuguese_brazilian"] = "Segure <b>$KEY_SecondaryAttack</b> para lançar várias linhas de pesca.",
        ["chinese"] = "长按 <b>$KEY_SecondaryAttack</b> 抛出多根鱼线。",
        ["chinese_trad"] = "長按 <b>$KEY_SecondaryAttack</b> 拋出多條釣線。",
        ["japanese"] = "複数の釣り糸を投げるには <b>$KEY_SecondaryAttack</b> を長押しします。",
        ["korean"] = "여러 낚싯줄을 던지려면 <b>$KEY_SecondaryAttack</b>을 길게 누르세요.",
        ["thai"] = "กด <b>$KEY_SecondaryAttack</b> ค้างไว้เพื่อเหวี่ยงสายเบ็ดหลายสาย",
        ["greek"] = "Κράτησε πατημένο το <b>$KEY_SecondaryAttack</b> για να ρίξεις πολλές πετονιές.",
        ["ukrainian"] = "Утримуйте <b>$KEY_SecondaryAttack</b>, щоб закинути кілька лісок.",
        ["latvian"] = "Turiet <b>$KEY_SecondaryAttack</b>, lai iemestu vairākas makšķerauklas.",
    };

    private static readonly Dictionary<string, string> BaitTooltipHeaderByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = EnglishBaitTooltipHeader,
        ["swedish"] = "Accepteras av:",
        ["french"] = "Accepté par :",
        ["italian"] = "Accettata da:",
        ["german"] = "Akzeptiert von:",
        ["spanish"] = "Aceptado por:",
        ["russian"] = "Принимается:",
        ["finnish"] = "Kelpaavat kalat:",
        ["danish"] = "Accepteres af:",
        ["norwegian"] = "Godtas av:",
        ["turkish"] = "Kabul edenler:",
        ["lithuanian"] = "Priima:",
        ["czech"] = "Přijímají:",
        ["hungarian"] = "Elfogadja:",
        ["slovak"] = "Prijímajú:",
        ["polish"] = "Akceptowane przez:",
        ["dutch"] = "Geaccepteerd door:",
        ["portuguese_european"] = "Aceite por:",
        ["portuguese_brazilian"] = "Aceito por:",
        ["chinese"] = "可钓：",
        ["chinese_trad"] = "可釣：",
        ["japanese"] = "有効な魚:",
        ["korean"] = "유효한 물고기:",
        ["thai"] = "ใช้ได้กับ:",
        ["greek"] = "Γίνεται δεκτό από:",
        ["ukrainian"] = "Підходить для:",
        ["latvian"] = "Derīgs:",
    };

    internal static void Register()
    {
        if (Localization.instance != null)
        {
            Register(Localization.instance);
        }
    }

    internal static void Register(Localization localization)
    {
        if (localization == null)
        {
            return;
        }

        string selectedLanguage = localization.GetSelectedLanguage();
        string languageName = NormalizeLanguageName(selectedLanguage);
        localization.AddWord(FishingRodBagOpenHintWord, GetOpenHint(languageName));
        localization.AddWord(FishingRodMultiLineHintWord, GetMultiLineHint(languageName));
        localization.AddWord(FishingBaitTooltipHeaderWord, GetBaitTooltipHeader(languageName));
    }

    internal static string Localize(string key)
    {
        if (Localization.instance == null)
        {
            return key;
        }

        string localized = Localization.instance.Localize(key);
        return localized.Contains('$') ? Localization.instance.Localize(localized) : localized;
    }

    private static string NormalizeLanguageName(string languageName)
    {
        return string.IsNullOrWhiteSpace(languageName)
            ? EnglishLanguage
            : languageName.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string GetOpenHint(string languageName)
    {
        return OpenHintByLanguage.TryGetValue(languageName, out string translation) ? translation : EnglishOpenHint;
    }

    private static string GetMultiLineHint(string languageName)
    {
        return MultiLineHintByLanguage.TryGetValue(languageName, out string translation) ? translation : EnglishMultiLineHint;
    }

    private static string GetBaitTooltipHeader(string languageName)
    {
        return BaitTooltipHeaderByLanguage.TryGetValue(languageName, out string translation) ? translation : EnglishBaitTooltipHeader;
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class LocalizationSetupLanguageFishingPatch
{
    private static void Postfix(Localization __instance)
    {
        FishingLocalization.Register(__instance);
    }
}
