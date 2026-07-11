using System;
using System.Collections.Generic;
using Config;
using Config.Common;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Building;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Merchant;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.Domains.Taiwu.ExchangeSystem;
using GameData.Utilities;
using CharacterObject = GameData.Domains.Character.Character;

namespace AutoMonthlyGroupMerchantBuy;

internal static class AutoMonthlyBuyService
{
	private const int MaterialItemType = 5;
	private const short FoodMaterialSubType = 500;
	private const short MedicinalMaterialSubType = 505;
	private const short PoisonMaterialSubType = 506;
	private const sbyte MoneyResourceType = 6;
	private const int MaxSelectedItemLogLinesPerMerchant = 40;

	internal static BuyResult RunOnAdvanceMonth(DataContext context)
	{
		if (!AutoMonthlyBuySettings.EnableAutoMonthlyBuy)
		{
			DebugLog("[AutoMonthlyBuy] skipped. auto monthly buy is disabled.");
			return BuyResult.Empty;
		}

		try
		{
			int money = GetTaiwuMoney();
			ItemSourceType targetItemSource = AutoMonthlyBuySettings.GetTargetItemSourceType();
			DebugLog($"[AutoMonthlyBuy] begin month-end auto buy. money={money}, threshold={AutoMonthlyBuySettings.AutoBuyMoneyThreshold}, grades=({AutoMonthlyBuySettings.GetGradeSummary()}), targetSource={targetItemSource}, loadReserve={AutoMonthlyBuySettings.InventoryLoadReserve}, food={AutoMonthlyBuySettings.BuyFoodMaterial}, medicine={AutoMonthlyBuySettings.BuyMedicinalMaterial}, poison={AutoMonthlyBuySettings.BuyPoisonMaterial}, skipIncreased={AutoMonthlyBuySettings.SkipPriceIncreasedItems}, skipOriginal={AutoMonthlyBuySettings.SkipOriginalPriceItems}, debug={AutoMonthlyBuySettings.EnableDebugLogging}");
			if (money <= AutoMonthlyBuySettings.AutoBuyMoneyThreshold)
			{
				DebugLog($"[AutoMonthlyBuy] skipped. money={money}, threshold={AutoMonthlyBuySettings.AutoBuyMoneyThreshold}");
				return BuyResult.Empty;
			}

			int merchantCount = 0;
			int selectedCount = 0;
			bool inventoryLimitReached = false;
			Dictionary<short, int> purchasedMaterialCounts = new Dictionary<short, int>();
			HashSet<int> groupIds = DomainManager.Taiwu.GetGroupCharIds().GetCollection();
			DebugLog($"[AutoMonthlyBuy] scanning group members. count={groupIds.Count}");
			foreach (int charId in groupIds)
			{
				money = GetTaiwuMoney();
				if (money <= AutoMonthlyBuySettings.AutoBuyMoneyThreshold)
				{
					DebugLog($"[AutoMonthlyBuy] stopped. money={money}, threshold={AutoMonthlyBuySettings.AutoBuyMoneyThreshold}");
					break;
				}

				if (!IsValidGroupMerchant(context, charId))
				{
					continue;
				}

				merchantCount++;
				BuyResult result = AutoBuyFromGroupMerchant(context, charId);
				selectedCount += result.SelectedCount;
				MergeCounts(purchasedMaterialCounts, result.PurchasedMaterialCounts);
				if (result.InventoryLimitReached)
				{
					inventoryLimitReached = true;
					DebugLog($"[AutoMonthlyBuy] stopped. reason=inventory-limit, selected={selectedCount}");
					break;
				}
			}

			DebugLog($"[AutoMonthlyBuy] month-end scan complete. merchants={merchantCount}, selected={selectedCount}, purchasedKinds={purchasedMaterialCounts.Count}, moneyAfter={GetTaiwuMoney()}, inventoryLimitReached={inventoryLimitReached}");
			return new BuyResult(selectedCount, inventoryLimitReached, purchasedMaterialCounts);
		}
		catch (Exception ex)
		{
			AdaptableLog.Error("[AutoMonthlyBuy] month-end auto buy failed: " + ex);
			return BuyResult.Empty;
		}
	}

	private static int GetTaiwuMoney()
	{
		try
		{
			return DomainManager.Taiwu.GetTaiwu().GetResource(MoneyResourceType);
		}
		catch
		{
			return 0;
		}
	}

	private static void MergeCounts(Dictionary<short, int> target, IReadOnlyDictionary<short, int> source)
	{
		if (source == null)
		{
			return;
		}

		foreach (KeyValuePair<short, int> item in source)
		{
			if (item.Value <= 0)
			{
				continue;
			}

			target.TryGetValue(item.Key, out int oldCount);
			target[item.Key] = oldCount + item.Value;
		}
	}

	internal static bool IsValidGroupMerchant(DataContext context, int charId)
	{
		if (charId == DomainManager.Taiwu.GetTaiwuCharId())
		{
			DebugLog($"[AutoMonthlyBuy] skip group member {charId}. reason=taiwu-self");
			return false;
		}

		if (!DomainManager.Character.TryGetElement_Objects(charId, out CharacterObject character))
		{
			DebugLog($"[AutoMonthlyBuy] skip group member {charId}. reason=character-not-found");
			return false;
		}

		if (character.GetAgeGroup() == 0)
		{
			DebugLog($"[AutoMonthlyBuy] skip group member {charId}. reason=child");
			return false;
		}

		sbyte merchantType = DomainManager.Taiwu.GetMerchantType(charId);
		if (merchantType < 0 || merchantType >= 7)
		{
			DebugLog($"[AutoMonthlyBuy] skip group member {charId}. reason=not-merchant, merchantType={merchantType}");
			return false;
		}

		MerchantData merchantData = DomainManager.Merchant.GetMerchantData(context, charId);
		if (merchantData == null)
		{
			DebugLog($"[AutoMonthlyBuy] skip group merchant {charId}. reason=merchant-data-null, merchantType={merchantType}");
			return false;
		}

		if (merchantData.CharId != charId)
		{
			DebugLog($"[AutoMonthlyBuy] skip group merchant {charId}. reason=merchant-data-char-mismatch, merchantType={merchantType}, dataCharId={merchantData.CharId}");
			return false;
		}

		DebugLog($"[AutoMonthlyBuy] group merchant accepted. charId={charId}, merchantType={merchantType}, template={merchantData.MerchantTemplateId}, level={merchantData.MerchantLevel}, money={merchantData.Money}");
		return true;
	}

	internal static BuyResult AutoBuyFromGroupMerchant(DataContext context, int merchantCharId)
	{
		OpenShopEventArguments openArgs = new OpenShopEventArguments
		{
			Id = merchantCharId,
			MerchantSourceType = (sbyte)OpenShopEventArguments.EMerchantSourceType.NormalCharacter,
			Refresh = false,
			IgnoreFavorability = false,
			IgnoreWorldProgress = false,
			CurrPage = 0
		};

		int moneyBefore = GetTaiwuMoney();
		DebugLog($"[AutoMonthlyBuy] open shop. merchant={merchantCharId}, moneyBefore={moneyBefore}");
		ShopDisplayData shopData = DomainManager.Taiwu.GetShopDisplayData(context, openArgs);
		ShopExchange exchange = shopData?.Exchange;
		if (exchange == null || exchange.TradeArguments == null)
		{
			DebugLog($"[AutoMonthlyBuy] shop unavailable. merchant={merchantCharId}, shopDataNull={shopData == null}, exchangeNull={exchange == null}");
			return BuyResult.Empty;
		}

		ItemSourceType targetItemSource = AutoMonthlyBuySettings.GetTargetItemSourceType();
		exchange.SetItemSource((sbyte)targetItemSource);
		DebugLog($"[AutoMonthlyBuy] target source before selection. merchant={merchantCharId}, targetSource={targetItemSource}, inventoryLimitEnabled={IsInventoryLimitEnabled(exchange)}, inventoryLoad={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveInventoryMax={GetEffectiveInventoryMaxLoad(exchange)}, reserve={AutoMonthlyBuySettings.InventoryLoadReserve}");
		if (IsInventoryLimitReached(exchange))
		{
			DebugLog($"[AutoMonthlyBuy] no purchase. merchant={merchantCharId}, reason=inventory-limit, targetSource={targetItemSource}, load={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}, reserve={AutoMonthlyBuySettings.InventoryLoadReserve}");
			return BuyResult.StoppedByInventoryLimit;
		}

		SelectionState selectionState = new SelectionState();
		int selected = SelectMatchingItems(exchange, shopData, merchantCharId, selectionState);
		if (selected <= 0)
		{
			DebugLog($"[AutoMonthlyBuy] no matching material selected. merchant={merchantCharId}, minDebtLevel={exchange.MinDebtLevel}, maxDebtLevel={exchange.MaxDebtLevel}, targetSource={targetItemSource}, load={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}");
			return new BuyResult(0, selectionState.InventoryLimitReached, selectionState.PurchasedMaterialCounts);
		}

		long buyMoneyBeforeDeal = exchange.TradeArguments.BuyMoney;
		DebugLog($"[AutoMonthlyBuy] selected items. merchant={merchantCharId}, selected={selected}, buyMoneyBeforeDeal={buyMoneyBeforeDeal}, targetSource={exchange.ToItemSourceTypeEnum}, loadPreview={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}, inventoryLimitReached={selectionState.InventoryLimitReached}");
		DomainManager.Taiwu.ConfirmShopExchange(context, exchange);
		LogTradeChangeSummary(exchange, merchantCharId);
		int moneyAfter = GetTaiwuMoney();
		DebugLog($"[AutoMonthlyBuy] bought {selected} material items from group merchant {merchantCharId}. moneyBefore={moneyBefore}, moneyAfter={moneyAfter}, delta={moneyAfter - moneyBefore}, loadPreview={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}");
		return new BuyResult(selected, selectionState.InventoryLimitReached, selectionState.PurchasedMaterialCounts);
	}

	private static int SelectMatchingItems(ShopExchange exchange, ShopDisplayData shopData, int merchantCharId, SelectionState selectionState)
	{
		int selected = 0;
		int selectedLogLines = 0;
		for (int page = 0; page <= 6; page++)
		{
			if (selectionState.InventoryLimitReached)
			{
				break;
			}

			if (!exchange.IsPageShow(page))
			{
				DebugLog($"[AutoMonthlyBuy] page skipped. merchant={merchantCharId}, page={page}, reason=hidden");
				continue;
			}

			int remainingLimitedCount = GetRemainingLimitedCount(exchange, page);
			if (remainingLimitedCount == 0)
			{
				DebugLog($"[AutoMonthlyBuy] page skipped. merchant={merchantCharId}, page={page}, reason=locked-by-favor, minDebtLevel={exchange.MinDebtLevel}");
				continue;
			}

			List<ItemDisplayData> items = shopData[page];
			if (items == null)
			{
				DebugLog($"[AutoMonthlyBuy] page skipped. merchant={merchantCharId}, page={page}, reason=item-list-null");
				continue;
			}

			PageScanStats stats = new PageScanStats(page, items.Count);
			foreach (ItemDisplayData item in items)
			{
				if (selectionState.InventoryLimitReached)
				{
					break;
				}

				int count = GetSelectableMaterialCount(exchange, item, page, ref remainingLimitedCount, selectionState, out string reason, out MaterialItem material, out int priceChangePercentValue);
				if (count <= 0)
				{
					stats.AddSkip(reason);
					if (selectionState.InventoryLimitReached)
					{
						DebugLog($"[AutoMonthlyBuy] page stopped. merchant={merchantCharId}, page={page}, reason=inventory-limit, load={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}");
						break;
					}

					continue;
				}

				exchange.SelectTargetItem(item, count);
				selectionState.AddPurchasedMaterial(item.RealKey.TemplateId, count);
				stats.SelectedKinds++;
				stats.SelectedCount += count;
				selected += count;
				if (AutoMonthlyBuySettings.EnableDebugLogging && selectedLogLines < MaxSelectedItemLogLinesPerMerchant)
				{
					LogSelectedItem(merchantCharId, page, item, material, count, priceChangePercentValue);
					selectedLogLines++;
				}

				if (selectionState.InventoryLimitReached)
				{
					DebugLog($"[AutoMonthlyBuy] page stopped. merchant={merchantCharId}, page={page}, reason=inventory-limit-after-partial-selection, load={exchange.TaiwuInventoryCurLoadPreview}/{exchange.TaiwuInventoryMaxLoadPreview}, effectiveMax={GetEffectiveInventoryMaxLoad(exchange)}");
					break;
				}
			}

			DebugLog(stats.ToLogString(merchantCharId));
		}

		if (AutoMonthlyBuySettings.EnableDebugLogging && selectedLogLines >= MaxSelectedItemLogLinesPerMerchant)
		{
			DebugLog($"[AutoMonthlyBuy] selected item log truncated. merchant={merchantCharId}, maxLines={MaxSelectedItemLogLinesPerMerchant}");
		}

		return selected;
	}

	private static int GetSelectableMaterialCount(ShopExchange exchange, ITradeableContent item, int page, ref int remainingLimitedCount, SelectionState selectionState, out string reason, out MaterialItem material, out int priceChangePercentValue)
	{
		reason = "selected";
		material = null;
		priceChangePercentValue = 0;

		if (item == null || item.Amount <= 0)
		{
			reason = "empty";
			return 0;
		}

		if (!ShopExchange.IsShopItem(item) || item.ItemSourceType - 10 != page)
		{
			reason = "not-current-shop-page";
			return 0;
		}

		if ((int)item.UsingType != -1 || item.ItemSourceType == 0)
		{
			reason = "using-or-invalid-source";
			return 0;
		}

		if (item.RealKey.ItemType != MaterialItemType)
		{
			reason = "not-material";
			return 0;
		}

		material = GetMaterialConfig(item);
		if (material == null || !IsAllowedMaterialSubType(material.ItemSubType))
		{
			reason = material == null ? "material-config-null" : $"material-subtype-disabled:{material.ItemSubType}";
			return 0;
		}

		sbyte grade = material.Grade;
		AutoMonthlyBuySettings.GetMaterialGradeRange(material.ItemSubType, out int highestDisplayGrade, out int lowestDisplayGrade);
		int highestRawGrade = DisplayGradeToRaw(highestDisplayGrade);
		int lowestRawGrade = DisplayGradeToRaw(lowestDisplayGrade);
		if (grade > highestRawGrade || grade < lowestRawGrade)
		{
			reason = $"grade-out-of-range:{grade},subtype={material.ItemSubType},range={highestDisplayGrade}-{lowestDisplayGrade}";
			return 0;
		}

		priceChangePercentValue = exchange.GetPriceChangePercentValue(item, true);
		if (AutoMonthlyBuySettings.SkipPriceIncreasedItems && priceChangePercentValue > 0)
		{
			reason = $"price-increased:{priceChangePercentValue}";
			return 0;
		}

		if (AutoMonthlyBuySettings.SkipOriginalPriceItems && priceChangePercentValue == 0)
		{
			reason = "original-price";
			return 0;
		}

		int available = item.Amount - GetSelectedTargetAmount(exchange, item);
		if (available <= 0)
		{
			reason = "already-selected-or-empty";
			return 0;
		}

		int count = remainingLimitedCount < 0 ? available : Math.Min(available, remainingLimitedCount);
		if (count <= 0)
		{
			reason = "limited-count-exhausted";
			return 0;
		}

		int countBeforeLoadCheck = count;
		count = LimitCountByInventoryCapacity(exchange, item, count, selectionState, out reason);
		if (count <= 0)
		{
			return 0;
		}

		if (remainingLimitedCount >= 0)
		{
			remainingLimitedCount -= count;
		}

		if (count < countBeforeLoadCheck)
		{
			selectionState.InventoryLimitReached = true;
		}

		return count;
	}

	private static int LimitCountByInventoryCapacity(ShopExchange exchange, ITradeableContent item, int count, SelectionState selectionState, out string reason)
	{
		reason = "selected";
		if (!IsInventoryLimitEnabled(exchange))
		{
			return count;
		}

		int effectiveMaxLoad = GetEffectiveInventoryMaxLoad(exchange);
		int remainingLoad = effectiveMaxLoad - exchange.TaiwuInventoryCurLoadPreview;
		if (remainingLoad <= 0)
		{
			selectionState.InventoryLimitReached = true;
			reason = "inventory-limit";
			return 0;
		}

		int itemWeight = GetItemWeight(item);
		if (itemWeight <= 0)
		{
			return count;
		}

		int countByLoad = remainingLoad / itemWeight;
		if (countByLoad <= 0)
		{
			selectionState.InventoryLimitReached = true;
			reason = $"inventory-limit:itemWeight={itemWeight},remainingLoad={remainingLoad}";
			return 0;
		}

		if (countByLoad < count)
		{
			selectionState.LoadLimitedKinds++;
			selectionState.LoadLimitedCount += count - countByLoad;
			reason = $"inventory-limit-partial:itemWeight={itemWeight},remainingLoad={remainingLoad}";
			return countByLoad;
		}

		return count;
	}

	private static bool IsInventoryLimitReached(ShopExchange exchange)
	{
		return IsInventoryLimitEnabled(exchange)
			&& exchange.TaiwuInventoryCurLoadPreview >= GetEffectiveInventoryMaxLoad(exchange);
	}

	private static bool IsInventoryLimitEnabled(ShopExchange exchange)
	{
		return exchange.ToItemSourceTypeEnum == ItemSourceType.Inventory;
	}

	private static int GetEffectiveInventoryMaxLoad(ShopExchange exchange)
	{
		return Math.Max(0, exchange.TaiwuInventoryMaxLoadPreview - AutoMonthlyBuySettings.InventoryLoadReserve);
	}

	private static int GetItemWeight(ITradeableContent item)
	{
		try
		{
			return ItemTemplateHelper.GetBaseWeight(item.RealKey.ItemType, item.RealKey.TemplateId);
		}
		catch
		{
			return 0;
		}
	}

	private static MaterialItem GetMaterialConfig(ITradeableContent item)
	{
		if (item == null)
		{
			return null;
		}

		try
		{
			return ((ConfigData<MaterialItem, short>)Config.Material.Instance)[item.RealKey.TemplateId];
		}
		catch
		{
			return null;
		}
	}

	private static bool IsAllowedMaterialSubType(short itemSubType)
	{
		return (itemSubType == FoodMaterialSubType && AutoMonthlyBuySettings.BuyFoodMaterial)
			|| (itemSubType == MedicinalMaterialSubType && AutoMonthlyBuySettings.BuyMedicinalMaterial)
			|| (itemSubType == PoisonMaterialSubType && AutoMonthlyBuySettings.BuyPoisonMaterial);
	}

	private static int DisplayGradeToRaw(int displayGrade)
	{
		return 9 - Math.Clamp(displayGrade, 1, 9);
	}

	private static int GetRemainingLimitedCount(ShopExchange exchange, int page)
	{
		if (exchange.MinDebtLevel >= page)
		{
			return -1;
		}

		return 0;
	}

	private static int GetSelectedTargetAmount(ShopExchange exchange, ITradeableContent item)
	{
		int selected = 0;
		List<ITradeableContent> targetContentList = exchange.TargetContentList;
		for (int i = 0; i < targetContentList.Count; i++)
		{
			ITradeableContent existing = targetContentList[i];
			if (existing == null)
			{
				continue;
			}

			if (existing.RealKey.Equals(item.RealKey) && existing.CharacterId == item.CharacterId && existing.ItemSourceType == item.ItemSourceType)
			{
				selected += existing.Amount;
			}
		}

		return selected;
	}

	private static void LogSelectedItem(int merchantCharId, int page, ITradeableContent item, MaterialItem material, int count, int priceChangePercentValue)
	{
		string name = string.IsNullOrEmpty(material?.Name) ? "unknown" : material.Name;
		DebugLog($"[AutoMonthlyBuy] select item. merchant={merchantCharId}, page={page}, name={name}, type={item.RealKey.ItemType}, template={item.RealKey.TemplateId}, subtype={material?.ItemSubType ?? -1}, grade={material?.Grade ?? -1}, count={count}, amountBefore={item.Amount}, priceChange={priceChangePercentValue}, source={item.ItemSourceType}");
	}

	internal static void DebugLog(string message)
	{
		if (AutoMonthlyBuySettings.EnableDebugLogging)
		{
			AdaptableLog.Info(message);
		}
	}

	private static void LogTradeChangeSummary(ShopExchange exchange, int merchantCharId)
	{
		if (!AutoMonthlyBuySettings.EnableDebugLogging)
		{
			return;
		}

		List<ItemSourceChange> changes = exchange.TradeArguments?.ItemChangeList;
		if (changes == null)
		{
			DebugLog($"[AutoMonthlyBuy] trade changes missing after ConfirmShopExchange. merchant={merchantCharId}");
			return;
		}

		int nonEmptySources = 0;
		foreach (ItemSourceChange change in changes)
		{
			if (change?.Items == null || change.Items.Count == 0)
			{
				continue;
			}

			nonEmptySources++;
			int addCount = 0;
			int removeCount = 0;
			foreach (ItemKeyAndCount item in change.Items)
			{
				if (item.Count > 0)
				{
					addCount += item.Count;
				}
				else
				{
					removeCount += -item.Count;
				}
			}

			DebugLog($"[AutoMonthlyBuy] trade change. merchant={merchantCharId}, source={(ItemSourceType)change.ItemSourceType}, entries={change.Items.Count}, add={addCount}, remove={removeCount}");
		}

		if (nonEmptySources == 0)
		{
			DebugLog($"[AutoMonthlyBuy] trade changes empty after ConfirmShopExchange. merchant={merchantCharId}");
		}
	}

	private sealed class PageScanStats
	{
		private readonly int _page;
		private readonly int _total;
		private int _empty;
		private int _notCurrentShopPage;
		private int _usingOrInvalidSource;
		private int _notMaterial;
		private int _configNull;
		private int _subtypeDisabled;
		private int _gradeOutOfRange;
		private int _priceIncreased;
		private int _originalPrice;
		private int _alreadySelectedOrEmpty;
		private int _limitedCountExhausted;
		private int _inventoryLimit;
		private int _inventoryLimitPartial;
		private int _other;

		internal int SelectedKinds;
		internal int SelectedCount;

		internal PageScanStats(int page, int total)
		{
			_page = page;
			_total = total;
		}

		internal void AddSkip(string reason)
		{
			if (string.IsNullOrEmpty(reason))
			{
				_other++;
			}
			else if (reason == "empty")
			{
				_empty++;
			}
			else if (reason == "not-current-shop-page")
			{
				_notCurrentShopPage++;
			}
			else if (reason == "using-or-invalid-source")
			{
				_usingOrInvalidSource++;
			}
			else if (reason == "not-material")
			{
				_notMaterial++;
			}
			else if (reason == "material-config-null")
			{
				_configNull++;
			}
			else if (reason.StartsWith("material-subtype-disabled:"))
			{
				_subtypeDisabled++;
			}
			else if (reason.StartsWith("grade-out-of-range:"))
			{
				_gradeOutOfRange++;
			}
			else if (reason.StartsWith("price-increased:"))
			{
				_priceIncreased++;
			}
			else if (reason == "original-price")
			{
				_originalPrice++;
			}
			else if (reason == "already-selected-or-empty")
			{
				_alreadySelectedOrEmpty++;
			}
			else if (reason == "limited-count-exhausted")
			{
				_limitedCountExhausted++;
			}
			else if (reason.StartsWith("inventory-limit-partial:"))
			{
				_inventoryLimitPartial++;
			}
			else if (reason == "inventory-limit" || reason.StartsWith("inventory-limit:"))
			{
				_inventoryLimit++;
			}
			else
			{
				_other++;
			}
		}

		internal string ToLogString(int merchantCharId)
		{
			return $"[AutoMonthlyBuy] page scan. merchant={merchantCharId}, page={_page}, total={_total}, selectedKinds={SelectedKinds}, selectedCount={SelectedCount}, empty={_empty}, notPage={_notCurrentShopPage}, using={_usingOrInvalidSource}, notMaterial={_notMaterial}, configNull={_configNull}, subtypeDisabled={_subtypeDisabled}, gradeOut={_gradeOutOfRange}, priceIncreased={_priceIncreased}, originalPrice={_originalPrice}, alreadySelected={_alreadySelectedOrEmpty}, limited={_limitedCountExhausted}, inventoryLimit={_inventoryLimit}, inventoryPartial={_inventoryLimitPartial}, other={_other}";
		}
	}

	internal readonly struct BuyResult
	{
		internal static readonly BuyResult Empty = new BuyResult(0, inventoryLimitReached: false, new Dictionary<short, int>());
		internal static readonly BuyResult StoppedByInventoryLimit = new BuyResult(0, inventoryLimitReached: true, new Dictionary<short, int>());

		internal int SelectedCount { get; }
		internal bool InventoryLimitReached { get; }
		internal IReadOnlyDictionary<short, int> PurchasedMaterialCounts { get; }

		internal BuyResult(int selectedCount, bool inventoryLimitReached, IReadOnlyDictionary<short, int> purchasedMaterialCounts)
		{
			SelectedCount = selectedCount;
			InventoryLimitReached = inventoryLimitReached;
			PurchasedMaterialCounts = purchasedMaterialCounts ?? new Dictionary<short, int>();
		}
	}

	private sealed class SelectionState
	{
		internal readonly Dictionary<short, int> PurchasedMaterialCounts = new Dictionary<short, int>();
		internal bool InventoryLimitReached;
		internal int LoadLimitedKinds;
		internal int LoadLimitedCount;

		internal void AddPurchasedMaterial(short templateId, int count)
		{
			if (count <= 0)
			{
				return;
			}

			PurchasedMaterialCounts.TryGetValue(templateId, out int oldCount);
			PurchasedMaterialCounts[templateId] = oldCount + count;
		}
	}
}
