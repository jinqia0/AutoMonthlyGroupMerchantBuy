using GameData.Common;
using GameData.Domains;
using GameData.Domains.World;
using GameData.Utilities;
using HarmonyLib;
using TaiwuModdingLib.Core.Plugin;

namespace AutoMonthlyGroupMerchantBuy;

[PluginConfig("AutoMonthlyGroupMerchantBuy", "Codex", "0.1.0")]
public sealed class AutoMonthlyGroupMerchantBuyPlugin : TaiwuRemakePlugin
{
	private Harmony _harmony;

	internal static string ModId { get; private set; }

	public override void Initialize()
	{
		ModId = ModIdStr;
		AutoMonthlyBuySettings.Load(ModId);
		_harmony = new Harmony("codex.auto-monthly-group-merchant-buy.backend");
		_harmony.PatchAll(typeof(AutoMonthlyGroupMerchantBuyPlugin));
		AdaptableLog.Info("[AutoMonthlyBuy] initialized. Harmony patch registered for WorldDomain.AdvanceMonth.");
	}

	public override void Dispose()
	{
		if (_harmony != null)
		{
			_harmony.UnpatchSelf();
			_harmony = null;
		}
	}

	public override void OnModSettingUpdate()
	{
		AutoMonthlyBuySettings.Load(ModId);
		AdaptableLog.Info("[AutoMonthlyBuy] settings reloaded.");
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(WorldDomain), "AdvanceMonth")]
	public static void WorldDomain_AdvanceMonth_Postfix(DataContext context)
	{
		AdaptableLog.Info("[AutoMonthlyBuy] AdvanceMonth postfix triggered.");
		AutoMonthlyBuyService.BuyResult buyResult = AutoMonthlyBuyService.RunOnAdvanceMonth(context);
		if (buyResult.SelectedCount > 0)
		{
			AutoMonthlyBuyEvents.Publish(context, buyResult);
		}
	}
}
