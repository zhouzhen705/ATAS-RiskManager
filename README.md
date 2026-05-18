# ATAS-RiskManager

[简体中文](./README.zh-CN.md)

A risk management ChartStrategy for ATAS 8.x that calculates position size from entry/stop prices and places bracket orders automatically.

RiskManager is a source-available, non-commercial ATAS 8.x `ChartStrategy` for risk-based position sizing and bracket order placement.

It allows a trader to select an entry price and a stop-loss price directly on the chart. The strategy then calculates the order quantity from the configured risk amount, derives the take-profit price from the configured risk/reward ratio, and places the required orders through the ATAS strategy trading API.

> This project is intended for research, personal use, and non-commercial trading workflow experiments only. It is not financial advice.

---

## Features

- ATAS 8.x `ChartStrategy`, not an `Indicator`
- Click-to-select entry price
- Click-to-select stop-loss price
- Automatic tick distance calculation
- Automatic position size calculation based on fixed dollar risk
- Automatic take-profit price calculation based on risk/reward ratio
- Automatic tick value detection for common futures symbols
- Manual tick value override
- Entry, stop-loss, and take-profit chart lines
- Manual OCO-style logic through order status handling
- Optional HUD-style panel or native ATAS strategy parameter panel

---

## Workflow

1. Load the `RiskManager` strategy on an ATAS chart.
2. Configure the maximum stop-loss amount and risk/reward ratio.
3. Click the order mode button.
4. Click the chart once to select the entry price.
5. Click the chart again to select the stop-loss price.
6. RiskManager calculates:
   - tick distance
   - order quantity
   - take-profit price
7. The strategy submits the entry order.
8. After the entry order is filled, the stop-loss and take-profit orders are submitted.
9. If the stop-loss is filled, the take-profit order is cancelled.
10. If the take-profit is filled, the stop-loss order is cancelled.

---

## Core Calculation

### Direction

```text
Entry Price < Current Market Price  => BUY LIMIT
Entry Price > Current Market Price  => SELL LIMIT
```

### Tick Distance

```text
tickDistance = abs(entryPrice - stopLossPrice) / tickSize
```

### Quantity

```text
quantity = floor(riskAmount / (tickDistance * tickCost))
quantity = max(quantity, 1)
```

### Take Profit

```text
Long:
takeProfitPrice = entryPrice + (entryPrice - stopLossPrice) * riskRewardRatio

Short:
takeProfitPrice = entryPrice - (stopLossPrice - entryPrice) * riskRewardRatio
```

The final take-profit price should be rounded to the nearest valid tick size.

---

## Supported Symbol Tick Values

RiskManager first attempts to read the actual trading symbol from `Security.Code`.

| Symbol Prefix | Tick Size | Tick Value |
|---|---:|---:|
| ES  | 0.25 | $12.50 |
| MES | 0.25 | $1.25 |
| NQ  | 0.25 | $5.00 |
| MNQ | 0.25 | $0.50 |
| GC  | 0.10 | $10.00 |
| MGC | 0.10 | $1.00 |

If the configured tick value is non-zero, the manual value takes priority over automatic detection.

---

## ATAS Strategy Notes

RiskManager must inherit from `ChartStrategy` because order placement requires access to the ATAS strategy trading API.

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

The compiled DLL should be placed in:

```text
%AppData%\ATAS\Strategies\
```

Do not place the DLL in the `Indicators` directory.

---

## Activation in ATAS

After adding the strategy to a chart:

1. Open the **Trading Strategies** tab at the bottom of the ATAS main window.
2. Find `RiskManager` in the strategy list.
3. Enable the checkbox on the left side of the strategy row.
4. Confirm that the strategy status changes to started/active.

`IsActivated` in the strategy settings panel is a read-only state indicator. It is not the real activation control.

Replay mode can be used for testing logic, but replay orders do not enter the real trading workflow.

---

## Strategy Parameters

| Parameter | Type | Default | Description |
|---|---:|---:|---|
| Risk Amount ($) | decimal | 250 | Maximum dollar risk per trade |
| Risk/Reward Ratio | decimal | 2.0 | Example: 2.0 means 1:2 RR |
| UI Mode | enum | HUD | HUD panel or native ATAS parameter panel |
| Tick Value ($) | decimal | 0 | 0 = auto-detect, non-zero = manual override |

---

## Development Environment

- Platform: ATAS 8.x
- Language: C#
- Target framework: `net10.0-windows`
- Build command:

```bash
dotnet build -c Release
```

- Output path:

```text
releases/RiskManager.dll
```

---

## Project Structure

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

## Required References

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

## Important Notes

1. Use `ChartStrategy`, not `Indicator`.
2. Place the DLL in `%AppData%\ATAS\Strategies\`.
3. Submit stop-loss and take-profit orders only after the entry order is filled.
4. OCO behavior must be implemented manually in `OnOrderChanged`.
5. Validate `tickDistance > 0` and `quantity > 0` before placing orders.
6. Round all order prices to valid tick increments.
7. Wrap order placement logic in `try-catch`.
8. Test thoroughly in replay or simulation before using with live accounts.

---

## Disclaimer

Trading futures involves substantial risk. This project is provided as-is, without warranty of any kind. The author is not responsible for trading losses, execution errors, platform issues, broker issues, data feed problems, or any other damages caused by the use of this software.
