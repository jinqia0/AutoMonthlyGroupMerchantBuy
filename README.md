# 过月同道商人自动采购

太吾绘卷后端 Mod。过月时自动扫描太吾同道中的商人，并按配置购买食材、药材与毒物。

## 功能

- 过月后自动触发采购。
- 采购对象为太吾同道中可识别为商人的角色。
- 支持食材、药材、毒物三类材料的独立开关。
- 支持分别设置食材、药材、毒物的最高/最低购买品级。
- 支持跳过涨价物品、跳过原价物品。
- 支持设置银币触发阈值，银币不足时不采购。
- 支持选择默认保存位置：行囊、私库、公库。
- 保存到行囊时支持背包负重预留，避免自动采购导致超重。
- 支持热修改配置，修改配置后无需重启游戏。
- Debug Logging 开启时输出详细扫描、选择与交易变更日志。

## 配置说明

### 基础设置

- 启用过月自动采购：总开关。
- 采购食材：是否购买食材类材料。
- 采购药材：是否购买药材类材料。
- 采购毒物：是否购买毒物类材料。
- 不采购涨价物品：跳过价格上涨的商品。
- 不采购原价物品：跳过原价商品。
- 默认保存位置：行囊、私库或公库。

### 高级设置

- 自动采购银币阈值：仅当太吾当前银币高于该值时触发采购。
- 背包负重预留阈值：保存到行囊时，购买后负重不超过背包上限减去该值。

### 品级设置

每类材料都可以分别设置最高品级和最低品级。品级文本使用游戏内置称呼：

- 神·一品
- 绝·二品
- 超·三品
- 极·四品
- 秘·五品
- 奇·六品
- 上·七品
- 中·八品
- 下·九品

如果最高/最低品级配置顺序相反，后端会自动归一化为有效范围。

### 调试设置

- Debug Logging：开启后输出详细日志，包括商人扫描、页面扫描、选择结果、交易变更等信息。

默认关闭，避免过月日志过多。

## 实现逻辑

入口位于 `AutoMonthlyGroupMerchantBuyPlugin.cs`：

- 使用 Harmony Postfix 监听 `WorldDomain.AdvanceMonth`。
- 过月后调用 `AutoMonthlyBuyService.RunOnAdvanceMonth(context)`。
- 若本次采购成功，会通过 `AutoMonthlyBuyEvents` 发布自动采购完成事件，供其他 Mod 可选监听。

采购核心位于 `AutoMonthlyBuyService.cs`：

- 读取银币、目标保存位置、品级范围、材料类型开关等配置。
- 遍历 `DomainManager.Taiwu.GetGroupCharIds()` 获取同道。
- 使用 `DomainManager.Taiwu.GetMerchantType(charId)` 与 `DomainManager.Merchant.GetMerchantData(context, charId)` 判断同道是否为有效商人。
- 通过 `DomainManager.Taiwu.GetShopDisplayData(context, openArgs)` 获取商店数据。
- 遍历 `Shop0` 到 `Shop6` 商品页，筛选材料类型、品级、价格变化与剩余容量。
- 通过 `exchange.SelectTargetItem(item, count)` 选择要买的物品。
- 使用官方封装 `DomainManager.Taiwu.ConfirmShopExchange(context, exchange)` 完成交易。

配置读取位于 `AutoMonthlyBuySettings.cs`：

- 使用 `DomainManager.Mod.GetSetting` 读取配置。
- `NeedRestartWhenSettingChanged = false`，并在 `OnModSettingUpdate()` 中重新读取配置。
- 保存位置映射为：
  - `0`：`ItemSourceType.Inventory`
  - `1`：`ItemSourceType.Warehouse`
  - `2`：`ItemSourceType.Treasury`

## 目录结构

```text
AutoMonthlyGroupMerchantBuy/
  Config.lua
  README.md
  AutoMonthlyGroupMerchantBuy/
    AutoMonthlyBuyEvents.cs
    AutoMonthlyBuyService.cs
    AutoMonthlyBuySettings.cs
    AutoMonthlyGroupMerchantBuyPlugin.cs
```

## 构建

需要引用太吾绘卷游戏目录中的 Backend DLL。示例路径：

```powershell
$csc = "work\decompile-tools\roslyn\tasks\netcore\bincore\csc.dll"
$backend = "C:\Program Files (x86)\Steam\steamapps\common\The Scroll Of Taiwu\Backend"
$src = Get-ChildItem -Path "github\AutoMonthlyGroupMerchantBuy\AutoMonthlyGroupMerchantBuy" -Filter *.cs

$refs = @(
  "System.Private.CoreLib.dll",
  "System.Runtime.dll",
  "System.Collections.dll",
  "System.Collections.NonGeneric.dll",
  "System.Linq.dll",
  "System.Console.dll",
  "System.Runtime.Extensions.dll",
  "netstandard.dll",
  "mscorlib.dll",
  "System.dll",
  "System.Core.dll",
  "0Harmony.dll",
  "TaiwuModdingLib.dll",
  "GameData.dll",
  "GameData.Shared.dll",
  "GameData.Common.dll",
  "GameData.Utilities.dll",
  "GameData.Utilities.Structure.dll",
  "GameData.Common.Algorithm.dll",
  "GameData.Serializer.dll",
  "GameData.ArchiveData.dll"
) | ForEach-Object { "/reference:" + (Join-Path $backend $_) }

dotnet $csc /nologo /target:library /langversion:latest /nostdlib+ `
  /out:"AutoMonthlyGroupMerchantBuy.dll" @refs @($src.FullName)
```

## 安装

将文件放入游戏本地 Mod 目录：

```text
The Scroll Of Taiwu/Mod/AutoMonthlyGroupMerchantBuy/
  Config.lua
  Plugins/
    AutoMonthlyGroupMerchantBuy.dll
```

DLL 更新后需要重启游戏。仅修改配置值时，游戏内配置页面支持热修改。

## 兼容说明

近期游戏更新后，商店交易确认流程发生变化。当前版本使用官方封装：

```csharp
DomainManager.Taiwu.ConfirmShopExchange(context, exchange);
```

避免直接调用 `Deal()` 时遗漏官方新增的交易准备步骤。

## 日志

普通情况下只保留异常日志。开启 Debug Logging 后，会输出：

- 过月触发信息。
- 商人筛选结果。
- 商品页扫描统计。
- 选中物品详情。
- 交易后 `ItemChangeList` 汇总。

如果过月没有采购，建议先开启 Debug Logging，再检查银币阈值、材料类型、品级范围、价格过滤和同道商人条件。
