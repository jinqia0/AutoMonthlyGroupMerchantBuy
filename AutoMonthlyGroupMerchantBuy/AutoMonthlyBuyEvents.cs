using System;
using System.Collections.Generic;
using GameData.Common;
using GameData.Domains.Taiwu;
using GameData.Utilities;

namespace AutoMonthlyGroupMerchantBuy;

public static class AutoMonthlyBuyEvents
{
	public static event Action<AutoMonthlyBuyCompletedEventArgs> AutoBuyCompleted;
	public static event Action<object> AutoBuyCompletedUntyped;

	internal static void Publish(DataContext context, AutoMonthlyBuyService.BuyResult buyResult)
	{
		AutoMonthlyBuyCompletedEventArgs args = new AutoMonthlyBuyCompletedEventArgs(
			context,
			buyResult.SelectedCount,
			buyResult.InventoryLimitReached,
			AutoMonthlyBuySettings.GetTargetItemSourceType(),
			buyResult.PurchasedMaterialCounts);
		AdaptableLog.Info($"[AutoMonthlyBuy] publishing auto-buy completed event. selected={args.SelectedCount}, purchasedKinds={args.PurchasedMaterialCounts.Count}, targetSource={args.TargetItemSourceType}");
		AutoBuyCompleted?.Invoke(args);
		AutoBuyCompletedUntyped?.Invoke(args);
	}
}

public sealed class AutoMonthlyBuyCompletedEventArgs : EventArgs
{
	public DataContext Context { get; }
	public int SelectedCount { get; }
	public bool InventoryLimitReached { get; }
	public ItemSourceType TargetItemSourceType { get; }
	public sbyte TargetItemSourceTypeValue => (sbyte)TargetItemSourceType;
	public IReadOnlyDictionary<short, int> PurchasedMaterialCounts { get; }

	public AutoMonthlyBuyCompletedEventArgs(
		DataContext context,
		int selectedCount,
		bool inventoryLimitReached,
		ItemSourceType targetItemSourceType,
		IReadOnlyDictionary<short, int> purchasedMaterialCounts)
	{
		Context = context;
		SelectedCount = selectedCount;
		InventoryLimitReached = inventoryLimitReached;
		TargetItemSourceType = targetItemSourceType;
		PurchasedMaterialCounts = purchasedMaterialCounts == null
			? new Dictionary<short, int>()
			: new Dictionary<short, int>(purchasedMaterialCounts);
	}
}
