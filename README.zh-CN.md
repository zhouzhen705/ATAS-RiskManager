# RiskManager

[English](./README.md)

ATAS的风险管理图表策略。从入场/止损价格计算仓位大小并自动下支架订单的X

RiskManager 是一个面向 ATAS 8.x 的源码公开、禁止商用的 `ChartStrategy` 风险管理下单策略。

它允许交易者直接在图表上点击选择入场价和止损价。策略会根据用户设置的单笔风险金额自动计算下单手数，并根据风险回报比自动计算止盈价，随后通过 ATAS 策略下单 API 完成入场单、止损单和止盈单的提交。

> 本项目仅用于研究、个人使用和非商业交易流程实验，不构成任何投资建议。

---

## 功能特性

- 基于 ATAS 8.x `ChartStrategy`，不是 `Indicator`
- 点击图表选择入场价
- 点击图表选择止损价
- 自动计算 Tick 距离
- 根据固定美元风险自动计算手数
- 根据风险回报比自动计算止盈价
- 自动识别常用品种的每 Tick 价值
- 支持手动覆盖每 Tick 价值
- 在图表上显示入场线、止损线、止盈线
- 通过订单状态回调手动实现 OCO 逻辑
- 支持 HUD 浮动面板或 ATAS 原生策略参数面板

---

## 使用流程

1. 在 ATAS 图表上加载 `RiskManager` 策略。
2. 配置最大止损金额和风险回报比。
3. 点击下单模式按钮。
4. 第一次点击图表，选择入场价。
5. 第二次点击图表，选择止损价。
6. RiskManager 自动计算：
   - Tick 距离
   - 下单手数
   - 止盈价
7. 策略提交入场限价单。
8. 入场单成交后，策略再提交止损单和止盈单。
9. 如果止损单成交，自动撤销止盈单。
10. 如果止盈单成交，自动撤销止损单。

---

## 核心计算逻辑

### 方向判断

```text
入场价 < 当前市场价格  => BUY LIMIT
入场价 > 当前市场价格  => SELL LIMIT
```

### Tick 距离

```text
tickDistance = abs(entryPrice - stopLossPrice) / tickSize
```

### 手数计算

```text
quantity = floor(riskAmount / (tickDistance * tickCost))
quantity = max(quantity, 1)
```

### 止盈价

```text
做多：
takeProfitPrice = entryPrice + (entryPrice - stopLossPrice) * riskRewardRatio

做空：
takeProfitPrice = entryPrice - (stopLossPrice - entryPrice) * riskRewardRatio
```

最终止盈价需要对齐到合法的 Tick 价格。

---

## 支持的品种 Tick 价值

RiskManager 会优先从 `Security.Code` 读取实际交易品种。

| 品种前缀 | Tick Size | Tick Value |
|---|---:|---:|
| ES  | 0.25 | $12.50 |
| MES | 0.25 | $1.25 |
| NQ  | 0.25 | $5.00 |
| MNQ | 0.25 | $0.50 |
| GC  | 0.10 | $10.00 |
| MGC | 0.10 | $1.00 |

如果用户手动填写的每 Tick 价值不为 0，则优先使用手动值。

---

## ATAS 策略说明

RiskManager 必须继承 `ChartStrategy`，因为下单功能需要调用 ATAS 策略交易 API。

```csharp
using ATAS.DataFeedsCore;
using ATAS.Strategies.Chart;

public class RiskManager : ChartStrategy
{
    // Portfolio
    // Security
    // CurrentPosition
    // OpenOrder / ModifyOrder / CancelOrder
}
```

编译后的 DLL 应放置在：

```text
%AppData%\ATAS\Strategies\
```

不要放到 `Indicators` 目录。

---

## 在 ATAS 中启动策略

将策略添加到图表后：

1. 打开 ATAS 主界面底部的「交易策略」标签页。
2. 在策略列表中找到 `RiskManager`。
3. 勾选该策略行左侧的复选框。
4. 确认策略状态变为已开始或已激活。

策略设置面板中的 `IsActivated` 是只读状态，不是真正的启动开关。

Replay 回放模式可用于测试逻辑，但回放下单不会进入真实交易流程。

---

## 策略参数

| 参数名 | 类型 | 默认值 | 说明 |
|---|---:|---:|---|
| Risk Amount ($) / SL 金额($) | decimal | 250 | 每笔交易最大亏损金额 |
| Risk/Reward Ratio / RR 比例 | decimal | 2.0 | 例如 2.0 表示 1:2 |
| UI Mode / UI 样式 | enum | HUD | HUD 浮动面板或 ATAS 原生参数面板 |
| Tick Value ($) / 每 Tick 价值($) | decimal | 0 | 0 = 自动检测，非零 = 手动覆盖 |

---

## 开发环境

- 平台：ATAS 8.x
- 语言：C#
- 目标框架：`net10.0-windows`
- 编译命令：

```bash
dotnet build -c Release
```

- 输出路径：

```text
releases/RiskManager.dll
```

---

## 项目结构

```text
3-RiskManager/
├── README.md
├── README.zh-CN.md
├── src/
│   ├── RiskManager.cs
│   └── RiskManager.csproj
├── lib/
│   └── atas-8x/
│       ├── ATAS.Indicators.dll
│       ├── ATAS.Strategies.dll
│       ├── OFT.Rendering.dll
│       ├── Rendering.GDIPlus.dll
│       └── OFT.Attributes.dll
└── releases/
    └── RiskManager.dll
```

---

## 必要 DLL 引用

```xml
<ItemGroup>
  <Reference Include="ATAS.Indicators">
    <HintPath>$(ATAS_BASE)\ATAS.Indicators.dll</HintPath>
  </Reference>
  <Reference Include="ATAS.Strategies">
    <HintPath>$(ATAS_BASE)\ATAS.Strategies.dll</HintPath>
  </Reference>
  <Reference Include="OFT.Rendering">
    <HintPath>$(ATAS_BASE)\OFT.Rendering.dll</HintPath>
  </Reference>
  <Reference Include="Rendering.GDIPlus">
    <HintPath>$(ATAS_BASE)\Rendering.GDIPlus.dll</HintPath>
  </Reference>
  <Reference Include="OFT.Attributes">
    <HintPath>$(ATAS_BASE)\OFT.Attributes.dll</HintPath>
  </Reference>
</ItemGroup>
```

---

## 注意事项

1. 必须使用 `ChartStrategy`，不能使用 `Indicator`。
2. DLL 必须放在 `%AppData%\ATAS\Strategies\`。
3. 入场单成交后再提交止损单和止盈单，避免出现游离保护单。
4. ATAS 没有原生 OCO API，需要在 `OnOrderChanged` 中手动实现撤单逻辑。
5. 下单前必须校验 `tickDistance > 0` 和 `quantity > 0`。
6. 所有订单价格都要对齐到合法 Tick。
7. 下单逻辑建议放在 `try-catch` 中处理。
8. 实盘使用前应先在 Replay 或模拟环境中充分测试。

---

## 许可证

本项目为源码公开项目，仅允许非商业用途。

未经版权持有人书面许可，禁止任何商业使用，包括但不限于销售、授权、付费服务、集成到商业产品、用于付费交易服务或商业交易系统。

推荐许可证：[PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0/)

---

## 免责声明

期货交易具有高风险。本项目按原样提供，不提供任何形式的担保。作者不对因使用本软件造成的交易亏损、执行错误、平台问题、券商问题、行情数据问题或其他损失承担责任。
