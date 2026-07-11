using System;
using GameData.Domains;
using GameData.Domains.Taiwu;
using GameData.Utilities;

namespace AutoMonthlyGroupMerchantBuy;

internal static class AutoMonthlyBuySettings
{
	private const short FoodMaterialSubType = 500;
	private const short MedicinalMaterialSubType = 505;
	private const short PoisonMaterialSubType = 506;

	internal static bool EnableAutoMonthlyBuy = true;
	internal static int FoodLowestPurchaseGrade = 9;
	internal static int FoodHighestPurchaseGrade = 1;
	internal static int MedicinalLowestPurchaseGrade = 9;
	internal static int MedicinalHighestPurchaseGrade = 1;
	internal static int PoisonLowestPurchaseGrade = 9;
	internal static int PoisonHighestPurchaseGrade = 1;
	private static int _foodHighestPurchaseGradeOption;
	private static int _foodLowestPurchaseGradeOption = 8;
	private static int _medicinalHighestPurchaseGradeOption;
	private static int _medicinalLowestPurchaseGradeOption = 8;
	private static int _poisonHighestPurchaseGradeOption;
	private static int _poisonLowestPurchaseGradeOption = 8;
	internal static int AutoBuyMoneyThreshold;
	internal static int TargetItemSource;
	internal static int InventoryLoadReserve;
	internal static bool BuyFoodMaterial = true;
	internal static bool BuyMedicinalMaterial = true;
	internal static bool BuyPoisonMaterial;
	internal static bool SkipPriceIncreasedItems;
	internal static bool SkipOriginalPriceItems;
	internal static bool EnableDebugLogging;

	internal static void Load(string modId)
	{
		if (string.IsNullOrEmpty(modId))
		{
			return;
		}

		Read(modId, "EnableAutoMonthlyBuy", ref EnableAutoMonthlyBuy);
		Read(modId, "FoodHighestPurchaseGrade", ref _foodHighestPurchaseGradeOption);
		Read(modId, "FoodLowestPurchaseGrade", ref _foodLowestPurchaseGradeOption);
		Read(modId, "MedicinalHighestPurchaseGrade", ref _medicinalHighestPurchaseGradeOption);
		Read(modId, "MedicinalLowestPurchaseGrade", ref _medicinalLowestPurchaseGradeOption);
		Read(modId, "PoisonHighestPurchaseGrade", ref _poisonHighestPurchaseGradeOption);
		Read(modId, "PoisonLowestPurchaseGrade", ref _poisonLowestPurchaseGradeOption);
		Read(modId, "AutoBuyMoneyThreshold", ref AutoBuyMoneyThreshold);
		Read(modId, "TargetItemSource", ref TargetItemSource);
		Read(modId, "InventoryLoadReserve", ref InventoryLoadReserve);
		Read(modId, "BuyFoodMaterial", ref BuyFoodMaterial);
		Read(modId, "BuyMedicinalMaterial", ref BuyMedicinalMaterial);
		Read(modId, "BuyPoisonMaterial", ref BuyPoisonMaterial);
		Read(modId, "SkipPriceIncreasedItems", ref SkipPriceIncreasedItems);
		Read(modId, "SkipOriginalPriceItems", ref SkipOriginalPriceItems);
		Read(modId, "EnableDebugLogging", ref EnableDebugLogging);

		NormalizeGradeRange(_foodHighestPurchaseGradeOption, _foodLowestPurchaseGradeOption, out FoodHighestPurchaseGrade, out FoodLowestPurchaseGrade);
		NormalizeGradeRange(_medicinalHighestPurchaseGradeOption, _medicinalLowestPurchaseGradeOption, out MedicinalHighestPurchaseGrade, out MedicinalLowestPurchaseGrade);
		NormalizeGradeRange(_poisonHighestPurchaseGradeOption, _poisonLowestPurchaseGradeOption, out PoisonHighestPurchaseGrade, out PoisonLowestPurchaseGrade);
		AutoBuyMoneyThreshold = Math.Clamp(AutoBuyMoneyThreshold, 0, 100000);
		TargetItemSource = Math.Clamp(TargetItemSource, 0, 2);
		InventoryLoadReserve = Math.Clamp(InventoryLoadReserve, 0, 20);
	}

	internal static void GetMaterialGradeRange(short itemSubType, out int highestDisplayGrade, out int lowestDisplayGrade)
	{
		switch (itemSubType)
		{
			case FoodMaterialSubType:
				highestDisplayGrade = FoodHighestPurchaseGrade;
				lowestDisplayGrade = FoodLowestPurchaseGrade;
				break;
			case MedicinalMaterialSubType:
				highestDisplayGrade = MedicinalHighestPurchaseGrade;
				lowestDisplayGrade = MedicinalLowestPurchaseGrade;
				break;
			case PoisonMaterialSubType:
				highestDisplayGrade = PoisonHighestPurchaseGrade;
				lowestDisplayGrade = PoisonLowestPurchaseGrade;
				break;
			default:
				highestDisplayGrade = 1;
				lowestDisplayGrade = 9;
				break;
		}
	}

	internal static string GetGradeSummary()
	{
		return $"food={FoodHighestPurchaseGrade}-{FoodLowestPurchaseGrade}, medicinal={MedicinalHighestPurchaseGrade}-{MedicinalLowestPurchaseGrade}, poison={PoisonHighestPurchaseGrade}-{PoisonLowestPurchaseGrade}";
	}

	private static void NormalizeGradeRange(int highestOption, int lowestOption, out int highestDisplayGrade, out int lowestDisplayGrade)
	{
		highestDisplayGrade = GradeOptionToDisplayGrade(highestOption);
		lowestDisplayGrade = GradeOptionToDisplayGrade(lowestOption);
		if (highestDisplayGrade > lowestDisplayGrade)
		{
			int temp = highestDisplayGrade;
			highestDisplayGrade = lowestDisplayGrade;
			lowestDisplayGrade = temp;
		}
	}

	private static int GradeOptionToDisplayGrade(int option)
	{
		return Math.Clamp(option, 0, 8) + 1;
	}

	internal static ItemSourceType GetTargetItemSourceType()
	{
		return TargetItemSource switch
		{
			1 => ItemSourceType.Warehouse,
			2 => ItemSourceType.Treasury,
			_ => ItemSourceType.Inventory,
		};
	}

	private static void Read(string modId, string key, ref bool value)
	{
		try
		{
			DomainManager.Mod.GetSetting(modId, key, ref value);
		}
		catch (Exception ex)
		{
			AdaptableLog.Warning($"[AutoMonthlyBuy] failed to read bool setting {key}: {ex.Message}");
		}
	}

	private static void Read(string modId, string key, ref int value)
	{
		try
		{
			DomainManager.Mod.GetSetting(modId, key, ref value);
		}
		catch (Exception ex)
		{
			AdaptableLog.Warning($"[AutoMonthlyBuy] failed to read int setting {key}: {ex.Message}");
		}
	}
}
