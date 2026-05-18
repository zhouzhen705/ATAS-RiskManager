using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using OFT.Rendering;
using OFT.Rendering.Context;
using OFT.Rendering.Control;
using OFT.Rendering.Tools;

namespace RiskManagerStrategy;

[DisplayName("RiskManager")]
[Category("Trading")]
public class RiskManager : ChartStrategy
{
    public enum PanelStyle
    {
        [Display(Name = "HUD")]
        Hud,

        [Display(Name = "折叠栏")]
        Collapsed
    }

    private enum ClickStage
    {
        Idle,
        WaitingEntry,
        WaitingStop
    }

    private readonly RenderFont _titleFont = new("Arial", 10f, FontStyle.Bold, GraphicsUnit.Point, 204);
    private readonly RenderFont _font = new("Arial", 9f, FontStyle.Regular, GraphicsUnit.Point, 204);
    private readonly RenderFont _smallFont = new("Arial", 8f, FontStyle.Regular, GraphicsUnit.Point, 204);
    private readonly RenderStringFormat _centerFormat = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };

    private readonly RenderStringFormat _leftFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center
    };

    private readonly Color _panelBack = Color.FromArgb(220, 24, 28, 36);
    private readonly Color _panelBorder = Color.FromArgb(230, 112, 124, 145);
    private readonly Color _panelText = Color.FromArgb(245, 245, 247, 250);
    private readonly Color _mutedText = Color.FromArgb(220, 166, 177, 196);
    private readonly Color _buttonBack = Color.FromArgb(235, 40, 48, 62);
    private readonly Color _buttonActive = Color.FromArgb(235, 37, 99, 235);
    private readonly Color _buttonDanger = Color.FromArgb(235, 177, 56, 70);
    private readonly Color _entryColor = Color.FromArgb(255, 66, 153, 225);
    private readonly Color _stopColor = Color.FromArgb(255, 229, 62, 62);
    private readonly Color _targetColor = Color.FromArgb(255, 72, 187, 120);

    private decimal _riskAmount = 250m;
    private decimal _rewardRiskRatio = 2.0m;
    private decimal _tickValueOverride;
    private PanelStyle _uiStyle = PanelStyle.Hud;

    private ClickStage _stage;
    private Rectangle _panelRect = new(20, 20, 290, 292);
    private Rectangle _cancelButtonRect;
    private Rectangle _modeButtonRect;
    private bool _isDraggingPanel;
    private Point _dragOffset;
    private decimal? _entryPrice;
    private decimal? _stopPrice;
    private decimal? _targetPrice;
    private decimal? _lastTickDistance;
    private decimal? _lastQuantity;
    private Order? _entryOrder;
    private Order? _stopOrder;
    private Order? _targetOrder;
    private string _entryOrderTag = string.Empty;
    private string _stopOrderTag = string.Empty;
    private string _targetOrderTag = string.Empty;
    private bool _bracketsSubmitted;
    private string _statusText = "等待下单";

    [Display(Name = "SL 金额($)", GroupName = "风险管理", Order = 10)]
    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal RiskAmount
    {
        get => _riskAmount;
        set
        {
            _riskAmount = Math.Max(0.01m, value);
            Refresh();
        }
    }

    [Display(Name = "RR 比例", GroupName = "风险管理", Order = 20)]
    [Range(typeof(decimal), "0.01", "9999")]
    public decimal RewardRiskRatio
    {
        get => _rewardRiskRatio;
        set
        {
            _rewardRiskRatio = Math.Max(0.01m, value);
            Refresh();
        }
    }

    [Display(Name = "UI 样式", GroupName = "界面", Order = 30)]
    public PanelStyle UiStyle
    {
        get => _uiStyle;
        set
        {
            _uiStyle = value;
            Refresh();
        }
    }

    [Display(Name = "每Tick价值($)", GroupName = "风险管理", Order = 40)]
    [Range(typeof(decimal), "0", "999999999")]
    public decimal TickValueOverride
    {
        get => _tickValueOverride;
        set
        {
            _tickValueOverride = Math.Max(0m, value);
            Refresh();
        }
    }

    public RiskManager()
        : base(useCandles: true)
    {
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Historical | DrawingLayouts.LatestBar | DrawingLayouts.Final);
        DataSeries[0].IsHidden = true;
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        DenyToChangePanel = true;
        DrawAbovePrice = true;
    }

    protected override void OnCalculate(int bar, decimal value)
    {
    }

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (ChartInfo == null || Container == null)
            return;

        DrawLevels(context);

        if (UiStyle == PanelStyle.Hud && layout == DrawingLayouts.Final)
            DrawHud(context);
    }

    public override bool ProcessMouseDown(RenderControlMouseEventArgs e)
    {
        if (ChartInfo == null || Container == null)
            return base.ProcessMouseDown(e);

        if (UiStyle == PanelStyle.Hud)
        {
            if (_cancelButtonRect.Contains(e.Location))
            {
                ResetSelection("已取消");
                return true;
            }

            if (_modeButtonRect.Contains(e.Location))
            {
                ToggleOrderMode();
                return true;
            }

            if (_panelRect.Contains(e.Location))
            {
                _isDraggingPanel = true;
                _dragOffset = new Point(e.X - _panelRect.X, e.Y - _panelRect.Y);
                return true;
            }
        }

        if (_stage is ClickStage.WaitingEntry or ClickStage.WaitingStop && Container.Region.Contains(e.Location))
        {
            HandleChartClick(e.Y);
            return true;
        }

        return base.ProcessMouseDown(e);
    }

    public override bool ProcessMouseMove(RenderControlMouseEventArgs e)
    {
        if (_isDraggingPanel && Container != null)
        {
            int x = Clamp(e.X - _dragOffset.X, Container.Region.Left, Math.Max(Container.Region.Left, Container.Region.Right - _panelRect.Width));
            int y = Clamp(e.Y - _dragOffset.Y, Container.Region.Top, Math.Max(Container.Region.Top, Container.Region.Bottom - _panelRect.Height));
            _panelRect = new Rectangle(x, y, _panelRect.Width, _panelRect.Height);
            Refresh();
            return true;
        }

        return base.ProcessMouseMove(e);
    }

    public override bool ProcessMouseUp(RenderControlMouseEventArgs e)
    {
        if (_isDraggingPanel)
        {
            _isDraggingPanel = false;
            return true;
        }

        return base.ProcessMouseUp(e);
    }

    public override StdCursor GetCursor(RenderControlMouseEventArgs e)
    {
        if (UiStyle == PanelStyle.Hud && (_cancelButtonRect.Contains(e.Location) || _modeButtonRect.Contains(e.Location) || _panelRect.Contains(e.Location)))
            return StdCursor.Hand;

        if (_stage is ClickStage.WaitingEntry or ClickStage.WaitingStop)
            return StdCursor.Cross;

        return base.GetCursor(e);
    }

    protected override void OnOrderChanged(Order order)
    {
        base.OnOrderChanged(order);

        try
        {
            if (IsTrackedOrder(order, _entryOrder, _entryOrderTag))
            {
                var status = order.Status();

                if (status == OrderStatus.Filled)
                {
                    _statusText = $"入场成交 {FormatOrderId(order)}";
                    SubmitBrackets();
                }
                else if (status == OrderStatus.Placed || order.State == OrderStates.Active)
                {
                    _statusText = $"入场已挂出 {FormatOrderId(order)}";
                    Refresh();
                }
                else if (status == OrderStatus.Canceled)
                {
                    _statusText = $"入场已取消 {FormatOrderId(order)}";
                    Refresh();
                }

                return;
            }

            if (IsTrackedOrder(order, _stopOrder, _stopOrderTag) && order.Status() == OrderStatus.Filled)
            {
                CancelSibling(_targetOrder);
                _statusText = $"SL 已成交 {FormatOrderId(order)}";
                Refresh();
                return;
            }

            if (IsTrackedOrder(order, _targetOrder, _targetOrderTag) && order.Status() == OrderStatus.Filled)
            {
                CancelSibling(_stopOrder);
                _statusText = $"TP 已成交 {FormatOrderId(order)}";
                Refresh();
            }
        }
        catch (Exception ex)
        {
            _statusText = "订单回调错误";
            Trace.TraceError($"RiskManager OnOrderChanged error: {ex}");
            Refresh();
        }
    }

    protected override void OnOrderRegisterFailed(Order order, string message)
    {
        base.OnOrderRegisterFailed(order, message);
        _statusText = $"下单失败: {TrimStatus(message)}";
        Trace.TraceError($"RiskManager order register failed: {message}");
        Refresh();
    }

    private void ToggleOrderMode()
    {
        if (_stage == ClickStage.Idle)
        {
            _stage = ClickStage.WaitingEntry;
            _entryPrice = null;
            _stopPrice = null;
            _targetPrice = null;
            _lastQuantity = null;
            _lastTickDistance = null;
            _statusText = "点击入场价";
        }
        else
        {
            ResetSelection("已取消");
        }

        Refresh();
    }

    private void HandleChartClick(int y)
    {
        if (ChartInfo?.PriceChartContainer == null)
            return;

        decimal price = ShrinkPrice(ChartInfo.PriceChartContainer.GetPriceByY(y));

        if (_stage == ClickStage.WaitingEntry)
        {
            _entryPrice = price;
            _stopPrice = null;
            _targetPrice = null;
            _lastQuantity = null;
            _lastTickDistance = null;
            _stage = ClickStage.WaitingStop;
            _statusText = "点击止损价";
            Refresh();
            return;
        }

        if (_stage != ClickStage.WaitingStop || !_entryPrice.HasValue)
            return;

        _stopPrice = price;

        if (TryBuildPlan(out OrderDirections entryDirection, out decimal quantity, out decimal targetPrice, out string error))
        {
            _targetPrice = targetPrice;
            SubmitEntry(entryDirection, quantity, targetPrice);
            _stage = ClickStage.Idle;
        }
        else
        {
            _statusText = error;
        }

        Refresh();
    }

    private bool TryBuildPlan(out OrderDirections entryDirection, out decimal quantity, out decimal targetPrice, out string error)
    {
        entryDirection = OrderDirections.Buy;
        quantity = 0m;
        targetPrice = 0m;
        error = string.Empty;

        if (!_entryPrice.HasValue || !_stopPrice.HasValue)
        {
            error = "缺少价格";
            return false;
        }

        decimal tickSize = InstrumentInfo?.TickSize ?? 0m;
        if (tickSize <= 0m)
        {
            error = "TickSize 无效";
            return false;
        }

        decimal entry = ShrinkPrice(_entryPrice.Value);
        decimal stop = ShrinkPrice(_stopPrice.Value);
        decimal market = GetCurrentPrice();

        if (entry == stop)
        {
            error = "止损距离为 0";
            return false;
        }

        if (entry < market)
            entryDirection = OrderDirections.Buy;
        else if (entry > market)
            entryDirection = OrderDirections.Sell;
        else
        {
            error = "入场价等于市价";
            return false;
        }

        if (entryDirection == OrderDirections.Buy && stop >= entry)
        {
            error = "多单止损需低于入场";
            return false;
        }

        if (entryDirection == OrderDirections.Sell && stop <= entry)
        {
            error = "空单止损需高于入场";
            return false;
        }

        decimal tickDistance = Math.Abs(entry - stop) / tickSize;
        if (tickDistance <= 0m)
        {
            error = "Tick 距离无效";
            return false;
        }

        decimal tickValue = GetTickValue();
        if (tickValue <= 0m)
        {
            error = "每Tick价值无效";
            return false;
        }

        quantity = Math.Max(1m, Math.Floor(RiskAmount / (tickDistance * tickValue)));
        decimal rawTarget = entryDirection == OrderDirections.Buy
            ? entry + (entry - stop) * RewardRiskRatio
            : entry - (stop - entry) * RewardRiskRatio;

        targetPrice = ShrinkPrice(rawTarget);
        _lastTickDistance = tickDistance;
        _lastQuantity = quantity;

        return true;
    }

    private void SubmitEntry(OrderDirections entryDirection, decimal quantity, decimal targetPrice)
    {
        if (!_entryPrice.HasValue || !_stopPrice.HasValue)
            return;

        if (!IsActivated)
        {
            _statusText = "未勾选 IsActivated";
            return;
        }

        if (Portfolio == null)
        {
            _statusText = "未选择账户";
            return;
        }

        if (Security == null)
        {
            _statusText = "未找到交易品种";
            return;
        }

        OrderDirections exitDirection = entryDirection == OrderDirections.Buy
            ? OrderDirections.Sell
            : OrderDirections.Buy;

        decimal entry = ShrinkPrice(_entryPrice.Value);
        decimal stop = ShrinkPrice(_stopPrice.Value);
        string group = $"RM-{DateTime.UtcNow:HHmmssfff}";
        _entryOrderTag = $"{group}-ENTRY";
        _stopOrderTag = $"{group}-SL";
        _targetOrderTag = $"{group}-TP";

        _entryOrder = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = entryDirection,
            Type = OrderTypes.Limit,
            Price = entry,
            QuantityToFill = quantity,
            Comment = _entryOrderTag
        };

        _stopOrder = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = exitDirection,
            Type = OrderTypes.Stop,
            TriggerPrice = stop,
            QuantityToFill = quantity,
            Comment = _stopOrderTag
        };

        _targetOrder = new Order
        {
            Portfolio = Portfolio,
            Security = Security,
            Direction = exitDirection,
            Type = OrderTypes.Limit,
            Price = targetPrice,
            QuantityToFill = quantity,
            Comment = _targetOrderTag
        };

        _bracketsSubmitted = false;

        try
        {
            OpenOrder(_entryOrder);
            _statusText = $"入场请求已发出 {quantity.ToString(CultureInfo.InvariantCulture)}";
        }
        catch (Exception ex)
        {
            _statusText = $"入场异常: {TrimStatus(ex.Message)}";
            Trace.TraceError($"RiskManager entry order error: {ex}");
        }
    }

    private void SubmitBrackets()
    {
        if (_bracketsSubmitted || _stopOrder == null || _targetOrder == null)
            return;

        try
        {
            OpenOrder(_stopOrder);
            OpenOrder(_targetOrder);
            _bracketsSubmitted = true;
            _statusText = "SL/TP 请求已发出";
            Refresh();
        }
        catch (Exception ex)
        {
            _statusText = $"括号单异常: {TrimStatus(ex.Message)}";
            Trace.TraceError($"RiskManager bracket orders error: {ex}");
            Refresh();
        }
    }

    private void CancelSibling(Order? order)
    {
        if (order == null)
            return;

        try
        {
            if (order.State == OrderStates.Active)
                CancelOrder(order);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"RiskManager cancel sibling error: {ex}");
        }
    }

    private void ResetSelection(string status)
    {
        _stage = ClickStage.Idle;
        _entryPrice = null;
        _stopPrice = null;
        _targetPrice = null;
        _lastQuantity = null;
        _lastTickDistance = null;
        _entryOrder = null;
        _stopOrder = null;
        _targetOrder = null;
        _entryOrderTag = string.Empty;
        _stopOrderTag = string.Empty;
        _targetOrderTag = string.Empty;
        _bracketsSubmitted = false;
        _statusText = status;
        Refresh();
    }

    private void DrawLevels(RenderContext context)
    {
        if (Container == null || ChartInfo == null)
            return;

        int left = Container.Region.Left;
        int right = Container.Region.Right;

        if (_entryPrice.HasValue)
            DrawLevel(context, _entryPrice.Value, _entryColor, DashStyle.Solid, "ENTRY");

        if (_stopPrice.HasValue)
            DrawLevel(context, _stopPrice.Value, _stopColor, DashStyle.Dash, "SL");

        if (_targetPrice.HasValue)
            DrawLevel(context, _targetPrice.Value, _targetColor, DashStyle.Dash, "TP");

        void DrawLevel(RenderContext renderContext, decimal price, Color color, DashStyle dashStyle, string text)
        {
            int y = ChartInfo.GetYByPrice(price, isStartOfPriceLevel: false);
            var pen = new RenderPen(color, 2f) { DashStyle = dashStyle };
            renderContext.DrawLine(pen, left, y, right, y);

            string label = $"-- {text} {price.ToString(CultureInfo.InvariantCulture)} --";
            Size size = renderContext.MeasureString(label, _smallFont);
            int labelX = Math.Max(left + 6, right - size.Width - 8);
            renderContext.DrawString(label, _smallFont, color, labelX, y - size.Height - 2);
        }
    }

    private void DrawHud(RenderContext context)
    {
        Rectangle panel = _panelRect;
        int pad = 10;
        int row = 21;
        int y = panel.Top + pad;

        context.FillRectangle(_panelBack, panel);
        context.DrawRectangle(new RenderPen(_panelBorder, 1f), panel);

        DrawText(context, "Risk Manager", _titleFont, _panelText, new Rectangle(panel.Left + pad, y, panel.Width - pad * 2, row), _leftFormat);
        y += row + 2;
        DrawSeparator(context, y);
        y += 8;

        DrawKeyValue(context, "SL 金额($)", RiskAmount.ToString("0.##", CultureInfo.InvariantCulture), y);
        y += row;
        DrawKeyValue(context, "RR 比例", RewardRiskRatio.ToString("0.##", CultureInfo.InvariantCulture), y);
        y += row;
        DrawSeparator(context, y);
        y += 8;

        string instrument = GetSecurityCode();
        decimal tickValue = GetTickValue();
        string tickMode = TickValueOverride > 0m ? "手动" : "自动";
        DrawKeyValue(context, "激活/账户", $"{(IsActivated ? "ON" : "OFF")} / {GetPortfolioName()}", y);
        y += row;
        DrawKeyValue(context, "品种", string.IsNullOrWhiteSpace(instrument) ? "--" : instrument, y);
        y += row;
        DrawKeyValue(context, "每Tick价值", $"${tickValue.ToString("0.##", CultureInfo.InvariantCulture)} ({tickMode})", y);
        y += row;
        DrawSeparator(context, y);
        y += 8;

        DrawKeyValue(context, "入场价", FormatPrice(_entryPrice), y);
        y += row;
        DrawKeyValue(context, "止损价", FormatPrice(_stopPrice), y);
        y += row;
        DrawKeyValue(context, "Tick 距离", _lastTickDistance?.ToString("0.##", CultureInfo.InvariantCulture) ?? "--", y);
        y += row;
        DrawKeyValue(context, "自动手数", _lastQuantity?.ToString("0.##", CultureInfo.InvariantCulture) ?? "--", y);
        y += row;
        DrawKeyValue(context, "止盈价", FormatPrice(_targetPrice), y);
        y += row;
        DrawSeparator(context, y);
        y += 8;

        DrawText(context, _statusText, _smallFont, _mutedText, new Rectangle(panel.Left + pad, y, panel.Width - pad * 2, row), _leftFormat);
        y += row + 2;

        int buttonWidth = 82;
        int buttonHeight = 25;
        _cancelButtonRect = new Rectangle(panel.Left + pad, y, buttonWidth, buttonHeight);
        _modeButtonRect = new Rectangle(panel.Right - pad - 108, y, 108, buttonHeight);
        DrawButton(context, _cancelButtonRect, "取消", _buttonDanger);
        DrawButton(context, _modeButtonRect, _stage == ClickStage.Idle ? "下单模式" : "退出模式", _stage == ClickStage.Idle ? _buttonBack : _buttonActive);

        void DrawSeparator(RenderContext renderContext, int separatorY)
        {
            renderContext.DrawLine(new RenderPen(Color.FromArgb(120, 112, 124, 145), 1f), panel.Left + pad, separatorY, panel.Right - pad, separatorY);
        }
    }

    private void DrawKeyValue(RenderContext context, string key, string value, int y)
    {
        Rectangle keyRect = new(_panelRect.Left + 10, y, 100, 20);
        Rectangle valueRect = new(_panelRect.Left + 112, y, _panelRect.Width - 122, 20);
        DrawText(context, key, _font, _mutedText, keyRect, _leftFormat);
        DrawText(context, value, _font, _panelText, valueRect, _leftFormat);
    }

    private void DrawButton(RenderContext context, Rectangle rect, string text, Color background)
    {
        context.FillRectangle(background, rect);
        context.DrawRectangle(new RenderPen(Color.FromArgb(220, 150, 160, 178), 1f), rect);
        DrawText(context, text, _font, Color.White, rect, _centerFormat);
    }

    private void DrawText(RenderContext context, string text, RenderFont font, Color color, Rectangle rect, RenderStringFormat format)
    {
        context.DrawString(text, font, color, rect, format);
    }

    private decimal GetCurrentPrice()
    {
        if (CurrentBar > 0)
            return ShrinkPrice(GetCandle(CurrentBar - 1).Close);

        return _entryPrice ?? 0m;
    }

    private string GetPortfolioName()
    {
        if (Portfolio == null)
            return "--";

        if (!string.IsNullOrWhiteSpace(Portfolio.AccountID))
            return Portfolio.AccountID;

        return Portfolio.ToString() ?? "--";
    }

    private decimal GetTickValue()
    {
        if (TickValueOverride > 0m)
            return TickValueOverride;

        string code = ExtractSymbolPrefix(GetSecurityCode());

        return code switch
        {
            "MES" => 1.25m,
            "MNQ" => 0.50m,
            "MGC" => 1.00m,
            "ES" => 12.50m,
            "NQ" => 5.00m,
            "GC" => 10.00m,
            _ => 0m
        };
    }

    private string GetSecurityCode()
    {
        string? securityCode = Security?.Code;
        if (!string.IsNullOrWhiteSpace(securityCode))
            return securityCode;

        return InstrumentInfo?.Instrument ?? string.Empty;
    }

    private static string ExtractSymbolPrefix(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        Match match = Regex.Match(code.ToUpperInvariant(), "^[A-Z]+");
        string prefix = match.Success ? match.Value : code.ToUpperInvariant();

        if (prefix.StartsWith("MNQ", StringComparison.Ordinal))
            return "MNQ";
        if (prefix.StartsWith("MES", StringComparison.Ordinal))
            return "MES";
        if (prefix.StartsWith("MGC", StringComparison.Ordinal))
            return "MGC";
        if (prefix.StartsWith("NQ", StringComparison.Ordinal))
            return "NQ";
        if (prefix.StartsWith("ES", StringComparison.Ordinal))
            return "ES";
        if (prefix.StartsWith("GC", StringComparison.Ordinal))
            return "GC";

        return prefix;
    }

    private static string FormatPrice(decimal? price)
    {
        return price?.ToString(CultureInfo.InvariantCulture) ?? "--";
    }

    private static bool IsTrackedOrder(Order order, Order? expected, string tag)
    {
        if (ReferenceEquals(order, expected))
            return true;

        return !string.IsNullOrWhiteSpace(tag) && string.Equals(order.Comment, tag, StringComparison.Ordinal);
    }

    private static string FormatOrderId(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.Id))
            return $"#{order.Id}";

        return string.Empty;
    }

    private static string TrimStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "--";

        message = message.Replace(Environment.NewLine, " ").Trim();
        return message.Length <= 30 ? message : message[..30];
    }

    private void Refresh()
    {
        RedrawChart();
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;

        return value;
    }
}
